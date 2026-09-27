using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace RoadOps.Mcp.Auth;

/// <summary>One inspector's API key. Only the SHA-256 hash of the key is configured, never the key itself.</summary>
public sealed class ApiKeyEntry
{
    /// <summary>The inspector's user name, recorded as CreatedBy on everything they log.</summary>
    public string User { get; set; } = string.Empty;

    /// <summary>Lower-case hex SHA-256 of the key. Generate with: dotnet run --project src/RoadOps.Mcp -- hash-key &lt;key&gt;</summary>
    public string KeyHash { get; set; } = string.Empty;
}

public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public List<ApiKeyEntry> Keys { get; set; } = [];
}

/// <summary>
/// Development/demo authentication: a per-inspector API key sent as "Authorization: Bearer &lt;key&gt;" or "X-Api-Key".
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
                    [new Claim(ClaimTypes.Name, entry.User), new Claim(ClaimTypes.NameIdentifier, entry.User), new Claim("amr", "api_key")],
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
