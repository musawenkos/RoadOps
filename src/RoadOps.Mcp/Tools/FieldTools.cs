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
/// Field tools for an inspector on site. The only writes the agent can make: log, attach a photo, correct, and void
/// within 10 minutes. There are deliberately no delete tools and no tools to create or rename surveys or sections.
/// Who is acting always comes from authentication, never from arguments.
/// </summary>
[McpServerToolType]
public sealed class FieldTools(FieldObservationService field)
{
    [McpServerTool(Name = "log_observation", Title = "Log a field observation", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("""
        Logs a road defect the inspector is standing at. Pass the phone's GPS position; the server works out the survey,
        section and km. Two steps: call without confirm to get the read-back (or a list of what is still missing, which
        you must ask the inspector for), read it back, and only after the inspector says yes call again with the same
        values and confirm=true. Use a distress name from list_distress_types.
        """)]
    public Task<string> LogObservation(
        RequestContext<CallToolRequestParams> context,
        [Description("GPS latitude of the inspector's phone, decimal degrees.")] double? latitude = null,
        [Description("GPS longitude of the inspector's phone, decimal degrees.")] double? longitude = null,
        [Description("Chainage in km, only if there is no GPS position (then corridor is required).")] double? km = null,
        [Description("Corridor code, e.g. \"N1\". Optional with GPS.")] string? corridor = null,
        [Description("Survey year. Optional; defaults to the active survey.")] int? year = null,
        [Description("TMH9 distress type, e.g. \"Potholes\", \"Crocodile cracking\".")] string? distressType = null,
        [Description("Degree (severity) 1-5.")] int? degree = null,
        [Description("Extent (how much of the area is affected) 1-5.")] int? extent = null,
        [Description("Rut depth in mm, if measured.")] double? rutDepthMm = null,
        [Description("Defect length in metres, if measured.")] double? lengthM = null,
        [Description("Defect width in metres, if measured.")] double? widthM = null,
        [Description("Defect depth in mm, if measured.")] double? depthMm = null,
        [Description("Short note from the inspector (max 1000 characters).")] string? notes = null,
        [Description("Only true after the inspector confirmed the read-back.")] bool confirm = false,
        CancellationToken cancellationToken = default) =>
        ToolGuard.RunAsync(async () =>
        {
            var caller = Caller.NameOf(context.User);
            var survey = corridor is null && year is null ? null : new SurveyReference(corridor, year);
            var result = await field.LogObservationAsync(new LogObservationRequest(
                latitude, longitude, km, survey, distressType, degree, extent, rutDepthMm, lengthM, widthM, depthMm, notes, confirm),
                caller, cancellationToken);

            var d = result.Draft;
            var where = $"{Km(d.ChainageFrom)} on {d.Survey.Corridor}, section {d.Section.SectionName} ({d.Survey.SurveyYear} survey)";
            return result.Status switch
            {
                LogObservationStatus.NeedsInput =>
                    $"Location: {where}. Still needed before logging: {string.Join(", ", result.Missing)}. Ask the inspector, then call again.",
                LogObservationStatus.AwaitingConfirmation =>
                    $"Read this back and ask the inspector to confirm: {Describe(d)} at {where}. Recommended action: {d.RecommendedAction}. " +
                    "If they say yes, call log_observation again with the same values and confirm=true. Nothing has been saved yet.",
                LogObservationStatus.AlreadySaved =>
                    $"This observation was already saved moments ago (ID {result.Observation!.Id}); nothing new was logged.",
                _ =>
                    $"Saved. Observation ID {result.Observation!.Id}: {Describe(d)} at {where}. Recommended action: {result.Observation.RecommendedAction}. " +
                    "The inspector can now attach photos or add measurements and notes.",
            };
        });

    [McpServerTool(Name = "attach_photo", Title = "Attach a photo", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Attaches a photo the inspector uploaded (use the photo ID the app gives you) to an observation. Without an observation ID it uses the inspector's latest observation from the last hour.")]
    public Task<string> AttachPhoto(
        RequestContext<CallToolRequestParams> context,
        [Description("Photo ID returned by the upload.")] string photoId,
        [Description("Observation ID. Omit to use the inspector's latest observation.")] string? observationId = null,
        CancellationToken cancellationToken = default) =>
        ToolGuard.RunAsync(async () =>
        {
            var result = await field.AttachPhotoAsync(photoId, observationId, Caller.NameOf(context.User), cancellationToken);
            var o = result.Observation;
            return result.WasAlreadyAttached
                ? $"That photo is already attached to the {o.DistressType} observation at {Km(o.ChainageFrom)}."
                : $"Photo attached to the {o.DistressType} observation at {Km(o.ChainageFrom)} (ID {o.Id}). It now has {o.ImagePaths.Length} photo(s).";
        });

    [McpServerTool(Name = "update_observation", Title = "Correct or add to an observation", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Adds or corrects measurements, notes, degree or extent on one of the inspector's own observations (default: their latest one from the last hour). Only changes the values given; notes are appended unless replaceNotes is true. The recommended action is recalculated.")]
    public Task<string> UpdateObservation(
        RequestContext<CallToolRequestParams> context,
        [Description("Observation ID. Omit to use the inspector's latest observation.")] string? observationId = null,
        [Description("Corrected degree 1-5.")] int? degree = null,
        [Description("Corrected extent 1-5.")] int? extent = null,
        [Description("Rut depth in mm.")] double? rutDepthMm = null,
        [Description("Defect length in metres.")] double? lengthM = null,
        [Description("Defect width in metres.")] double? widthM = null,
        [Description("Defect depth in mm.")] double? depthMm = null,
        [Description("Note to add (max 1000 characters in total).")] string? notes = null,
        [Description("Replace the existing notes instead of appending.")] bool replaceNotes = false,
        CancellationToken cancellationToken = default) =>
        ToolGuard.RunAsync(async () =>
        {
            var change = await field.UpdateObservationAsync(observationId,
                new ObservationPatch(degree, extent, rutDepthMm, lengthM, widthM, depthMm, notes, AppendNotes: !replaceNotes),
                Caller.NameOf(context.User), cancellationToken);

            var (before, after) = (change.Before, change.After);
            var changes = new List<string>();
            if (before.Degree != after.Degree) changes.Add($"degree {before.Degree} to {after.Degree}");
            if (before.Extent != after.Extent) changes.Add($"extent {before.Extent} to {after.Extent}");
            if (before.RutDepthMm != after.RutDepthMm) changes.Add($"rut depth {Num(after.RutDepthMm)} mm");
            var size = Measurements(
                after.LengthM != before.LengthM ? after.LengthM : null,
                after.WidthM != before.WidthM ? after.WidthM : null,
                after.DepthMm != before.DepthMm ? after.DepthMm : null);
            if (size.Length > 0) changes.Add(size);
            if (before.Notes != after.Notes) changes.Add(replaceNotes ? "notes replaced" : "note added");

            var text = new StringBuilder($"Updated the {after.DistressType} observation at {Km(after.ChainageFrom)} (ID {after.Id}): ");
            text.Append(changes.Count == 0 ? "no values changed" : string.Join(", ", changes)).Append('.');
            if (before.RecommendedAction != after.RecommendedAction) text.Append($" Recommended action is now: {after.RecommendedAction}.");
            return text.ToString();
        });

    [McpServerTool(Name = "void_observation", Title = "Void a mistaken observation", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Voids (withdraws) an observation logged by mistake. Only the inspector who logged it can do this, and only within 10 minutes of logging. It is a soft void, not a delete. Always confirm with the inspector first and pass the observation ID.")]
    public Task<string> VoidObservation(
        RequestContext<CallToolRequestParams> context,
        [Description("ID of the observation to void (returned when it was logged).")] string observationId,
        CancellationToken cancellationToken = default) =>
        ToolGuard.RunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(observationId))
            {
                throw new ArgumentException("Give the ID of the observation to void.");
            }

            var result = await field.VoidObservationAsync(observationId, Caller.NameOf(context.User), cancellationToken);
            var o = result.Observation;
            return $"Voided the {o.DistressType} observation at {Km(o.ChainageFrom)} (ID {o.Id}). It no longer appears in any results.";
        });

    private static string Describe(ObservationDraft d)
    {
        var text = new StringBuilder($"{d.DistressType}, degree {d.Degree}, extent {d.Extent}");
        if (d.RutDepthMm is { } rut) text.Append($", rut {Num(rut)} mm");
        var size = Measurements(d.LengthM, d.WidthM, d.DepthMm);
        if (size.Length > 0) text.Append(", ").Append(size);
        if (d.Notes is not null) text.Append(", with a note");
        return text.ToString();
    }
}
