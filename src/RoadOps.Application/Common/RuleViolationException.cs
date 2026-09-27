namespace RoadOps.Application.Common;

/// <summary>
/// A request that is well-formed but not allowed by a business rule, e.g. voiding someone else's observation or
/// voiding after the time window. The message is safe to show to the caller.
/// </summary>
public class RuleViolationException : Exception
{
    public RuleViolationException(string message) : base(message)
    {
    }
}
