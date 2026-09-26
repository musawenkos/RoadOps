namespace RoadOps.Application.DTOs;

public class RoadSectionDto
{
    public string Id { get; set; } = string.Empty;
    public string SectionName { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class CreateRoadSectionDto
{
    public string SectionName { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
}

public class UpdateRoadSectionDto
{
    public string SectionName { get; set; } = string.Empty;
}
