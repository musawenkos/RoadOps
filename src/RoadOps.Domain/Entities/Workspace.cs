using RoadOps.Domain.Enum;

namespace RoadOps.Domain.Entities;

public sealed class Workspace
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AssessmentType { get; set; } = string.Empty;

    /// <summary>Road corridor code, e.g. "N1". Surveys of the same corridor can be compared.</summary>
    public string Corridor { get; set; } = string.Empty;

    /// <summary>Year the survey campaign was carried out, e.g. 2026.</summary>
    public int SurveyYear { get; set; }

    public WorkspaceStatus Status { get; set; } = WorkspaceStatus.Active;

    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
