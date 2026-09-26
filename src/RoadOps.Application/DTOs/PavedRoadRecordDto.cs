using RoadOps.Domain.Enum;

namespace RoadOps.Application.DTOs;

public class PavedRoadRecordDto
{
    public string Id { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string SectionId { get; set; } = string.Empty;
    public double ChainageFrom { get; set; }
    public double ChainageTo { get; set; }
    public SurfaceType SurfaceType { get; set; }
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
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class CreatePavedRoadRecordDto
{
    public string WorkspaceId { get; set; } = string.Empty;
    public string SectionId { get; set; } = string.Empty;
    public double ChainageFrom { get; set; }
    public double ChainageTo { get; set; }
    public SurfaceType SurfaceType { get; set; }
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
    public string CreatedBy { get; set; } = string.Empty;
}

public class UpdatePavedRoadRecordDto
{
    public double ChainageFrom { get; set; }
    public double ChainageTo { get; set; }
    public SurfaceType SurfaceType { get; set; }
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
}
