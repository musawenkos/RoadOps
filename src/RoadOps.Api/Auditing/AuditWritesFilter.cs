using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using RoadOps.Auth;

namespace RoadOps.Api.Auditing;

/// <summary>
/// Writes one audit entry per write request (POST, PUT, DELETE) that reaches a controller: who, which action, the
/// route id and body fields, the outcome and the duration. It uses the same <see cref="AuditLog"/> category and format
/// as the MCP server, so field and admin changes land in one trail. Note text is logged only as its length, and the
/// ignored createdBy body field is left out. Requests stopped earlier (401 without a valid key, 429 rate limited)
/// never reach a controller; the authentication handler and the rate limiter log those as warnings.
/// </summary>
public sealed class AuditWritesFilter(ILoggerFactory loggers) : IAsyncActionFilter
{
    private static readonly IReadOnlySet<string> Skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "createdBy" };

    private readonly ILogger _audit = loggers.CreateLogger(AuditLog.Category);

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method))
        {
            await next();
            return;
        }

        var action = context.ActionDescriptor is ControllerActionDescriptor d ? $"{d.ControllerName}.{d.ActionName}" : context.ActionDescriptor.DisplayName;
        var user = context.HttpContext.User.Identity?.Name ?? "anonymous";
        var arguments = AuditLog.DescribeArguments(Flatten(context.ActionArguments), Skipped);
        var started = Stopwatch.GetTimestamp();

        var executed = await next();

        string outcome, result;
        if (executed.Exception is { } ex && !executed.ExceptionHandled)
        {
            // The exception handler turns it into the response later (e.g. 409 for a duplicate); record what failed.
            (outcome, result) = ("failed", ex.GetType().Name);
        }
        else
        {
            var status = (executed.Result as IStatusCodeActionResult)?.StatusCode ?? context.HttpContext.Response.StatusCode;
            outcome = status switch { < 400 => "ok", < 500 => "rejected", _ => "failed" };
            result = $"{status}{Detail(executed.Result)}";
        }

        _audit.LogInformation(
            "Audit {Action} by {User}: {Outcome} in {ElapsedMs:0} ms. {Method} {Path}. Arguments: {Arguments}. Result: {Result}",
            action, user, outcome, Stopwatch.GetElapsedTime(started).TotalMilliseconds, request.Method, request.Path, arguments, result);
    }

    /// <summary>Route values as they are; a body DTO is expanded into its fields (camelCase, as the client sent them).</summary>
    private static IEnumerable<KeyValuePair<string, JsonElement>> Flatten(IDictionary<string, object?> arguments)
    {
        foreach (var (name, value) in arguments)
        {
            if (value is null or CancellationToken)
            {
                continue;
            }

            var json = JsonSerializer.SerializeToElement(value, value.GetType(), JsonSerializerOptions.Web);
            if (json.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in json.EnumerateObject())
                {
                    yield return new(property.Name, property.Value);
                }
            }
            else
            {
                yield return new(name, json);
            }
        }
    }

    /// <summary>The created id, or the validation message the client got back.</summary>
    private static string Detail(IActionResult? result) => result switch
    {
        CreatedAtActionResult { RouteValues: { } values } when values.TryGetValue("id", out var id) => $", id {id}",
        ObjectResult { Value: string message } => $", {AuditLog.Shorten(message)}",
        _ => string.Empty,
    };
}
