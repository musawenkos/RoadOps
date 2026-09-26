using RoadOps.Application.Analytics;
using RoadOps.Application.Common;
using RoadOps.Application.Repositories;
using RoadOps.Domain.Enum;

namespace RoadOps.Application.Services;

/// <summary>
/// Resolves a GPS position to a survey, section and chainage, and looks up the observations at a location.
/// Chainage comes from previously surveyed points on the same corridor (any year), so a new survey without any
/// records yet can still be located.
/// </summary>
public class SurveyLocationService
{
    /// <summary>A position further than this from every surveyed point is treated as off the corridor.</summary>
    public const double MaxDistanceFromRoadM = 300;

    public const int MaxObservations = 20;

    /// <summary>How far either side of a km to look for observations.</summary>
    public const double ObservationToleranceKm = 0.05;

    private const int ReferencePoints = 20;

    private readonly IConditionAnalyticsRepository _analytics;
    private readonly SurveyResolver _resolver;

    public SurveyLocationService(IConditionAnalyticsRepository analytics, SurveyResolver resolver)
    {
        _analytics = analytics;
        _resolver = resolver;
    }

    /// <summary>
    /// Works out where a GPS position is. With a survey, the position must lie on that survey's corridor. Without one,
    /// the nearest corridor is used and the position resolves to its latest survey (Active preferred).
    /// </summary>
    public async Task<LocationFix> LocateAsync(double latitude, double longitude, SurveyReference? survey = null, CancellationToken cancellationToken = default)
    {
        FieldRules.RequireRange(latitude, -90, 90, nameof(latitude));
        FieldRules.RequireRange(longitude, -180, 180, nameof(longitude));

        var target = survey is null ? null : await _resolver.ResolveSurveyAsync(survey, cancellationToken);

        var nearby = await _analytics.FindNearestRecordsAsync(
            new GeoQuery(latitude, longitude, MaxDistanceFromRoadM, Corridor: target?.Corridor, Limit: ReferencePoints), cancellationToken);
        if (nearby.Count == 0)
        {
            throw new ArgumentException(target is null
                ? $"This position is more than {MaxDistanceFromRoadM:F0} m from any surveyed road."
                : $"This position is more than {MaxDistanceFromRoadM:F0} m from the {target.Corridor} corridor.", nameof(latitude));
        }

        var nearest = nearby[0];
        target ??= await LatestSurveyOfAsync(nearest.Corridor, cancellationToken);

        // Use points from one survey only, so chainages come from one consistent set of measurements.
        var references = nearby.Where(r => r.WorkspaceId == nearest.WorkspaceId).ToList();
        var km = Math.Round(EstimateChainage(latitude, longitude, references), 3);

        var section = await _analytics.FindSectionAtChainageAsync(target.Id, km, cancellationToken);
        return new LocationFix(
            target,
            section is null ? null : new SectionRef(section.Id, section.SectionName, section.ChainageFrom, section.ChainageTo),
            km,
            Math.Round(nearest.DistanceM, 1),
            nearest.SurveyYear);
    }

    /// <summary>Observations of a survey at a km, or at a GPS position (resolved with <see cref="LocateAsync"/>).</summary>
    public async Task<LocationDetails> GetLocationDetailsAsync(SurveyReference? survey, double? km, double? latitude, double? longitude, int limit = 10, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > MaxObservations)
        {
            throw new ArgumentException($"Limit must be between 1 and {MaxObservations}.", nameof(limit));
        }

        LocationFix? fix = null;
        WorkspaceOverview workspace;
        double chainage;

        if (latitude is { } lat && longitude is { } lon)
        {
            fix = await LocateAsync(lat, lon, survey, cancellationToken);
            workspace = fix.Survey;
            chainage = fix.ChainageKm;
        }
        else if (km is { } k)
        {
            if (survey is null)
            {
                throw new ArgumentException("Name the survey when giving a km instead of a GPS position.", nameof(survey));
            }

            FieldRules.RequireRange(k, 0, double.MaxValue, nameof(km));
            workspace = await _resolver.ResolveSurveyAsync(survey, cancellationToken);
            chainage = k;
        }
        else
        {
            throw new ArgumentException("Give either a km or a GPS position (latitude and longitude).", nameof(km));
        }

        var section = fix?.Section;
        if (fix is null)
        {
            var s = await _analytics.FindSectionAtChainageAsync(workspace.Id, chainage, cancellationToken);
            section = s is null ? null : new SectionRef(s.Id, s.SectionName, s.ChainageFrom, s.ChainageTo);
        }

        var records = await _analytics.GetRecordsAtChainageAsync(workspace.Id, chainage, ObservationToleranceKm, limit, cancellationToken);
        return new LocationDetails(workspace, section, chainage, fix, records.Select(PavedRoadRecordService.MapToDto).ToList());
    }

    /// <summary>
    /// Projects the position onto the line between the nearest surveyed point and the nearest point at a different
    /// chainage, and interpolates the km. Record coordinates are taken as the middle of the record's km range.
    /// </summary>
    internal static double EstimateChainage(double latitude, double longitude, IReadOnlyList<NearbyRecord> references)
    {
        static double Mid(NearbyRecord r) => (r.ChainageFrom + r.ChainageTo) / 2;

        var a = references[0];
        var b = references.Skip(1).FirstOrDefault(r => Math.Abs(Mid(r) - Mid(a)) > FieldRules.ChainageTolerance);
        if (b is null)
        {
            return Mid(a);
        }

        // Allow a little extrapolation past A (the position may be beyond the last point), but not wild guesses.
        var t = Math.Clamp(Geo.ProjectOntoSegment(latitude, longitude, a.Latitude, a.Longitude, b.Latitude, b.Longitude), -1, 1);
        return Math.Max(0, Mid(a) + t * (Mid(b) - Mid(a)));
    }

    private async Task<WorkspaceOverview> LatestSurveyOfAsync(string corridor, CancellationToken cancellationToken)
    {
        var surveys = await _resolver.FindSurveysAsync(new SurveyReference(corridor), cancellationToken: cancellationToken);
        return surveys
            .OrderByDescending(s => s.Status == WorkspaceStatus.Active)
            .ThenByDescending(s => s.SurveyYear)
            .First();
    }
}
