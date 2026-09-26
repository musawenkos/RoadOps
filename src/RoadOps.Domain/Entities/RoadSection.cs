namespace RoadOps.Domain.Entities;

public sealed class RoadSection
{
    public string Id { get; set; } = string.Empty;
    public string SectionName { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;

    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

}