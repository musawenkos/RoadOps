using System.Security.Claims;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoadOps.Auth;

namespace RoadOps.Mcp.Tools;

/// <summary>
/// Key roles on the MCP server: every key can use the read tools; a tool that is not annotated read-only (the same rule
/// <see cref="ToolAuditFilter"/> uses for "write") needs the editor role. Read-only keys do not even see the write tools
/// in tools/list, and a direct call returns an error the agent can read out, with an audit entry. A new write tool is
/// covered by its annotation alone.
/// </summary>
internal static class ToolRoleFilter
{
    public static McpRequestHandler<ListToolsRequestParams, ListToolsResult> HideWriteTools(
        McpRequestHandler<ListToolsRequestParams, ListToolsResult> next) =>
        async (context, cancellationToken) =>
        {
            var result = await next(context, cancellationToken);
            if (!CanWrite(context.User))
            {
                result.Tools = result.Tools.Where(IsReadOnly).ToList();
            }

            return result;
        };

    public static McpRequestHandler<CallToolRequestParams, CallToolResult> RejectWriteTools(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (context, cancellationToken) =>
        {
            if (context.MatchedPrimitive is McpServerTool tool && !IsReadOnly(tool.ProtocolTool) && !CanWrite(context.User))
            {
                var user = context.User?.Identity?.Name ?? "anonymous";
                var roles = string.Join(", ", context.User?.FindAll(ClaimTypes.Role).Select(c => c.Value) ?? []);
                context.Services?.GetService<ILoggerFactory>()?.CreateLogger(AuditLog.Category)
                    .LogInformation("Audit forbidden tool {Tool} by {User} (roles: {Roles}).", tool.ProtocolTool.Name, user, roles);

                return new CallToolResult
                {
                    IsError = true,
                    Content = [new TextContentBlock
                    {
                        Text = $"Your key is read-only, so it can't use {tool.ProtocolTool.Name}. Ask an administrator for editor access.",
                    }],
                };
            }

            return await next(context, cancellationToken);
        };

    private static bool CanWrite(ClaimsPrincipal? user) => user?.IsInRole(ApiKeyRoles.Editor) == true;

    private static bool IsReadOnly(Tool tool) => tool.Annotations?.ReadOnlyHint == true;
}
