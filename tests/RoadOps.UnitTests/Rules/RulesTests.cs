using RoadOps.Application.Common;
using RoadOps.Application.Rules;

namespace RoadOps.UnitTests.Rules;

public class RecommendedActionRulesTests
{
    [Theory]
    [InlineData("None", 0, 0, RecommendedActionRules.NoAction)]
    [InlineData("Potholes", 2, 2, RecommendedActionRules.RoutineMaintenance)]
    [InlineData("Potholes", 2, 3, RecommendedActionRules.PotholeRepair)]
    [InlineData("Potholes", 3, 2, RecommendedActionRules.UrgentPotholeRepair)]
    [InlineData("potholes", 5, 5, RecommendedActionRules.UrgentPotholeRepair)]
    [InlineData("Crocodile cracking", 3, 3, RecommendedActionRules.Patching)]
    [InlineData("Crocodile cracking", 4, 4, RecommendedActionRules.Rehabilitation)]
    [InlineData("Rutting", 3, 3, RecommendedActionRules.RutFill)]
    [InlineData("Rutting", 4, 4, RecommendedActionRules.MillAndReplace)]
    [InlineData("Transverse cracking", 3, 2, RecommendedActionRules.CrackSealing)]
    [InlineData("Transverse cracking", 5, 3, RecommendedActionRules.AsphaltOverlay)]
    [InlineData("Bleeding", 3, 2, RecommendedActionRules.ApplyGrit)]
    [InlineData("Aggregate loss", 5, 5, RecommendedActionRules.Reseal)]
    [InlineData("Edge breaking", 3, 3, RecommendedActionRules.EdgeRepair)]
    [InlineData("Punchouts", 3, 2, RecommendedActionRules.FullDepthSlabRepair)]
    [InlineData("Faulting", 3, 2, RecommendedActionRules.DiamondGrinding)]
    [InlineData("Something new", 5, 5, RecommendedActionRules.RoutineMaintenance)]
    public void Recommend_FollowsTheTmh9Rules(string distress, int degree, int extent, string expected)
    {
        Assert.Equal(expected, RecommendedActionRules.Recommend(distress, degree, extent));
    }

    [Theory]
    [InlineData(RecommendedActionRules.UrgentPotholeRepair, ActionPriority.Urgent)]
    [InlineData(RecommendedActionRules.Rehabilitation, ActionPriority.High)]
    [InlineData(RecommendedActionRules.CrackSealing, ActionPriority.Low)]
    [InlineData(RecommendedActionRules.Patching, ActionPriority.Medium)]
    [InlineData(RecommendedActionRules.NoAction, ActionPriority.None)]
    [InlineData("Engineer's custom action", ActionPriority.Low)]
    public void PriorityOf_RanksUrgentWorkFirst(string action, ActionPriority expected)
    {
        Assert.Equal(expected, RecommendedActionRules.PriorityOf(action));
    }
}

public class DistressCatalogTests
{
    [Theory]
    [InlineData(" crocodile CRACKING ", "Crocodile cracking")]
    [InlineData("none", "None")]
    [InlineData("crack", null)]
    [InlineData("", null)]
    public void Canonicalise_MatchesCatalogueCaseInsensitively(string input, string? expected)
    {
        Assert.Equal(expected, DistressCatalog.Canonicalise(input));
    }
}

public class ConditionRulesTests
{
    [Theory]
    [InlineData(1.0, 1.6, ConditionTrend.Deteriorated)]
    [InlineData(1.0, 1.4, ConditionTrend.Stable)]
    [InlineData(2.0, 1.6, ConditionTrend.Stable)]
    [InlineData(3.0, 0.5, ConditionTrend.Rehabilitated)]
    public void Classify_UsesHalfADegreeThreshold(double before, double after, ConditionTrend expected)
    {
        Assert.Equal(expected, ConditionRules.Classify(before, after));
    }

    [Fact]
    public void Classify_MissingSide_IsNotComparable()
    {
        Assert.Equal(ConditionTrend.NotComparable, ConditionRules.Classify(null, 2));
        Assert.Equal(ConditionTrend.NotComparable, ConditionRules.Classify(2, null));
    }
}

public class FieldRulesTests
{
    [Theory]
    [InlineData(" n1 ", "N1")]
    [InlineData("R21", "R21")]
    public void NormaliseCorridor_TrimsAndUppercases(string input, string expected)
    {
        Assert.Equal(expected, FieldRules.NormaliseCorridor(input, "c"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("N 1")]
    [InlineData("N1;DROP")]
    [InlineData("ABCDEFGHIJKLM")]
    public void NormaliseCorridor_RejectsInvalidCodes(string input)
    {
        Assert.Throws<ArgumentException>(() => FieldRules.NormaliseCorridor(input, "c"));
    }
}

public class GeoTests
{
    [Fact]
    public void DistanceM_OneThousandthOfADegreeOfLatitude_IsAbout111Metres()
    {
        Assert.InRange(Geo.DistanceM(-25.0, 28.0, -25.001, 28.0), 110, 112);
    }

    [Theory]
    [InlineData(-25.0005, 28.0, 0.5)]
    [InlineData(-25.0, 28.0, 0)]
    [InlineData(-25.001, 28.0, 1)]
    [InlineData(-25.0005, 28.0003, 0.5)] // off to the side still projects onto the middle
    [InlineData(-24.9995, 28.0, -0.5)]   // before A
    public void ProjectOntoSegment_ReturnsFractionAlongTheLine(double lat, double lon, double expected)
    {
        Assert.Equal(expected, Geo.ProjectOntoSegment(lat, lon, -25.0, 28.0, -25.001, 28.0), 3);
    }
}
