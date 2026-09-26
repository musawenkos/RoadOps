using RoadOps.Api.ErrorHandling;
using Microsoft.EntityFrameworkCore;
using RoadOps.Application.Repositories;
using RoadOps.Application.Services;
using RoadOps.Infrastructure.Data;
using RoadOps.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DatabaseExceptionHandler>();

// Configure PostgreSQL connection
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<RoadOpsDbContext>(options =>
    options.UseNpgsql(connectionString));

// Register repositories
builder.Services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
builder.Services.AddScoped<IRoadSectionRepository, RoadSectionRepository>();
builder.Services.AddScoped<IPavedRoadRecordRepository, PavedRoadRecordRepository>();

// Register application services
builder.Services.AddScoped<WorkspaceService>();
builder.Services.AddScoped<RoadSectionService>();
builder.Services.AddScoped<PavedRoadRecordService>();

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
