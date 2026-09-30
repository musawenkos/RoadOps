using System.Diagnostics;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoadOps.Auth;

namespace RoadOps.Mcp.Tools;

/// <summary>
/// Writes one audit entry per call to a tool that can change data (any tool not annotated read-only): who, which tool,
/// the arguments, the outcome and the duration, in the shared <see cref="AuditLog"/> format and category (the REST API
/// audits its writes the same way). Note text is logged only as its length. The database records CreatedBy and VoidedBy on the observation, but not who made a correction, so this log
/// is the only record of update_observation calls.
/// </summary>
internal static class ToolAuditFilter
{
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> AuditWrites(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (context, cancellationToken) =>
        {
            if (context.MatchedPrimitive is not McpServerTool tool || tool.ProtocolTool.Annotations?.ReadOnlyHint == true)
            {
                return await next(context, cancellationToken);
            }

            var logger = context.Services?.GetService<ILoggerFactory>()?.CreateLogger(AuditLog.Category);
            var user = context.User?.Identity?.Name ?? "anonymous";
            var arguments = AuditLog.DescribeArguments(context.Params?.Arguments);
            var started = Stopwatch.GetTimestamp();
            try
            {
                var result = await next(context, cancellationToken);
                var outcome = result.IsError == true ? "rejected" : "ok";
                var detail = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
                logger?.LogInformation(
                    "Audit {Tool} by {User}: {Outcome} in {ElapsedMs:0} ms. Arguments: {Arguments}. Result: {Result}",
                    tool.ProtocolTool.Name, user, outcome, Stopwatch.GetElapsedTime(started).TotalMilliseconds, arguments, AuditLog.Shorten(detail));
                return result;
            }
            catch (Exception ex)
            {
                var outcome = ex is ToolInputException ? "rejected" : "failed";
                logger?.LogInformation(
                    "Audit {Tool} by {User}: {Outcome} in {ElapsedMs:0} ms. Arguments: {Arguments}. Result: {Result}",
                    tool.ProtocolTool.Name, user, outcome, Stopwatch.GetElapsedTime(started).TotalMilliseconds, arguments, AuditLog.Shorten(ex.Message));
                throw;
            }
        };
}
