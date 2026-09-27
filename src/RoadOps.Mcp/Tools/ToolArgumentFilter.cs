using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RoadOps.Mcp.Tools;

/// <summary>
/// Rejects tool calls that pass arguments the tool does not declare. The SDK would silently ignore them; rejecting
/// them stops an agent from believing an unsupported argument (e.g. "createdBy" or "workspaceId") took effect.
/// </summary>
internal static class ToolArgumentFilter
{
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> RejectUnknownArguments(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (context, cancellationToken) =>
        {
            if (context.MatchedPrimitive is McpServerTool tool && context.Params?.Arguments is { Count: > 0 } arguments)
            {
                var allowed = DeclaredArguments(tool.ProtocolTool.InputSchema);
                var unknown = arguments.Keys.Where(k => !allowed.Contains(k)).ToList();
                if (unknown.Count > 0)
                {
                    return new CallToolResult
                    {
                        IsError = true,
                        Content = [new TextContentBlock
                        {
                            Text = $"Unknown argument(s) for {tool.ProtocolTool.Name}: {string.Join(", ", unknown)}. " +
                                   $"Allowed: {string.Join(", ", allowed.Order())}.",
                        }],
                    };
                }
            }

            return await next(context, cancellationToken);
        };

    private static HashSet<string> DeclaredArguments(JsonElement schema) =>
        schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object
            ? properties.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal)
            : [];
}
