using System.Security.Claims;
using ModelContextProtocol;

namespace RoadOps.Mcp.Auth;

public static class Caller
{
    /// <summary>
    /// The authenticated inspector's user name. Write tools record it as CreatedBy; it is never taken from tool arguments.
    /// Works with any authentication scheme that sets the Name claim (API key today, OAuth later).
    /// </summary>
    public static string NameOf(ClaimsPrincipal? user)
    {
        var name = user?.Identity is { IsAuthenticated: true } identity ? identity.Name : null;
        return string.IsNullOrWhiteSpace(name)
            ? throw new McpException("You are not signed in.")
            : name;
    }
}
