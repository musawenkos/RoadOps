using RoadOps.Application.Analytics;
using RoadOps.Application.DTOs;
using RoadOps.Domain.Enum;

namespace RoadOps.Application.Field;

/// <summary>
/// A spoken observation from the field. Location is a GPS position (preferred) or a km on a named survey.
/// Nothing is saved until <see cref="Confirm"/> is true, so the observation can be read back to the inspector first.
/// </summary>
public sealed record LogObservationRequest(
    double? Latitude = null,
    double? Longitude = null,
    double? Km = null,
    SurveyReference? Survey = null,
    string? DistressType = null,
    int? Degree = null,
    int? Extent = null,
    double? RutDepthMm = null,
    double? LengthM = null,
    double? WidthM = null,
    double? DepthMm = null,
    string? Notes = null,
    bool Confirm = false);

public enum LogObservationStatus
{
    /// <summary>Something required is missing (see <see cref="LogObservationResult.Missing"/>); ask the inspector.</summary>
    NeedsInput,
    /// <summary>Everything is known; read the draft back and call again with Confirm = true.</summary>
    AwaitingConfirmation,
    Saved,
    /// <summary>An identical observation was saved moments ago (e.g. a retried confirmation); nothing new was saved.</summary>
    AlreadySaved
}

/// <summary>What would be (or was) logged, resolved from the GPS position or km.</summary>
public sealed record ObservationDraft(
    WorkspaceOverview Survey,
    SectionRef Section,
    double ChainageFrom,
    double ChainageTo,
    SurfaceType SurfaceType,
    double Latitude,
    double Longitude,
    double? DistanceFromRoadM,
    string? DistressType,
    int? Degree,
    int? Extent,
    double? RutDepthMm,
    double? LengthM,
    double? WidthM,
    double? DepthMm,
    string? Notes,
    string? RecommendedAction);

public sealed record LogObservationResult(
    LogObservationStatus Status,
    ObservationDraft Draft,
    IReadOnlyList<string> Missing,
    PavedRoadRecordDto? Observation);

/// <summary>Fields to add or correct. Null means "leave unchanged".</summary>
public sealed record ObservationPatch(
    int? Degree = null,
    int? Extent = null,
    double? RutDepthMm = null,
    double? LengthM = null,
    double? WidthM = null,
    double? DepthMm = null,
    string? Notes = null,
    bool AppendNotes = true)
{
    public bool IsEmpty => Degree is null && Extent is null && RutDepthMm is null && LengthM is null && WidthM is null &&
                           DepthMm is null && string.IsNullOrWhiteSpace(Notes);
}

public sealed record ObservationChange(PavedRoadRecordDto Before, PavedRoadRecordDto After);

public sealed record PhotoAttachment(PavedRoadRecordDto Observation, string PhotoId, bool WasAlreadyAttached);

public sealed record VoidedObservation(PavedRoadRecordDto Observation, DateTimeOffset VoidedAt);

public sealed record UploadedPhoto(string Id, string ContentType, long SizeBytes, string Sha256);
