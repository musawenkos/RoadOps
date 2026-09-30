using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RoadOps.Mcp.Tools;

/// <summary>
/// Returns <see cref="ToolInputException"/>s as tool errors the agent can read out, logged once as a warning without a
/// stack trace. Left to the SDK they would be logged at error level as unhandled exceptions, burying real failures;
/// any other exception still reaches the SDK and is logged as an error.
/// </summary>
internal static class ToolErrorFilter
{
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> ReportInputErrors(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (context, cancellationToken) =>
        {
            try
            {
                return await next(context, cancellationToken);
            }
            catch (ToolInputException ex)
            {
                var toolName = context.Params?.Name ?? "unknown";
                context.Services?.GetService<ILoggerFactory>()?.CreateLogger(typeof(ToolErrorFilter))
                    .LogWarning("Tool {Tool} rejected the call: {Reason}", toolName, ex.Message);

                return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = ex.Message }] };
            }
        };
}
