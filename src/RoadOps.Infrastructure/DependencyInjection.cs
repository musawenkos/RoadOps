using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RoadOps.Application.Repositories;
using RoadOps.Infrastructure.Data;
using RoadOps.Infrastructure.Repositories;
using RoadOps.Infrastructure.Storage;

namespace RoadOps.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers the PostgreSQL DbContext and repositories. Shared by the REST API and the MCP server.</summary>
    public static IServiceCollection AddRoadOpsInfrastructure(this IServiceCollection services, string? connectionString)
    {
        services.AddDbContext<RoadOpsDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
        services.AddScoped<IRoadSectionRepository, RoadSectionRepository>();
        services.AddScoped<IPavedRoadRecordRepository, PavedRoadRecordRepository>();
        services.AddScoped<IConditionAnalyticsRepository, ConditionAnalyticsRepository>();
        services.AddScoped<IPhotoRepository, PhotoRepository>();
        services.AddScoped<IInspectorSessionRepository, InspectorSessionRepository>();
        return services;
    }

    /// <summary>Stores photo files in a local folder (demo). Needed by hosts that accept uploads (the MCP server).</summary>
    public static IServiceCollection AddRoadOpsLocalPhotoStorage(this IServiceCollection services, string rootPath)
    {
        services.AddSingleton<IPhotoStorage>(new LocalPhotoStorage(rootPath));
        return services;
    }
}
