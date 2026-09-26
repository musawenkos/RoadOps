using RoadOps.Api.ErrorHandling;
using Microsoft.EntityFrameworkCore;
using RoadOps.Application;
using RoadOps.Infrastructure;
using RoadOps.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DatabaseExceptionHandler>();

// PostgreSQL, repositories and application services (same registrations as the MCP server).
builder.Services.AddRoadOpsInfrastructure(builder.Configuration.GetConnectionString("DefaultConnection"));
builder.Services.AddRoadOpsApplication();

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
    app.MapOpenApi();
}

app.UseExceptionHandler();

app.UseHttpsRedirection();

app.UseCors("AllowAll");

app.MapControllers();

app.Run();

// Exposes the entry point to WebApplicationFactory in the integration and stress tests.
public partial class Program { }
