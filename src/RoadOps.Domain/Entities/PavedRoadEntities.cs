using RoadOps.Domain.Enum;

namespace RoadOps.Domain.Entities;

public sealed class PavedRoadRecord
{
    public string Id { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string SectionId { get; set; } = string.Empty;

    public double ChainageFrom { get; set; }
    public double ChainageTo { get; set; }

    public SurfaceType SurfaceType { get; set; } = SurfaceType.Asphalt;
    public string DistressType { get; set; } = string.Empty;
    public int Degree { get; set; }
    public int Extent { get; set; }

    public double RutDepthMm { get; set; }
    public string RidingQuality { get; set; } = string.Empty;
    public string SkidResistance { get; set; } = string.Empty;
    public string StdRef { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;

    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string[] ImagePaths { get; set; } = [];

    /// <summary>Free-text inspector notes. Stored and returned as data only.</summary>
    public string? Notes { get; set; }

    /// <summary>Optional on-site dimensions of the defect.</summary>
    public double? LengthM { get; set; }
    public double? WidthM { get; set; }
    public double? DepthMm { get; set; }

    /// <summary>Set when the observation was withdrawn (soft void). Voided records are excluded from every query.</summary>
    public DateTimeOffset? VoidedAt { get; set; }
    public string? VoidedBy { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

