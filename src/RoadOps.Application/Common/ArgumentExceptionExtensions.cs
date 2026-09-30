namespace RoadOps.Application.Common;

public static class ArgumentExceptionExtensions
{
    /// <summary>
    /// The validation message without the " (Parameter 'Name')" suffix .NET appends when a parameter name is given,
    /// so API clients and the agent see only the sentence meant for them.
    /// </summary>
    public static string UserMessage(this ArgumentException ex) =>
        ex.ParamName is null ? ex.Message : ex.Message.Replace($" (Parameter '{ex.ParamName}')", string.Empty);
}
