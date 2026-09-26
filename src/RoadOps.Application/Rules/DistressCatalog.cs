using RoadOps.Domain.Enum;

namespace RoadOps.Application.Rules;

/// <summary>The TMH9 distress vocabulary used by RoadOps, per surface family.</summary>
public static class DistressCatalog
{
    public const string None = "None";

    public static readonly IReadOnlyList<string> Flexible =
    [
        "Surface cracks", "Aggregate loss", "Binder condition", "Longitudinal cracking", "Bleeding",
        "Block cracking", "Transverse cracking", "Patching", "Rutting", "Edge breaking", "Undulation",
        "Crocodile cracking", "Potholes", "Pumping", "Surface failure",
    ];

    public static readonly IReadOnlyList<string> Rigid =
    [
        "Joint seal damage", "Shrinkage cracks", "Transverse cracking", "Joint spalling", "Faulting",
        "Corner breaks", "Punchouts", "Pumping",
    ];

    public static IReadOnlyList<string> For(SurfaceType surface) => surface == SurfaceType.Concrete ? Rigid : Flexible;

    public static IEnumerable<string> All => Flexible.Concat(Rigid).Append(None).Distinct();

    /// <summary>Returns the catalogue spelling of a distress name (case-insensitive), or null if it is not in the catalogue.</summary>
    public static string? Canonicalise(string? distressType)
    {
        if (string.IsNullOrWhiteSpace(distressType))
        {
            return null;
        }

        var trimmed = distressType.Trim();
        return All.FirstOrDefault(d => string.Equals(d, trimmed, StringComparison.OrdinalIgnoreCase));
    }
}
