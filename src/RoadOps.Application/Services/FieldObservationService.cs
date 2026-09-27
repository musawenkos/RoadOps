using RoadOps.Application.Analytics;
using RoadOps.Application.Common;
using RoadOps.Application.DTOs;
using RoadOps.Application.Field;
using RoadOps.Application.Repositories;
using RoadOps.Application.Rules;
using RoadOps.Domain.Entities;
using RoadOps.Domain.Enum;

namespace RoadOps.Application.Services;

/// <summary>
/// Field workflow for an inspector on site: log an observation from GPS (with read-back before saving), then attach
/// photos, add measurements or notes, or void a mistaken log. The caller's identity always comes from authentication,
/// never from tool arguments, and every rule is enforced here rather than trusted to the agent.
/// </summary>
public class FieldObservationService
{
    /// <summary>A mistaken observation can be voided by its creator for this long after logging it.</summary>
    public static readonly TimeSpan VoidWindow = TimeSpan.FromMinutes(10);

    /// <summary>"My latest observation" only looks this far back, so a follow-up never lands on yesterday's record.</summary>
    public static readonly TimeSpan LatestObservationWindow = TimeSpan.FromHours(1);

    /// <summary>A confirmation repeated within this time for the same defect is treated as a retry, not a new observation.</summary>
    public static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(2);

    private const double DuplicateDistanceKm = 0.005;
    private const double ReferenceSearchKm = 0.1;

    private readonly SurveyResolver _resolver;
    private readonly SurveyLocationService _location;
    private readonly IConditionAnalyticsRepository _analytics;
    private readonly PavedRoadRecordService _records;
    private readonly IPavedRoadRecordRepository _recordRepository;
    private readonly IWorkspaceRepository _workspaces;
    private readonly IPhotoRepository _photos;
    private readonly IRoadSectionRepository _sections;
    private readonly TimeProvider _time;

    public FieldObservationService(
        SurveyResolver resolver,
        SurveyLocationService location,
        IConditionAnalyticsRepository analytics,
        PavedRoadRecordService records,
        IPavedRoadRecordRepository recordRepository,
        IWorkspaceRepository workspaces,
        IPhotoRepository photos,
        IRoadSectionRepository sections,
        TimeProvider time)
    {
        _resolver = resolver;
        _location = location;
        _analytics = analytics;
        _records = records;
        _recordRepository = recordRepository;
        _workspaces = workspaces;
        _photos = photos;
        _sections = sections;
        _time = time;
    }

    /// <summary>
    /// Resolves survey, section and chainage from the position, checks the observation and, only when
    /// <see cref="LogObservationRequest.Confirm"/> is true and nothing is missing, saves it.
    /// </summary>
    public async Task<LogObservationResult> LogObservationAsync(LogObservationRequest request, string caller, CancellationToken cancellationToken = default)
    {
        RequireCaller(caller);

        var distress = ValidateDistress(request.DistressType);
        if (request.Degree is { } degree) FieldRules.RequireRange(degree, 1, FieldRules.MaxDegree, nameof(request.Degree));
        if (request.Extent is { } extent) FieldRules.RequireRange(extent, 1, FieldRules.MaxDegree, nameof(request.Extent));
        FieldRules.RequireRange(request.RutDepthMm, 0, FieldRules.MaxRutDepthMm, nameof(request.RutDepthMm));
        FieldRules.RequireRange(request.LengthM, 0, FieldRules.MaxLengthM, nameof(request.LengthM));
        FieldRules.RequireRange(request.WidthM, 0, FieldRules.MaxWidthM, nameof(request.WidthM));
        FieldRules.RequireRange(request.DepthMm, 0, FieldRules.MaxDepthMm, nameof(request.DepthMm));
        var notes = FieldRules.CleanNotes(request.Notes, nameof(request.Notes));

        var place = await ResolvePlaceAsync(request, cancellationToken);
        if (place.Survey.Status != WorkspaceStatus.Active)
        {
            throw new RuleViolationException(
                $"Survey '{place.Survey.Name}' is {place.Survey.Status.ToString().ToLowerInvariant()}; observations can only be logged to an active survey.");
        }

        // A measured length extends the observation along the road, but never past the end of its section.
        var chainageFrom = Math.Round(place.Km, 3);
        var chainageTo = Math.Round(Math.Min(chainageFrom + (request.LengthM ?? 0) / 1000, place.Section.ChainageTo), 3);

        var missing = new List<string>();
        if (distress is null) missing.Add("distress type");
        if (request.Degree is null) missing.Add("degree (1-5)");
        if (request.Extent is null) missing.Add("extent (1-5)");

        var draft = new ObservationDraft(
            place.Survey, place.Section, chainageFrom, chainageTo, place.Surface, place.Latitude, place.Longitude, place.DistanceFromRoadM,
            distress, request.Degree, request.Extent, request.RutDepthMm, request.LengthM, request.WidthM, request.DepthMm, notes,
            distress is not null && request.Degree is { } d && request.Extent is { } e ? RecommendedActionRules.Recommend(distress, d, e) : null);

        if (missing.Count > 0)
        {
            return new LogObservationResult(LogObservationStatus.NeedsInput, draft, missing, null);
        }

        if (!request.Confirm)
        {
            return new LogObservationResult(LogObservationStatus.AwaitingConfirmation, draft, [], null);
        }

        var duplicate = await FindRecentDuplicateAsync(draft, caller, cancellationToken);
        if (duplicate is not null)
        {
            return new LogObservationResult(LogObservationStatus.AlreadySaved, draft, [], duplicate);
        }

        var saved = await _records.CreateAsync(new CreatePavedRoadRecordDto
        {
            WorkspaceId = draft.Survey.Id,
            SectionId = draft.Section.Id,
            ChainageFrom = draft.ChainageFrom,
            ChainageTo = draft.ChainageTo,
            SurfaceType = draft.SurfaceType,
            DistressType = draft.DistressType!,
            Degree = draft.Degree!.Value,
            Extent = draft.Extent!.Value,
            RutDepthMm = draft.RutDepthMm ?? 0,
            StdRef = draft.SurfaceType == SurfaceType.Concrete ? "TMH9 Part C (Rigid)" : "TMH9 Part B (Flexible)",
            RecommendedAction = string.Empty, // derived by the shared rules
            Latitude = draft.Latitude,
            Longitude = draft.Longitude,
            Notes = draft.Notes,
            LengthM = draft.LengthM,
            WidthM = draft.WidthM,
            DepthMm = draft.DepthMm,
            CreatedBy = caller,
        }, cancellationToken);

        return new LogObservationResult(LogObservationStatus.Saved, draft, [], saved);
    }

    /// <summary>Adds or corrects measurements, notes, degree or extent on the caller's own observation.</summary>
    public async Task<ObservationChange> UpdateObservationAsync(string? observationId, ObservationPatch patch, string caller, CancellationToken cancellationToken = default)
    {
        RequireCaller(caller);
        if (patch.IsEmpty)
        {
            throw new ArgumentException("Nothing to update: give a degree, extent, rut depth, measurement or note.", nameof(patch));
        }

        var record = await ResolveObservationAsync(observationId, caller, cancellationToken);
        RequireCreator(record, caller, "change");
        await RequireActiveSurveyAsync(record, cancellationToken);

        var notes = FieldRules.CleanNotes(patch.Notes, nameof(patch.Notes));
        if (notes is not null && patch.AppendNotes && !string.IsNullOrWhiteSpace(record.Notes))
        {
            notes = record.Notes + "\n" + notes;
        }

        FieldRules.RequireRange(patch.LengthM, 0, FieldRules.MaxLengthM, nameof(patch.LengthM));

        // As when logging, a measured length extends the observation along the road (never past its section's end).
        var chainageTo = record.ChainageTo;
        if (patch.LengthM is { } lengthM)
        {
            var section = await _sections.GetByIdAsync(record.SectionId, cancellationToken);
            chainageTo = Math.Round(Math.Min(record.ChainageFrom + lengthM / 1000, section?.ChainageTo ?? record.ChainageTo), 3);
            chainageTo = Math.Max(chainageTo, record.ChainageFrom);
        }

        var before = Snapshot(record);

        var updated = await _records.UpdateAsync(record.Id, new UpdatePavedRoadRecordDto
        {
            ChainageFrom = record.ChainageFrom,
            ChainageTo = chainageTo,
            SurfaceType = record.SurfaceType,
            DistressType = record.DistressType,
            Degree = patch.Degree ?? record.Degree,
            Extent = patch.Extent ?? record.Extent,
            RutDepthMm = patch.RutDepthMm ?? record.RutDepthMm,
            RidingQuality = record.RidingQuality,
            SkidResistance = record.SkidResistance,
            StdRef = record.StdRef,
            RecommendedAction = string.Empty, // re-derived from the corrected degree and extent
            Latitude = record.Latitude,
            Longitude = record.Longitude,
            ImagePaths = record.ImagePaths,
            Notes = notes ?? record.Notes,
            LengthM = patch.LengthM ?? record.LengthM,
            WidthM = patch.WidthM ?? record.WidthM,
            DepthMm = patch.DepthMm ?? record.DepthMm,
        }, cancellationToken) ?? throw new RuleViolationException("That observation no longer exists.");

        return new ObservationChange(before, updated);
    }

    /// <summary>Links a photo the caller uploaded to an observation (by default the caller's latest one).</summary>
    public async Task<PhotoAttachment> AttachPhotoAsync(string photoId, string? observationId, string caller, CancellationToken cancellationToken = default)
    {
        RequireCaller(caller);
        FieldRules.RequireText(photoId, 36, "Photo ID is required.", nameof(photoId));

        var photo = await _photos.GetByIdAsync(photoId.Trim(), cancellationToken);
        if (photo is null || photo.UploadedBy != caller)
        {
            // Same message whether the photo is missing or someone else's, so ids can't be probed.
            throw new RuleViolationException($"You have no uploaded photo with ID '{photoId.Trim()}'.");
        }

        var record = await ResolveObservationAsync(observationId, caller, cancellationToken);
        await RequireActiveSurveyAsync(record, cancellationToken);

        var path = PhotoPath(photo.Id);
        if (photo.RecordId == record.Id)
        {
            return new PhotoAttachment(Snapshot(record), photo.Id, WasAlreadyAttached: true);
        }

        if (photo.RecordId is not null)
        {
            throw new RuleViolationException("That photo is already attached to another observation.");
        }

        if (record.ImagePaths.Length >= FieldRules.MaxImagePaths)
        {
            throw new RuleViolationException($"That observation already has the maximum of {FieldRules.MaxImagePaths} photos.");
        }

        var now = _time.GetUtcNow();
        photo.RecordId = record.Id;
        photo.AttachedAt = now;
        await _photos.UpdateAsync(photo, cancellationToken);

        record.ImagePaths = [.. record.ImagePaths, path];
        record.UpdatedAt = now;
        await _recordRepository.UpdateAsync(record, cancellationToken);

        return new PhotoAttachment(Snapshot(record), photo.Id, WasAlreadyAttached: false);
    }

    /// <summary>
    /// Withdraws a mistaken observation (soft void, never a hard delete). Only its creator can do this, and only within
    /// <see cref="VoidWindow"/> of logging it.
    /// </summary>
    public async Task<VoidedObservation> VoidObservationAsync(string? observationId, string caller, CancellationToken cancellationToken = default)
    {
        RequireCaller(caller);

        var record = await ResolveObservationAsync(observationId, caller, cancellationToken);
        RequireCreator(record, caller, "void");

        var now = _time.GetUtcNow();
        if (now - record.CreatedAt > VoidWindow)
        {
            throw new RuleViolationException(
                $"Observations can only be voided within {VoidWindow.TotalMinutes:F0} minutes of logging. Ask a supervisor to correct this one.");
        }

        record.VoidedAt = now;
        record.VoidedBy = caller;
        record.UpdatedAt = now;
        await _recordRepository.UpdateAsync(record, cancellationToken);

        return new VoidedObservation(Snapshot(record), now);
    }

    public static string PhotoPath(string photoId) => $"photos/{photoId}";

    private sealed record Place(WorkspaceOverview Survey, SectionRef Section, double Km, double Latitude, double Longitude, SurfaceType Surface, double? DistanceFromRoadM);

    private async Task<Place> ResolvePlaceAsync(LogObservationRequest request, CancellationToken cancellationToken)
    {
        if (request.Latitude is { } lat && request.Longitude is { } lon)
        {
            var fix = await _location.LocateAsync(lat, lon, request.Survey, cancellationToken);
            var section = fix.Section ?? throw new RuleViolationException(
                $"Km {fix.ChainageKm:F2} is not inside any section of survey '{fix.Survey.Name}'.");
            return new Place(fix.Survey, section, fix.ChainageKm, lat, lon, fix.SurfaceType, fix.DistanceFromRoadM);
        }

        if (request.Latitude is not null || request.Longitude is not null)
        {
            throw new ArgumentException("Give both latitude and longitude.", nameof(request.Latitude));
        }

        if (request.Km is not { } km)
        {
            throw new ArgumentException("Give the GPS position (latitude and longitude) or a km on a named survey.", nameof(request.Km));
        }

        if (request.Survey is null)
        {
            throw new ArgumentException("Name the survey (e.g. corridor N1) when giving a km instead of a GPS position.", nameof(request.Survey));
        }

        FieldRules.RequireRange(km, 0, double.MaxValue, nameof(request.Km));
        var survey = await _resolver.ResolveSurveyAsync(request.Survey, cancellationToken);
        var sectionAtKm = await _analytics.FindSectionAtChainageAsync(survey.Id, km, cancellationToken)
            ?? throw new RuleViolationException($"Km {km:F2} is not inside any section of survey '{survey.Name}'.");

        // Without GPS, take the position and surface of the nearest earlier observation at that km on the corridor.
        var reference = await FindReferenceRecordAsync(survey, km, cancellationToken)
            ?? throw new RuleViolationException($"There is no surveyed position near km {km:F2}; please give the GPS position instead.");

        return new Place(survey, new SectionRef(sectionAtKm.Id, sectionAtKm.SectionName, sectionAtKm.ChainageFrom, sectionAtKm.ChainageTo),
            km, reference.Latitude, reference.Longitude, reference.SurfaceType, null);
    }

    private async Task<PavedRoadRecord?> FindReferenceRecordAsync(WorkspaceOverview survey, double km, CancellationToken cancellationToken)
    {
        var surveys = await _resolver.FindSurveysAsync(new SurveyReference(survey.Corridor), cancellationToken: cancellationToken);
        foreach (var candidate in surveys.OrderByDescending(s => s.Id == survey.Id).ThenByDescending(s => s.SurveyYear))
        {
            var records = await _analytics.GetRecordsAtChainageAsync(candidate.Id, km, ReferenceSearchKm, 1, cancellationToken);
            if (records.Count > 0)
            {
                return records[0];
            }
        }

        return null;
    }

    private static string? ValidateDistress(string? distressType)
    {
        if (string.IsNullOrWhiteSpace(distressType))
        {
            return null;
        }

        FieldRules.RequireMaxLength(distressType, FieldRules.MaxNameLength, nameof(distressType));
        var canonical = DistressCatalog.Canonicalise(distressType);
        if (canonical is null || canonical == DistressCatalog.None)
        {
            throw new ArgumentException(
                $"Unknown distress type. Use one of: {string.Join(", ", DistressCatalog.All.Where(d => d != DistressCatalog.None))}.",
                nameof(distressType));
        }

        return canonical;
    }

    private async Task<PavedRoadRecordDto?> FindRecentDuplicateAsync(ObservationDraft draft, string caller, CancellationToken cancellationToken)
    {
        var latest = await _recordRepository.GetLatestByCreatorAsync(caller, _time.GetUtcNow() - DuplicateWindow, cancellationToken);
        return latest is not null &&
               latest.SectionId == draft.Section.Id &&
               latest.DistressType == draft.DistressType &&
               latest.Degree == draft.Degree &&
               latest.Extent == draft.Extent &&
               Math.Abs(latest.ChainageFrom - draft.ChainageFrom) <= DuplicateDistanceKm
            ? Snapshot(latest)
            : null;
    }

    /// <summary>The given observation, or the caller's most recent one when no id is given.</summary>
    private async Task<PavedRoadRecord> ResolveObservationAsync(string? observationId, string caller, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(observationId))
        {
            return await _recordRepository.GetLatestByCreatorAsync(caller, _time.GetUtcNow() - LatestObservationWindow, cancellationToken)
                ?? throw new RuleViolationException(
                    $"You haven't logged an observation in the last {LatestObservationWindow.TotalMinutes:F0} minutes; say which observation you mean.");
        }

        FieldRules.RequireMaxLength(observationId, 36, nameof(observationId));
        return await _recordRepository.GetByIdAsync(observationId.Trim(), cancellationToken)
            ?? throw new RuleViolationException($"There is no observation with ID '{observationId.Trim()}' (it may have been voided).");
    }

    private async Task RequireActiveSurveyAsync(PavedRoadRecord record, CancellationToken cancellationToken)
    {
        var workspace = await _workspaces.GetByIdAsync(record.WorkspaceId, cancellationToken);
        if (workspace is null || workspace.Status != WorkspaceStatus.Active)
        {
            throw new RuleViolationException("That observation belongs to a survey that is no longer active, so it can't be changed.");
        }
    }

    private static void RequireCreator(PavedRoadRecord record, string caller, string verb)
    {
        if (!string.Equals(record.CreatedBy, caller, StringComparison.Ordinal))
        {
            throw new RuleViolationException($"Only the inspector who logged an observation can {verb} it.");
        }
    }

    private static void RequireCaller(string caller)
    {
        if (string.IsNullOrWhiteSpace(caller))
        {
            // Programming error: the host must pass the authenticated user.
            throw new InvalidOperationException("An authenticated caller is required.");
        }
    }

    private static PavedRoadRecordDto Snapshot(PavedRoadRecord record) => PavedRoadRecordService.MapToDto(record);
}
