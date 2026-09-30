namespace RoadOps.Application.Rules;

/// <summary>How soon a recommended action should be carried out. Lower values are more urgent.</summary>
public enum ActionPriority
{
    Urgent = 0,
    High = 1,
    Medium = 2,
    Low = 3,
    None = 4
}

/// <summary>
/// TMH9-style rules that turn an observation (distress type, degree, extent) into a recommended maintenance action.
/// Shared by the REST API, the MCP tools and the synthetic data seeder so every record follows one rule set.
/// </summary>
public static class RecommendedActionRules
{
    public const string NoAction = "No action required";
    public const string RoutineMaintenance = "Routine maintenance – monitor";
    public const string UrgentPotholeRepair = "Pothole repair – urgent (within 72h)";
    public const string PotholeRepair = "Pothole repair";
    public const string Rehabilitation = "Rehabilitation – base repair and resurfacing";
    public const string Patching = "Patching";
    public const string Reseal = "Reseal";
    public const string FogSpray = "Fog spray / rejuvenator";
    public const string ApplyGrit = "Apply grit";
    public const string MillAndReplace = "Mill and replace";
    public const string RutFill = "Rut fill / levelling";
    public const string EdgeRepair = "Edge repair and shoulder maintenance";
    public const string AsphaltOverlay = "Asphalt overlay";
    public const string CrackSealing = "Crack sealing";
    public const string JointResealing = "Joint resealing";
    public const string PartialDepthRepair = "Partial-depth repair";
    public const string FullDepthSlabRepair = "Full-depth slab repair";
    public const string DiamondGrinding = "Diamond grinding and load transfer restoration";

    /// <summary>Degree × extent above this is treated as severe (structural work rather than preventive).</summary>
    public const int SevereScore = 12;

    /// <summary>Degree × extent at or below this only needs routine maintenance.</summary>
    public const int RoutineScore = 4;

    public static string Recommend(string distressType, int degree, int extent)
    {
        var distress = DistressCatalog.Canonicalise(distressType) ?? distressType;
        if (distress == DistressCatalog.None)
        {
            return NoAction;
        }

        // A pothole of degree 3 or more is a safety hazard however small its extent, so it bypasses the routine threshold.
        if (distress == "Potholes" && degree >= 3)
        {
            return UrgentPotholeRepair;
        }

        var score = degree * extent;
        if (score <= RoutineScore)
        {
            return RoutineMaintenance;
        }

        var severe = score > SevereScore;
        return distress switch
        {
            "Potholes" => PotholeRepair,
            "Crocodile cracking" or "Surface failure" or "Pumping" when severe => Rehabilitation,
            "Crocodile cracking" or "Surface failure" or "Pumping" or "Patching" => Patching,
            "Surface cracks" or "Aggregate loss" or "Binder condition" => severe ? Reseal : FogSpray,
            "Bleeding" => severe ? Reseal : ApplyGrit,
            "Rutting" or "Undulation" => severe ? MillAndReplace : RutFill,
            "Edge breaking" => EdgeRepair,
            "Block cracking" or "Transverse cracking" or "Longitudinal cracking" or "Shrinkage cracks" => severe ? AsphaltOverlay : CrackSealing,
            "Joint seal damage" => JointResealing,
            "Joint spalling" => PartialDepthRepair,
            "Corner breaks" or "Punchouts" => FullDepthSlabRepair,
            "Faulting" => DiamondGrinding,
            _ => RoutineMaintenance,
        };
    }

    public static ActionPriority PriorityOf(string recommendedAction) => recommendedAction switch
    {
        UrgentPotholeRepair => ActionPriority.Urgent,
        Rehabilitation or MillAndReplace or FullDepthSlabRepair or PotholeRepair => ActionPriority.High,
        Patching or Reseal or AsphaltOverlay or RutFill or EdgeRepair or PartialDepthRepair or DiamondGrinding => ActionPriority.Medium,
        NoAction => ActionPriority.None,
        _ => ActionPriority.Low,
    };
}
