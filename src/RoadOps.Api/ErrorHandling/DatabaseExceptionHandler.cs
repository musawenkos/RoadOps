using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace RoadOps.Api.ErrorHandling;

/// <summary>
/// Turns PostgreSQL constraint violations into client errors instead of 500s.
/// Services validate references up front; this covers the remaining race (e.g. a workspace deleted
/// between the existence check and the insert).
/// </summary>
public sealed class DatabaseExceptionHandler(ILogger<DatabaseExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DbUpdateException { InnerException: PostgresException postgres })
        {
            return false;
        }

        var (status, title) = postgres.SqlState switch
        {
            PostgresErrorCodes.ForeignKeyViolation => (StatusCodes.Status400BadRequest, "A referenced workspace or road section does not exist."),
            PostgresErrorCodes.UniqueViolation => (StatusCodes.Status409Conflict, "A record with the same key already exists."),
            _ => (0, string.Empty)
        };

        if (status == 0)
        {
            return false;
        }

        logger.LogWarning("Database constraint {Constraint} violated: {SqlState}", postgres.ConstraintName, postgres.SqlState);

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = postgres.ConstraintName is null ? null : $"Constraint: {postgres.ConstraintName}"
        }, cancellationToken);
        return true;
    }
}
