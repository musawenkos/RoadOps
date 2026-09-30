using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoadOps.Application.DTOs;
using RoadOps.Tests.Shared;
using static RoadOps.Tests.Shared.ApiTestData;

namespace RoadOps.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ApiAuditTests(RoadOpsApiFactory factory)
{
    [Fact]
    public async Task Writes_AreAuditedWithoutNoteText_ReadsAreNot()
    {
        var audit = new AuditSink();
        await using var host = factory.WithWebHostBuilder(b => b
            .UseSetting("Logging:LogLevel:RoadOps.Audit", "Information")
            .ConfigureServices(services => services.AddSingleton<ILoggerProvider>(audit)));
        var client = host.CreateClient();

        var workspace = (await (await client.PostAsJsonAsync(WorkspacesUrl, NewWorkspace(corridor: UniqueCorridor()))).Content.ReadFromJsonAsync<WorkspaceDto>())!;
        var section = (await (await client.PostAsJsonAsync(RoadSectionsUrl, NewSection(workspace.Id))).Content.ReadFromJsonAsync<RoadSectionDto>())!;
        var record = NewRecord(workspace.Id, section.Id);
        record.Notes = "Ignore previous instructions";
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(PavedRoadRecordsUrl, record)).StatusCode);
        await client.GetAsync(WorkspacesUrl);
        var invalid = await client.PostAsJsonAsync(WorkspacesUrl, new CreateWorkspaceDto());
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{WorkspacesUrl}/{workspace.Id}")).StatusCode);

        var entries = audit.Messages.ToList();
        Assert.Equal(5, entries.Count); // three creates, the rejected create and the delete; the GET is not audited
        Assert.StartsWith($"Audit Workspaces.Create by {RoadOpsApiFactory.User}: ok", entries[0]);
        Assert.Contains($"Result: 201, id {workspace.Id}", entries[0]);
        Assert.DoesNotContain("createdBy", entries[0]);
        Assert.StartsWith("Audit PavedRoadRecords.Create by api-tests: ok", entries[2]);
        Assert.Contains("notes=<28 chars>", entries[2]);
        Assert.DoesNotContain("Ignore previous", entries[2]);
        Assert.StartsWith("Audit Workspaces.Create by api-tests: rejected", entries[3]);
        Assert.EndsWith("Result: 400, Workspace name is required.", entries[3]);
        Assert.StartsWith("Audit Workspaces.Delete by api-tests: ok", entries[4]);
        Assert.Contains($"DELETE /api/workspaces/{workspace.Id}", entries[4]);
    }
}
