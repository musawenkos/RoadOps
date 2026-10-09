using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoadOps.Application.Analytics;
using RoadOps.Application.Field;
using RoadOps.Application.Services;
using RoadOps.Mcp.Auth;
using static RoadOps.Mcp.Tools.Speech;

namespace RoadOps.Mcp.Tools;

/// <summary>
/// Memory across voice sessions: where the inspector left off and what they still have to do. Each inspector only ever
/// sees and changes their own summary; who they are comes from authentication.
/// </summary>
[McpServerToolType]
public sealed class SessionTools(SessionMemoryService sessions, TimeProvider time)
{
    [McpServerTool(Name = "get_session_summary", Title = "Where did I leave off?", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("""
        Where the inspector left off: survey, section and km (from what they saved or their latest observation, whichever
        is newer), how many poor defects the previous survey had from there to the section end, their saved follow-ups and
        their latest observation. Call it at the start of a session or when they say "let's continue".
        """)]
    public Task<string> GetSessionSummary(RequestContext<CallToolRequestParams> context, CancellationToken cancellationToken = default) =>
        ToolGuard.RunAsync(async () =>
        {
            var summary = await sessions.GetSummaryAsync(Caller.NameOf(context.User), cancellationToken);
            return summary.IsEmpty
                ? $"No saved session and no observations from this inspector in the last {SessionMemoryService.LatestObservationLookBack.TotalDays:F0} days. Ask which survey they are working on."
                : Describe(summary, time.GetUtcNow(), quoteFollowUps: true);
        });

    [McpServerTool(Name = "save_session_summary", Title = "Remember where I am", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("""
        Saves where the inspector is and what they still have to do, so the next session can pick up from there. Use it
        when they stop or take a break, or ask you to remember something. Position: the phone's GPS, or a km (on the
        named survey, or the one already saved), or only a survey. followUps replaces the saved follow-ups, so include
        any earlier ones that still apply.
        """)]
    public Task<string> SaveSessionSummary(
        RequestContext<CallToolRequestParams> context,
        [Description("GPS latitude of the inspector's phone, decimal degrees.")] double? latitude = null,
        [Description("GPS longitude of the inspector's phone, decimal degrees.")] double? longitude = null,
        [Description("Km where the inspector stopped, if there is no GPS position.")] double? km = null,
        [Description("Corridor code, e.g. \"N1\". Optional with GPS or when a survey is already saved.")] string? corridor = null,
        [Description("Survey year. Optional; defaults to the latest survey of the corridor.")] int? year = null,
        [Description("What the inspector still has to do, in their words (max 1000 characters). Replaces the saved follow-ups.")] string? followUps = null,
        [Description("Remove the saved follow-ups (e.g. when they are all done).")] bool clearFollowUps = false,
        CancellationToken cancellationToken = default) =>
        ToolGuard.RunAsync(async () =>
        {
            var survey = corridor is null && year is null ? null : new SurveyReference(corridor, year);
            var summary = await sessions.SaveAsync(
                new SaveSessionRequest(latitude, longitude, km, survey, followUps, clearFollowUps), Caller.NameOf(context.User), cancellationToken);
            // The result goes into the audit log, which never holds free text, so the follow-ups aren't repeated here.
            return "Saved. " + Describe(summary, time.GetUtcNow(), quoteFollowUps: false);
        });

    private static string Describe(SessionSummary s, DateTimeOffset now, bool quoteFollowUps)
    {
        var text = new StringBuilder();
        if (s.Survey is { } survey)
        {
            text.Append(s.PositionSource == SessionPositionSource.LatestObservation ? "Last position, from the latest observation" : "Saved position");
            if (s.PositionAt is { } at) text.Append(" (").Append(Ago(now - at)).Append(')');
            text.Append($": {survey.Name} ({survey.Corridor} {survey.SurveyYear}, {survey.Status.ToString().ToLowerInvariant()})");
            if (s.Section is { } section) text.Append(", section ").Append(section.SectionName);
            if (s.Km is { } km) text.Append(", ").Append(Km(km));
            text.Append('.');
        }

        if (s.PreviousSurveyAhead is { } ahead)
        {
            text.Append($" The {ahead.SurveyYear} survey recorded {ahead.PoorCount} poor observation(s) (degree 4-5) from {Km(ahead.FromKm)} to the section end at {Km(ahead.ToKm)}.");
        }

        if (s.FollowUps is not null)
        {
            text.Append(" Follow-ups saved");
            if (s.SavedAt is { } savedAt) text.Append(' ').Append(Ago(now - savedAt));
            // Same treatment as observation notes: free text from a user is quoted as data.
            text.Append(quoteFollowUps ? ": " + QuoteNotes(s.FollowUps).Replace("Inspector note", "Inspector's own words") : ".");
        }
        else
        {
            text.Append(" No follow-ups saved.");
        }

        if (s.LatestObservation is { } o)
        {
            text.Append(" Latest observation (").Append(Ago(now - o.CreatedAt)).Append($"): {o.DistressType}, degree {o.Degree}, extent {o.Extent} at {Km(o.ChainageFrom)}, ID {o.Id}.");
        }

        return text.ToString().Trim();
    }

    /// <summary>"5 minutes ago", "3 hours ago", "2 days ago".</summary>
    internal static string Ago(TimeSpan elapsed) => elapsed switch
    {
        { TotalMinutes: < 1 } => "just now",
        { TotalMinutes: < 2 } => "1 minute ago",
        { TotalHours: < 1 } => $"{(int)elapsed.TotalMinutes} minutes ago",
        { TotalHours: < 2 } => "1 hour ago",
        { TotalDays: < 1 } => $"{(int)elapsed.TotalHours} hours ago",
        { TotalDays: < 2 } => "yesterday",
        _ => $"{(int)elapsed.TotalDays} days ago",
    };
}
