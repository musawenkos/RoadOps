using Microsoft.EntityFrameworkCore;
using RoadOps.Domain.Entities;
using RoadOps.Infrastructure.Configurations;

namespace RoadOps.Infrastructure.Data;

public class RoadOpsDbContext : DbContext
{
    public RoadOpsDbContext(DbContextOptions<RoadOpsDbContext> options)
        : base(options)
    {
    }

    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<RoadSection> RoadSections => Set<RoadSection>();
    public DbSet<PavedRoadRecord> PavedRoadRecords => Set<PavedRoadRecord>();
    public DbSet<Photo> Photos => Set<Photo>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // PostgreSQL convention: snake_case tables and columns (e.g. chainage_from), so raw SQL needs no quoting.
        optionsBuilder.UseSnakeCaseNamingConvention();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new WorkspaceConfiguration());
        modelBuilder.ApplyConfiguration(new RoadSectionConfiguration());
        modelBuilder.ApplyConfiguration(new PavedRoadRecordConfiguration());
        modelBuilder.ApplyConfiguration(new PhotoConfiguration());
    }
}
