using RoadOps.Api.Auditing;
using RoadOps.Api.ErrorHandling;
using Microsoft.EntityFrameworkCore;
using RoadOps.Application;
using RoadOps.Auth;
using RoadOps.Infrastructure;
using RoadOps.Infrastructure.Data;

// Helper for configuring API keys: prints the SHA-256 hash to put in Api:ApiKeys (the key itself is never stored).
//   dotnet run --project src/RoadOps.Api -- hash-key <key>
if (args is ["hash-key", var keyToHash])
{
    Console.WriteLine(ApiKeyAuthenticationHandler.Hash(keyToHash));
    return;
}

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Every write (POST, PUT, DELETE) is audit logged to the RoadOps.Audit category, like the MCP server's write tools.
builder.Services.AddControllers(options => options.Filters.Add<AuditWritesFilter>());
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DatabaseExceptionHandler>();

// PostgreSQL, repositories and application services (same registrations as the MCP server).
builder.Services.AddRoadOpsInfrastructure(builder.Configuration.GetConnectionString("DefaultConnection"));
builder.Services.AddRoadOpsApplication();

// Authentication: the same API-key scheme as the MCP server, with its own key list (Api:ApiKeys), so the admin keys
// that can create and rename surveys are separate from the inspectors' field keys. Every endpoint requires a key.
builder.Services.AddRoadOpsApiKeyAuthentication(builder.Configuration.GetSection("Api:ApiKeys"));
builder.Services.AddAuthorization(options => options.FallbackPolicy = options.DefaultPolicy);

// Rate limits per user (by IP without a valid key), with a stricter bucket for writes: POST, PUT and DELETE hit the
// database harder and can delete whole surveys, so a runaway script is stopped sooner.
builder.Services.AddRoadOpsRateLimits(builder.Configuration,
    new RateLimitBucket("write", request => !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method),
        builder.Configuration.GetValue($"{RateLimitOptions.SectionName}:WritesPerMinute", 60)));

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Apply pending EF Core migrations on startup (enabled by default in Development).
if (app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", app.Environment.IsDevelopment()))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<RoadOpsDbContext>().Database.Migrate();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.Logger.LogApiKeyWarnings(app.Configuration.GetSection("Api:ApiKeys"));

app.UseExceptionHandler();

app.UseHttpsRedirection();

app.UseCors("AllowAll");

app.UseAuthentication();
app.UseRateLimiter(); // after authentication, so limits are per user rather than per IP
app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposes the entry point to WebApplicationFactory in the integration and stress tests.
public partial class Program { }
