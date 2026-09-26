using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace RoadOps.StressTests;

/// <summary>Tunable via environment variables so the same tests work for a quick CI smoke run or a long soak.</summary>
public sealed record StressSettings(int DurationSeconds, int Concurrency, double MaxP95Ms, double MaxErrorRate, int BurstSize)
{
    public static StressSettings FromEnvironment() => new(
        DurationSeconds: Env("STRESS_DURATION_SECONDS", 20),
        Concurrency: Env("STRESS_CONCURRENCY", 25),
        MaxP95Ms: Env("STRESS_MAX_P95_MS", 500.0),
        MaxErrorRate: Env("STRESS_MAX_ERROR_RATE", 0.0),
        BurstSize: Env("STRESS_BURST_SIZE", 500));

    private static T Env<T>(string name, T fallback) where T : IParsable<T> =>
        T.TryParse(Environment.GetEnvironmentVariable(name), CultureInfo.InvariantCulture, out var value) ? value : fallback;
}

/// <summary>One weighted operation in a workload. Returns true when the response was the expected one.</summary>
public sealed record Scenario(string Name, int Weight, Func<HttpClient, Task<bool>> Execute);

public sealed record ScenarioStats(string Name, int Requests, int Errors, double P50, double P95, double P99, double Max)
{
    public double ErrorRate => Requests == 0 ? 0 : (double)Errors / Requests;
}

public sealed record LoadResult(string Title, TimeSpan Elapsed, int Concurrency, IReadOnlyList<ScenarioStats> Scenarios, ScenarioStats Total)
{
    public double RequestsPerSecond => Total.Requests / Elapsed.TotalSeconds;

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"### {Title}");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Duration {Elapsed.TotalSeconds:F1}s | concurrency {Concurrency} | {Total.Requests} requests | **{RequestsPerSecond:F0} req/s**");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Requests | Errors | p50 ms | p95 ms | p99 ms | max ms |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
        foreach (var s in Scenarios.Append(Total))
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {s.Name} | {s.Requests} | {s.Errors} | {s.P50:F1} | {s.P95:F1} | {s.P99:F1} | {s.Max:F1} |");
        }
        return sb.ToString();
    }
}

public static class LoadRunner
{
    /// <summary>Runs <paramref name="concurrency"/> workers, each picking weighted scenarios back-to-back until the duration elapses.</summary>
    public static async Task<LoadResult> RunAsync(string title, HttpClient client, IReadOnlyList<Scenario> scenarios, TimeSpan duration, int concurrency)
    {
        var samples = new ConcurrentBag<(string Name, double Ms, bool Ok)>();
        var totalWeight = scenarios.Sum(s => s.Weight);
        using var cts = new CancellationTokenSource(duration);
        var clock = Stopwatch.StartNew();

        var workers = Enumerable.Range(0, concurrency).Select(worker => Task.Run(async () =>
        {
            var random = new Random(worker);
            while (!cts.IsCancellationRequested)
            {
                var scenario = Pick(scenarios, random.Next(totalWeight));
                var sw = Stopwatch.StartNew();
                bool ok;
                try
                {
                    ok = await scenario.Execute(client);
                }
                catch
                {
                    ok = false;
                }
                samples.Add((scenario.Name, sw.Elapsed.TotalMilliseconds, ok));
            }
        }));

        await Task.WhenAll(workers);
        clock.Stop();

        var all = samples.ToList();
        var perScenario = all.GroupBy(s => s.Name).OrderBy(g => g.Key)
            .Select(g => Summarise(g.Key, g.ToList()))
            .ToList();

        return new LoadResult(title, clock.Elapsed, concurrency, perScenario, Summarise("**Total**", all));
    }

    public static ScenarioStats Summarise(string name, IReadOnlyCollection<(string Name, double Ms, bool Ok)> samples)
    {
        var sorted = samples.Select(s => s.Ms).Order().ToArray();
        return new ScenarioStats(name, sorted.Length, samples.Count(s => !s.Ok),
            Percentile(sorted, 0.50), Percentile(sorted, 0.95), Percentile(sorted, 0.99), sorted.LastOrDefault());
    }

    private static double Percentile(double[] sorted, double p) =>
        sorted.Length == 0 ? 0 : sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)];

    private static Scenario Pick(IReadOnlyList<Scenario> scenarios, int roll)
    {
        foreach (var scenario in scenarios)
        {
            if (roll < scenario.Weight)
            {
                return scenario;
            }
            roll -= scenario.Weight;
        }
        return scenarios[^1];
    }

    /// <summary>Appends a result to the markdown report consumed by scripts/run-tests.ps1.</summary>
    public static void AppendToReport(LoadResult result)
    {
        var path = Environment.GetEnvironmentVariable("STRESS_REPORT_PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.AppendAllText(path, result.ToMarkdown() + Environment.NewLine);
        }
    }
}
