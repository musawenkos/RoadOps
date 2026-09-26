using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RoadOps.DataSeeder;
using RoadOps.Infrastructure.Data;

// Usage:
//   dotnet run --project tools/RoadOps.DataSeeder -- [--reset] [--seed 42] [--segment-km 0.1] [--connection "<cs>"]
//
//   --reset       Delete ALL existing workspaces/sections/records before seeding.
//   --seed        Random seed; the same seed always produces the same data shape.
//   --segment-km  Length of each assessed segment in km (default 0.1 = 100 m).
//   --connection  PostgreSQL connection string. Falls back to ROADOPS_CONNECTION, then the docker-compose default.

// Generated names and image paths must not depend on the machine's locale (e.g. "km 0,0").
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

const string DefaultConnection = "Host=localhost;Port=5432;Database=roadops;Username=roadops_user;Password=roadops_dev_password";

var connection = Option("--connection") ?? Environment.GetEnvironmentVariable("ROADOPS_CONNECTION") ?? DefaultConnection;
var seed = int.Parse(Option("--seed") ?? "42", CultureInfo.InvariantCulture);
var segmentKm = double.Parse(Option("--segment-km") ?? "0.1", CultureInfo.InvariantCulture);
var reset = args.Contains("--reset");

var options = new DbContextOptionsBuilder<RoadOpsDbContext>().UseNpgsql(connection).Options;
await using var db = new RoadOpsDbContext(options);

Console.WriteLine("Applying migrations...");
await db.Database.MigrateAsync();

if (reset)
{
    Console.WriteLine("Removing existing data (--reset)...");
    await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE paved_road_records, road_sections, workspaces");
}
else if (await db.Workspaces.AnyAsync())
{
    Console.Error.WriteLine("The database already contains workspaces. Re-run with --reset to replace them.");
    return 1;
}

var clock = Stopwatch.StartNew();
Console.WriteLine($"Generating data (seed {seed}, {segmentKm * 1000:F0} m segments)...");
var data = new SyntheticDataGenerator(seed, segmentKm).Generate();

db.ChangeTracker.AutoDetectChangesEnabled = false;
db.Workspaces.AddRange(data.Workspaces);
db.RoadSections.AddRange(data.Sections);
await db.SaveChangesAsync();

const int batchSize = 5000;
for (var i = 0; i < data.Records.Count; i += batchSize)
{
    db.PavedRoadRecords.AddRange(data.Records.Skip(i).Take(batchSize));
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();
    Console.Write($"\r  records {Math.Min(i + batchSize, data.Records.Count),6}/{data.Records.Count}");
}
Console.WriteLine();

Console.WriteLine();
Console.WriteLine($"Seeded in {clock.Elapsed.TotalSeconds:F1}s: {data.Workspaces.Count} workspaces, {data.Sections.Count} sections, {data.Records.Count} paved road records.");
Console.WriteLine();
Console.WriteLine($"{"Workspace",-52} {"Status",-10} {"Sections",8} {"Records",8} {"Avg deg",8}");
foreach (var w in data.Workspaces)
{
    var records = data.Records.Where(r => r.WorkspaceId == w.Id && r.Degree > 0).ToList();
    Console.WriteLine($"{w.Name,-52} {w.Status,-10} {data.Sections.Count(s => s.WorkspaceId == w.Id),8} {data.Records.Count(r => r.WorkspaceId == w.Id),8} {(records.Count == 0 ? 0 : records.Average(r => r.Degree)),8:F2}");
}

Console.WriteLine();
Console.WriteLine("Top distress types:");
foreach (var g in data.Records.GroupBy(r => r.DistressType).OrderByDescending(g => g.Count()).Take(10))
{
    Console.WriteLine($"  {g.Key,-24} {g.Count(),6}");
}

return 0;

string? Option(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
