namespace RoadOps.Application.Rules;

public enum ConditionTrend
{
    Deteriorated,
    Stable,
    Rehabilitated,
    NotComparable
}

/// <summary>Thresholds used when summarising and comparing condition.</summary>
public static class ConditionRules
{
    /// <summary>An observation with a degree at or above this counts as "poor".</summary>
    public const int PoorDegree = 4;

    public const string PoorRide = "Poor";
    public const string VeryPoorRide = "Very poor";

    /// <summary>Change in average degree between surveys that counts as a real change rather than noise.</summary>
    public const double TrendThreshold = 0.5;

    public static ConditionTrend Classify(double? baselineAvgDegree, double? currentAvgDegree)
    {
        if (baselineAvgDegree is not { } before || currentAvgDegree is not { } after)
        {
            return ConditionTrend.NotComparable;
        }

        var change = after - before;
        return change >= TrendThreshold ? ConditionTrend.Deteriorated
            : change <= -TrendThreshold ? ConditionTrend.Rehabilitated
            : ConditionTrend.Stable;
    }
}
