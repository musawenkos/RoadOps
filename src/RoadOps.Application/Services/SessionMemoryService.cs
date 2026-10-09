using RoadOps.Application.Analytics;
using RoadOps.Application.Common;
using RoadOps.Application.Field;
using RoadOps.Application.Repositories;
using RoadOps.Domain.Entities;

namespace RoadOps.Application.Services;

/// <summary>
/// Remembers where each inspector left off across voice sessions, which can drop at any time. The saved summary holds
/// the survey, km and follow-ups the inspector asked to remember; the inspector's latest observation fills in the
/// position when it is newer, so a session that ended without saving still resumes at the right place. Who the summary
/// belongs to always comes from authentication.
/// </summary>
public class SessionMemoryService
{
    /// <summary>Observations older than this don't count as "where you left off".</summary>
    public static readonly TimeSpan LatestObservationLookBack = TimeSpan.FromDays(30);

    private readonly IInspectorSessionRepository _sessions;
    private readonly IPavedRoadRecordRepository _records;
    private readonly IWorkspaceRepository _workspaces;
    private readonly IRoadSectionRepository _sections;
    private readonly IConditionAnalyticsRepository _analytics;
    private readonly SurveyResolver _resolver;
    private readonly SurveyLocationService _location;
    private readonly TimeProvider _time;

    public SessionMemoryService(
        IInspectorSessionRepository sessions,
        IPavedRoadRecordRepository records,
        IWorkspaceRepository workspaces,
        IRoadSectionRepository sections,
        IConditionAnalyticsRepository analytics,
        SurveyResolver resolver,
        SurveyLocationService location,
        TimeProvider time)
    {
        _sessions = sessions;
        _records = records;
        _workspaces = workspaces;
        _sections = sections;
        _analytics = analytics;
        _resolver = resolver;
        _location = location;
        _time = time;
    }

    public async Task<SessionSummary> GetSummaryAsync(string caller, CancellationToken cancellationToken = default)
    {
        RequireCaller(caller);

        var saved = await _sessions.GetAsync(caller, cancellationToken);
        var latest = await _records.GetLatestByCreatorAsync(caller, _time.GetUtcNow() - LatestObservationLookBack, cancellationToken);

        // Whichever is newer says where the inspector is: the saved position or their latest observation.
        string? workspaceId, sectionId;
        double? km;
        SessionPositionSource source;
        DateTimeOffset? positionAt;
        if (latest is not null && (saved?.PositionAt is not { } savedAt || latest.CreatedAt > savedAt))
        {
            (workspaceId, sectionId, km, source, positionAt) =
                (latest.WorkspaceId, latest.SectionId, latest.ChainageFrom, SessionPositionSource.LatestObservation, latest.CreatedAt);
        }
        else if (saved?.PositionAt is { } savedPositionAt && saved.WorkspaceId is not null)
        {
            (workspaceId, sectionId, km, source, positionAt) =
                (saved.WorkspaceId, saved.SectionId, saved.Km, SessionPositionSource.Saved, savedPositionAt);
        }
        else
        {
            (workspaceId, sectionId, km, source, positionAt) = (null, null, null, SessionPositionSource.None, null);
        }

        var survey = workspaceId is null ? null : await _workspaces.GetByIdAsync(workspaceId, cancellationToken);
        var section = sectionId is null ? null : await _sections.GetByIdAsync(sectionId, cancellationToken);
        var sessionSurvey = survey is null ? null : new SessionSurvey(survey.Id, survey.Name, survey.Corridor, survey.SurveyYear, survey.Status);
        var sectionRef = section is null ? null : new SectionRef(section.Id, section.SectionName, section.ChainageFrom, section.ChainageTo);

        var ahead = sessionSurvey is not null && sectionRef is not null && km is { } at
            ? await FindPreviousSurveyAheadAsync(sessionSurvey, sectionRef, at, cancellationToken)
            : null;

        return new SessionSummary(
            sessionSurvey,
            sectionRef,
            km,
            source,
            positionAt,
            saved?.FollowUps,
            saved?.UpdatedAt,
            latest is null ? null : PavedRoadRecordService.MapToDto(latest),
            ahead);
    }

    /// <summary>Saves what the inspector asked to remember, replacing their previous summary's values that are given.</summary>
    public async Task<SessionSummary> SaveAsync(SaveSessionRequest request, string caller, CancellationToken cancellationToken = default)
    {
        RequireCaller(caller);

        var followUps = FieldRules.CleanNotes(request.FollowUps, nameof(request.FollowUps));
        var hasPosition = request.Latitude is not null || request.Longitude is not null || request.Km is not null || request.Survey is not null;
        if (!hasPosition && followUps is null && !request.ClearFollowUps)
        {
            throw new ArgumentException(
                "Nothing to save: give the position (GPS, or a km and survey), the survey, or the follow-ups to remember.", nameof(request));
        }

        var session = await _sessions.GetAsync(caller, cancellationToken) ?? new InspectorSession { Inspector = caller };

        var now = _time.GetUtcNow();
        if (hasPosition)
        {
            (session.WorkspaceId, session.SectionId, session.Km) = await ResolvePositionAsync(request, session, cancellationToken);
            session.PositionAt = now;
        }

        if (request.ClearFollowUps)
        {
            session.FollowUps = null;
        }

        if (followUps is not null)
        {
            session.FollowUps = followUps;
        }

        session.UpdatedAt = now;
        await _sessions.SaveAsync(session, cancellationToken);

        return await GetSummaryAsync(caller, cancellationToken);
    }

    private async Task<(string WorkspaceId, string? SectionId, double? Km)> ResolvePositionAsync(
        SaveSessionRequest request, InspectorSession saved, CancellationToken cancellationToken)
    {
        if (request.Latitude is { } lat && request.Longitude is { } lon)
        {
            var fix = await _location.LocateAsync(lat, lon, request.Survey, cancellationToken);
            return (fix.Survey.Id, fix.Section?.Id, Math.Round(fix.ChainageKm, 3));
        }

        if (request.Latitude is not null || request.Longitude is not null)
        {
            throw new ArgumentException("Give both latitude and longitude.", nameof(request.Latitude));
        }

        // A km alone is on the survey already saved ("I stopped at km 3.2").
        string workspaceId;
        if (request.Survey is not null)
        {
            workspaceId = (await _resolver.ResolveSurveyAsync(request.Survey, cancellationToken)).Id;
        }
        else
        {
            workspaceId = saved.WorkspaceId
                ?? throw new ArgumentException("Name the survey (e.g. corridor N1) with the km, since none is saved yet.", nameof(request.Survey));
        }

        if (request.Km is not { } km)
        {
            return (workspaceId, null, null);
        }

        FieldRules.RequireRange(km, 0, double.MaxValue, nameof(request.Km));
        var section = await _analytics.FindSectionAtChainageAsync(workspaceId, km, cancellationToken);
        return (workspaceId, section?.Id, Math.Round(km, 3));
    }

    private async Task<PreviousSurveyAhead?> FindPreviousSurveyAheadAsync(SessionSurvey survey, SectionRef section, double km, CancellationToken cancellationToken)
    {
        if (km >= section.ChainageTo)
        {
            return null;
        }

        var surveys = await _resolver.FindSurveysAsync(new SurveyReference(survey.Corridor), cancellationToken: cancellationToken);
        var previous = surveys.Where(s => s.SurveyYear < survey.SurveyYear).MaxBy(s => s.SurveyYear);
        if (previous is null)
        {
            return null;
        }

        var aggregate = await _analytics.GetConditionAggregateAsync(new ConditionScope(previous.Id, null, km, section.ChainageTo), 1, cancellationToken);
        return new PreviousSurveyAhead(previous.SurveyYear, aggregate.PoorCount, km, section.ChainageTo);
    }

    private static void RequireCaller(string caller)
    {
        if (string.IsNullOrWhiteSpace(caller))
        {
            // Programming error: the host must pass the authenticated user.
            throw new InvalidOperationException("An authenticated caller is required.");
        }
    }
}
