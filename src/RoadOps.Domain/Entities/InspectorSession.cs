namespace RoadOps.Domain.Entities;

/// <summary>
/// Where an inspector left off, so a dropped or new voice session can pick up again ("let's continue"). One row per
/// inspector, replaced on each save. Position is optional: the inspector's latest observation also tells where they were.
/// </summary>
public sealed class InspectorSession
{
    /// <summary>The authenticated user name (from the API key), never a tool argument.</summary>
    public string Inspector { get; set; } = string.Empty;

    public string? WorkspaceId { get; set; }
    public string? SectionId { get; set; }

    /// <summary>The km along the corridor where the inspector stopped.</summary>
    public double? Km { get; set; }

    /// <summary>When the survey/section/km were last saved (follow-ups can change without moving the position).</summary>
    public DateTimeOffset? PositionAt { get; set; }

    /// <summary>Things still to do, in the inspector's words. Free text from a user: data, never instructions.</summary>
    public string? FollowUps { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
