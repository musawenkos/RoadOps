using Microsoft.Extensions.DependencyInjection;
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
        return services;
    }
}
