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

    /// <summary>Default section range: wide enough for every chainage the tests use.</summary>
    public const double SectionFromKm = 0;
    public const double SectionToKm = 10_000;

    /// <summary>A corridor code no other test (or seeded data) uses, so aggregate queries see only this test's data.</summary>
    public static string UniqueCorridor() => "T" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    public static CreateWorkspaceDto NewWorkspace(string? name = null, string corridor = "TEST", int surveyYear = 2026) => new()
    {
        Name = name ?? $"Test workspace {Guid.NewGuid():N}",
        AssessmentType = "Visual Condition Assessment",
        Corridor = corridor,
        SurveyYear = surveyYear,
        CreatedBy = "tests"
    };

    public static CreateRoadSectionDto NewSection(string workspaceId, string? name = null, double from = SectionFromKm, double to = SectionToKm) => new()
    {
        WorkspaceId = workspaceId,
        SectionName = name ?? $"Section {Guid.NewGuid():N}",
        ChainageFrom = from,
        ChainageTo = to,
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
        Notes = "Water ponding after rain.",
        LengthM = 0.6,
        WidthM = 0.4,
        DepthMm = 45,
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

    public static Task<RoadSectionDto> CreateSectionAsync(this HttpClient client, string workspaceId, double from = SectionFromKm, double to = SectionToKm) =>
        client.PostAndReadAsync<RoadSectionDto>(RoadSectionsUrl, NewSection(workspaceId, from: from, to: to));

    public static Task<PavedRoadRecordDto> CreateRecordAsync(this HttpClient client, string workspaceId, string sectionId, double from = 10.0, double to = 10.1) =>
        client.PostAndReadAsync<PavedRoadRecordDto>(PavedRoadRecordsUrl, NewRecord(workspaceId, sectionId, from, to));
}
