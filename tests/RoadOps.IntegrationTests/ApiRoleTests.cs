using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoadOps.Application.DTOs;
using RoadOps.Tests.Shared;
using static RoadOps.Tests.Shared.ApiTestData;

namespace RoadOps.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ApiRoleTests(RoadOpsApiFactory factory)
{
    private HttpClient ClientWith(string key)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return client;
    }

    [Fact]
    public async Task Reader_CanOnlyRead()
    {
        var admin = factory.CreateClient();
        var workspace = await admin.CreateWorkspaceAsync();
        var reader = ClientWith(RoadOpsApiFactory.ReaderKey); // no role configured: reader by default

        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync($"{WorkspacesUrl}/{workspace.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync(PavedRoadRecordsUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync(WorkspacesUrl, NewWorkspace())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PutAsJsonAsync($"{WorkspacesUrl}/{workspace.Id}", Rename(workspace))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.DeleteAsync($"{WorkspacesUrl}/{workspace.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"{WorkspacesUrl}/{workspace.Id}")).StatusCode); // still there
    }

    [Fact]
    public async Task Editor_CanCreateAndUpdate_ButNotDelete()
    {
        var editor = ClientWith(RoadOpsApiFactory.EditorKey);

        var create = await editor.PostAsJsonAsync(WorkspacesUrl, NewWorkspace());
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var workspace = (await create.Content.ReadFromJsonAsync<WorkspaceDto>())!;
        Assert.Equal("api-editor", workspace.CreatedBy);
        var section = await editor.PostAsJsonAsync(RoadSectionsUrl, NewSection(workspace.Id));
        Assert.Equal(HttpStatusCode.Created, section.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await editor.PutAsJsonAsync($"{WorkspacesUrl}/{workspace.Id}", Rename(workspace))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.DeleteAsync($"{WorkspacesUrl}/{workspace.Id}")).StatusCode);
        var sectionId = (await section.Content.ReadFromJsonAsync<RoadSectionDto>())!.Id;
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.DeleteAsync($"{RoadSectionsUrl}/{sectionId}")).StatusCode);

        // An admin can delete what the editor created.
        Assert.Equal(HttpStatusCode.NoContent, (await factory.CreateClient().DeleteAsync($"{WorkspacesUrl}/{workspace.Id}")).StatusCode);
    }

    [Fact]
    public async Task ForbiddenAttempts_AreAudited()
    {
        var audit = new AuditSink();
        await using var host = factory.WithWebHostBuilder(b => b
            .UseSetting("Logging:LogLevel:RoadOps.Audit", "Information")
            .ConfigureServices(services => services.AddSingleton<ILoggerProvider>(audit)));
        var editor = host.CreateClient();
        editor.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", RoadOpsApiFactory.EditorKey);

        var response = await editor.DeleteAsync($"{WorkspacesUrl}/some-id");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var entry = Assert.Single(audit.Messages);
        Assert.Equal("Audit forbidden DELETE /api/workspaces/some-id by api-editor (roles: reader, editor).", entry);
    }

    private static UpdateWorkspaceDto Rename(WorkspaceDto workspace) => new()
    {
        Name = workspace.Name + " (renamed)", AssessmentType = workspace.AssessmentType, Corridor = workspace.Corridor,
        SurveyYear = workspace.SurveyYear, Status = workspace.Status,
    };
}
