using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RoadOps.Auth;

/// <summary>One user's API key. Only the SHA-256 hash of the key is configured, never the key itself.</summary>
public sealed class ApiKeyEntry
{
    /// <summary>The user name, recorded as CreatedBy on everything they create.</summary>
    public string User { get; set; } = string.Empty;

    /// <summary>Lower-case hex SHA-256 of the key. Generate with: dotnet run --project src/RoadOps.Mcp -- hash-key &lt;key&gt; (or src/RoadOps.Api)</summary>
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>
    /// The key's role on the REST API: reader, editor or admin (see <see cref="ApiKeyRoles"/>). Missing or unknown
    /// means reader. The MCP server ignores it.
    /// </summary>
    public string? Role { get; set; }
}

/// <summary>
/// Hierarchical roles for API keys, one per key: reader (read), editor (reader + create/update), admin (editor +
/// delete). A key gets a role claim for its own role and every role below it, so an endpoint requires only the
/// minimum role, e.g. [Authorize(Roles = ApiKeyRoles.Editor)] also admits admins.
/// </summary>
public static class ApiKeyRoles
{
    public const string Reader = "reader";
    public const string Editor = "editor";
    public const string Admin = "admin";

    private static readonly string[] Hierarchy = [Reader, Editor, Admin];

    public static bool IsKnown(string? role) => role is not null && Hierarchy.Contains(role, StringComparer.OrdinalIgnoreCase);

    /// <summary>The canonical role name; missing or unknown values get the least privilege, <see cref="Reader"/>.</summary>
    public static string Resolve(string? role) =>
        Hierarchy.FirstOrDefault(r => string.Equals(r, role?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Reader;

    /// <summary>The role and every role it includes, e.g. admin gives [reader, editor, admin].</summary>
    public static IReadOnlyList<string> Expand(string? role) => Hierarchy[..(Array.IndexOf(Hierarchy, Resolve(role)) + 1)];
}

public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public List<ApiKeyEntry> Keys { get; set; } = [];
}

/// <summary>
/// Development/demo authentication: a per-user API key sent as "Authorization: Bearer &lt;key&gt;" or "X-Api-Key".
/// It produces an ordinary <see cref="ClaimsPrincipal"/> (Name = inspector), so it can later be replaced by OAuth/JWT
/// bearer authentication without touching the tools, which only read the user name from the principal.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var key = ReadKey();
        if (key is null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var presented = HashBytes(key);
        foreach (var entry in Options.Keys)
        {
            if (string.IsNullOrWhiteSpace(entry.User) || !TryParseHash(entry.KeyHash, out var expected))
            {
                continue;
            }

            if (CryptographicOperations.FixedTimeEquals(presented, expected))
            {
                var identity = new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.Name, entry.User), new Claim(ClaimTypes.NameIdentifier, entry.User), new Claim("amr", "api_key"),
                        .. ApiKeyRoles.Expand(entry.Role).Select(role => new Claim(ClaimTypes.Role, role)),
                    ],
                    SchemeName);
                return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
            }
        }

        // Never log the key itself.
        Logger.LogWarning("Rejected an invalid API key from {RemoteIp}.", Context.Connection.RemoteIpAddress);
        return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer realm=\"roadops\"";
        return Task.CompletedTask;
    }

    /// <summary>
    /// A valid key without the role an endpoint needs. Authorization stops these before any controller runs, so they
    /// are written to the audit trail here: attempts above a key's role are exactly what the audit should show.
    /// </summary>
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        var roles = Context.User.FindAll(ClaimTypes.Role).Select(c => c.Value);
        Context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(AuditLog.Category).LogInformation(
            "Audit forbidden {Method} {Path} by {User} (roles: {Roles}).",
            Request.Method, Request.Path, Context.User.Identity?.Name ?? "anonymous", string.Join(", ", roles));
        return Task.CompletedTask;
    }

    private string? ReadKey()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authorization["Bearer ".Length..].Trim();
            return token.Length > 0 ? token : null;
        }

        var header = Request.Headers[HeaderName].ToString().Trim();
        return header.Length > 0 ? header : null;
    }

    public static string Hash(string key) => Convert.ToHexStringLower(HashBytes(key));

    private static byte[] HashBytes(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));

    private static bool TryParseHash(string hex, out byte[] bytes)
    {
        bytes = [];
        if (hex.Length != 64)
        {
            return false;
        }

        try
        {
            bytes = Convert.FromHexString(hex);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public static class ApiKeyAuthenticationExtensions
{
    /// <summary>
    /// Registers API-key authentication as the default scheme, with the keys bound from <paramref name="keys"/>
    /// (a list of { User, KeyHash, Role }). Each host has its own list, so field keys and admin keys stay separate.
    /// </summary>
    public static AuthenticationBuilder AddRoadOpsApiKeyAuthentication(this IServiceCollection services, IConfigurationSection keys) =>
        services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName,
                options => keys.Bind(options.Keys));

    /// <summary>Startup warnings for a key list: no keys at all (everything is rejected), or keys without a valid role.</summary>
    public static void LogApiKeyWarnings(this ILogger logger, IConfigurationSection keys)
    {
        var entries = keys.Get<List<ApiKeyEntry>>() ?? [];
        if (entries.Count == 0)
        {
            logger.LogWarning("No API keys are configured ({Section}), so every request will be rejected. See README.", keys.Path);
        }

        foreach (var entry in entries.Where(e => !ApiKeyRoles.IsKnown(e.Role)))
        {
            logger.LogWarning("API key for {User} in {Section} has no valid Role ({Role}); it is treated as reader (read-only). Set reader, editor or admin.",
                entry.User, keys.Path, entry.Role ?? "missing");
        }
    }
}
