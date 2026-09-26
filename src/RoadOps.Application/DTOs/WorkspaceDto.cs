using RoadOps.Domain.Enum;

namespace RoadOps.Application.DTOs;

public class WorkspaceDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AssessmentType { get; set; } = string.Empty;
    public WorkspaceStatus Status { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class CreateWorkspaceDto
{
    public string Name { get; set; } = string.Empty;
    public string AssessmentType { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
}

public class UpdateWorkspaceDto
{
    public string Name { get; set; } = string.Empty;
    public string AssessmentType { get; set; } = string.Empty;
    public WorkspaceStatus Status { get; set; }
}
