using RoadOps.Application.Analytics;
using RoadOps.Application.DTOs;
using RoadOps.Domain.Enum;

namespace RoadOps.Application.Field;

/// <summary>
/// What to remember about where the inspector is. Position is a GPS position, a km (on a named survey, or the one
/// already saved), or just a survey. <see cref="FollowUps"/> replaces the saved follow-ups; <see cref="ClearFollowUps"/>
/// removes them.
/// </summary>
public sealed record SaveSessionRequest(
    double? Latitude = null,
    double? Longitude = null,
    double? Km = null,
    SurveyReference? Survey = null,
    string? FollowUps = null,
    bool ClearFollowUps = false);

public sealed record SessionSurvey(string Id, string Name, string Corridor, int SurveyYear, WorkspaceStatus Status);

public enum SessionPositionSource
{
    None,
    /// <summary>The inspector (through the agent) saved it.</summary>
    Saved,
    /// <summary>Taken from the inspector's latest observation, which is newer than anything saved.</summary>
    LatestObservation
}

/// <summary>The previous survey's poor observations (degree 4-5) from where the inspector stopped to the end of the section.</summary>
public sealed record PreviousSurveyAhead(int SurveyYear, int PoorCount, double FromKm, double ToKm);

/// <summary>Where the inspector left off, for picking up a dropped or new session.</summary>
public sealed record SessionSummary(
    SessionSurvey? Survey,
    SectionRef? Section,
    double? Km,
    SessionPositionSource PositionSource,
    DateTimeOffset? PositionAt,
    string? FollowUps,
    DateTimeOffset? SavedAt,
    PavedRoadRecordDto? LatestObservation,
    PreviousSurveyAhead? PreviousSurveyAhead)
{
    public bool IsEmpty => Survey is null && FollowUps is null && LatestObservation is null;
}
