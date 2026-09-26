using RoadOps.Domain.Enum;

namespace RoadOps.Domain.Entities;

public sealed class Workspace
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AssessmentType { get; set; } = string.Empty;

    public WorkspaceStatus Status { get; set; } = WorkspaceStatus.Active;

    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}