using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RoadOps.Application.Services;

namespace RoadOps.Application;

public static class DependencyInjection
{
    /// <summary>Registers the application services. Shared by the REST API and the MCP server.</summary>
    public static IServiceCollection AddRoadOpsApplication(this IServiceCollection services)
    {
        services.AddScoped<WorkspaceService>();
        services.AddScoped<RoadSectionService>();
        services.AddScoped<PavedRoadRecordService>();
        services.AddScoped<SurveyResolver>();
        services.AddScoped<ConditionAnalyticsService>();
        services.AddScoped<SurveyLocationService>();
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }

    /// <summary>
    /// Registers the field-work services (observation logging, photos, session memory). Requires an <c>IPhotoStorage</c>, so only hosts
    /// that accept photo uploads (the MCP server) call this.
    /// </summary>
    public static IServiceCollection AddRoadOpsFieldWork(this IServiceCollection services)
    {
        services.AddScoped<FieldObservationService>();
        services.AddScoped<PhotoService>();
        services.AddScoped<SessionMemoryService>();
        return services;
    }
}
