using System.Text.Json;

namespace RoadOps.Auth;

/// <summary>
/// Shared conventions for the audit trail of both hosts: one log category (so entries can be routed to their own sink)
/// and one way of writing arguments. Free text such as inspector notes is never copied into the log, only its length,
/// so the audit trail holds no personal text and nothing that could inject into a log viewer or an LLM reading logs.
/// </summary>
public static class AuditLog
{
    public const string Category = "RoadOps.Audit";

    private static readonly HashSet<string> FreeTextArguments = new(StringComparer.OrdinalIgnoreCase) { "notes", "followUps" };

    /// <summary>"name=value, ..." sorted by name; free text is replaced by its length and long values are shortened.</summary>
    public static string DescribeArguments(IEnumerable<KeyValuePair<string, JsonElement>>? arguments, IReadOnlySet<string>? skip = null)
    {
        var parts = (arguments ?? [])
            .Where(a => skip is null || !skip.Contains(a.Key))
            .OrderBy(a => a.Key, StringComparer.Ordinal)
            .Select(a => FreeTextArguments.Contains(a.Key) && a.Value.ValueKind == JsonValueKind.String
                ? $"{a.Key}=<{a.Value.GetString()!.Length} chars>"
                : $"{a.Key}={Shorten(a.Value.GetRawText(), 80)}")
            .ToList();
        return parts.Count == 0 ? "none" : string.Join(", ", parts);
    }

    public static string? Shorten(string? text, int max = 300) =>
        text is null || text.Length <= max ? text : text[..max] + "…";
}
