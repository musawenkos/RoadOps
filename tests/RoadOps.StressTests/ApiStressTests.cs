using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using RoadOps.Application.DTOs;
using RoadOps.Domain.Enum;
using RoadOps.Tests.Shared;
using Xunit.Abstractions;
using static RoadOps.Tests.Shared.ApiTestData;

namespace RoadOps.StressTests;

[Trait("Category", "Stress")]
public class ApiStressTests(StressTarget target, ITestOutputHelper output) : IClassFixture<StressTarget>
{
    private readonly StressSettings _settings = StressSettings.FromEnvironment();
    private HttpClient Client => target.Client;

    [Fact]
    public async Task MixedReadWriteWorkload_StaysWithinLatencyAndErrorBudget()
    {
        // Arrange: a realistic working set to read from.
        var workspace = await Client.CreateWorkspaceAsync();
        var sections = new List<RoadSectionDto>();
        for (var i = 0; i < 5; i++)
        {
            sections.Add(await Client.CreateSectionAsync(workspace.Id));
        }

        var recordIds = new ConcurrentBag<string>();
        await Parallel.ForEachAsync(Enumerable.Range(0, 200), new ParallelOptions { MaxDegreeOfParallelism = 10 }, async (i, _) =>
        {
            var from = i * 0.1;
            var record = await Client.CreateRecordAsync(workspace.Id, sections[i % sections.Count].Id, from, from + 0.1);
            recordIds.Add(record.Id);
        });
        var ids = recordIds.ToArray();
        var random = Random.Shared;
        string AnyId() => ids[random.Next(ids.Length)];
        string AnySection() => sections[random.Next(sections.Count)].Id;

        var scenarios = new List<Scenario>
        {
            new("GET record by id", 40, async c => (await c.GetAsync($"{PavedRoadRecordsUrl}/{AnyId()}")).StatusCode == HttpStatusCode.OK),
            new("GET records by section", 20, async c => (await c.GetAsync($"{PavedRoadRecordsUrl}/section/{AnySection()}")).IsSuccessStatusCode),
            new("GET chainage range", 15, async c => (await c.GetAsync($"{PavedRoadRecordsUrl}/chainage?chainageFrom=2&chainageTo=8")).IsSuccessStatusCode),
            new("POST record", 15, async c =>
            {
                var from = 100 + random.NextDouble() * 50;
                return (await c.PostAsJsonAsync(PavedRoadRecordsUrl, NewRecord(workspace.Id, AnySection(), from, from + 0.1))).StatusCode == HttpStatusCode.Created;
            }),
            new("PUT record", 10, async c =>
            {
                var from = random.NextDouble() * 20;
                var dto = new UpdatePavedRoadRecordDto { ChainageFrom = from, ChainageTo = from + 0.1, SurfaceType = SurfaceType.Asphalt, DistressType = "Rutting", Degree = random.Next(1, 6), Extent = random.Next(1, 6) };
                return (await c.PutAsJsonAsync($"{PavedRoadRecordsUrl}/{AnyId()}", dto)).StatusCode == HttpStatusCode.OK;
            }),
        };

        // Act
        var result = await LoadRunner.RunAsync($"Mixed read/write workload ({target.Description})", Client, scenarios,
            TimeSpan.FromSeconds(_settings.DurationSeconds), _settings.Concurrency);

        // Assert
        Report(result);
        Assert.True(result.Total.Requests > 0, "No requests completed.");
        Assert.True(result.Total.ErrorRate <= _settings.MaxErrorRate,
            $"Error rate {result.Total.ErrorRate:P2} exceeded budget {_settings.MaxErrorRate:P2}.");
        Assert.True(result.Total.P95 <= _settings.MaxP95Ms,
            $"p95 latency {result.Total.P95:F1} ms exceeded budget {_settings.MaxP95Ms} ms.");
    }

    [Fact]
    public async Task ConcurrentWriteBurst_PersistsEveryRecordExactlyOnce()
    {
        var workspace = await Client.CreateWorkspaceAsync();
        var section = await Client.CreateSectionAsync(workspace.Id);
        var samples = new ConcurrentBag<(string Name, double Ms, bool Ok)>();
        var created = new ConcurrentBag<string>();
        var clock = System.Diagnostics.Stopwatch.StartNew();

        await Parallel.ForEachAsync(Enumerable.Range(0, _settings.BurstSize),
            new ParallelOptions { MaxDegreeOfParallelism = _settings.Concurrency * 2 }, async (i, _) =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var response = await Client.PostAsJsonAsync(PavedRoadRecordsUrl, NewRecord(workspace.Id, section.Id, i, i + 0.5));
                var ok = response.StatusCode == HttpStatusCode.Created;
                if (ok)
                {
                    created.Add((await response.Content.ReadFromJsonAsync<PavedRoadRecordDto>())!.Id);
                }
                samples.Add(("POST record (burst)", sw.Elapsed.TotalMilliseconds, ok));
            });
        clock.Stop();

        var stats = LoadRunner.Summarise("POST record (burst)", samples.ToList());
        Report(new LoadResult($"Concurrent write burst of {_settings.BurstSize} ({target.Description})", clock.Elapsed,
            _settings.Concurrency * 2, [stats], stats with { Name = "**Total**" }));

        Assert.Equal(0, stats.Errors);
        var stored = new List<string>();
        for (var page = 1; ; page++)
        {
            var result = await Client.GetPageAsync<PavedRoadRecordDto>($"{PavedRoadRecordsUrl}/section/{section.Id}?page={page}&pageSize=200");
            Assert.Equal(_settings.BurstSize, result.TotalCount);
            stored.AddRange(result.Items.Select(r => r.Id));
            if (!result.HasNextPage)
            {
                break;
            }
        }
        Assert.Equal(created.Order(), stored.Order());
    }

    private void Report(LoadResult result)
    {
        output.WriteLine(result.ToMarkdown());
        LoadRunner.AppendToReport(result);
    }
}
