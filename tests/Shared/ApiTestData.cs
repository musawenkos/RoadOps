using System.Net.Http.Json;
using RoadOps.Application.Common;
using RoadOps.Application.DTOs;
using RoadOps.Domain.Enum;

namespace RoadOps.Tests.Shared;

/// <summary>Builders and HTTP helpers shared by the integration and stress tests.</summary>
public static class ApiTestData
{
    public const string WorkspacesUrl = "/api/workspaces";
    public const string RoadSectionsUrl = "/api/road-sections";
    public const string PavedRoadRecordsUrl = "/api/paved-road-records";

    public static CreateWorkspaceDto NewWorkspace(string? name = null) => new()
    {
        Name = name ?? $"Test workspace {Guid.NewGuid():N}",
        AssessmentType = "Visual Condition Assessment",
        CreatedBy = "tests"
    };

    public static CreateRoadSectionDto NewSection(string workspaceId, string? name = null) => new()
    {
        WorkspaceId = workspaceId,
        SectionName = name ?? $"Section {Guid.NewGuid():N}",
        CreatedBy = "tests"
    };

    public static CreatePavedRoadRecordDto NewRecord(string workspaceId, string sectionId, double from = 10.0, double to = 10.1) => new()
    {
        WorkspaceId = workspaceId,
        SectionId = sectionId,
        ChainageFrom = from,
        ChainageTo = to,
        SurfaceType = SurfaceType.Asphalt,
        DistressType = "Pothole",
        Degree = 3,
        Extent = 2,
        RutDepthMm = 15.5,
        RidingQuality = "Poor",
        SkidResistance = "Low",
        StdRef = "TMH9",
        RecommendedAction = "Patch",
        Latitude = -25.7461,
        Longitude = 28.1881,
        ImagePaths = ["images/a.jpg", "images/b.jpg"],
        CreatedBy = "tests"
    };

    public static async Task<T> PostAndReadAsync<T>(this HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    public static async Task<PagedResult<T>> GetPageAsync<T>(this HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PagedResult<T>>())!;
    }

    public static Task<WorkspaceDto> CreateWorkspaceAsync(this HttpClient client) =>
        client.PostAndReadAsync<WorkspaceDto>(WorkspacesUrl, NewWorkspace());

    public static Task<RoadSectionDto> CreateSectionAsync(this HttpClient client, string workspaceId) =>
        client.PostAndReadAsync<RoadSectionDto>(RoadSectionsUrl, NewSection(workspaceId));

    public static Task<PavedRoadRecordDto> CreateRecordAsync(this HttpClient client, string workspaceId, string sectionId, double from = 10.0, double to = 10.1) =>
        client.PostAndReadAsync<PavedRoadRecordDto>(PavedRoadRecordsUrl, NewRecord(workspaceId, sectionId, from, to));
}
