using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoadOps.Domain.Entities;

namespace RoadOps.Infrastructure.Configurations;

public class PavedRoadRecordConfiguration : IEntityTypeConfiguration<PavedRoadRecord>
{
    public void Configure(EntityTypeBuilder<PavedRoadRecord> builder)
    {
        builder.ToTable("paved_road_records");

        builder.HasKey(pr => pr.Id);

        builder.Property(pr => pr.Id)
            .HasMaxLength(36)
            .IsRequired();

        builder.Property(pr => pr.WorkspaceId)
            .HasMaxLength(36)
            .IsRequired();

        builder.Property(pr => pr.SectionId)
            .HasMaxLength(36)
            .IsRequired();

        builder.Property(pr => pr.ChainageFrom)
            .IsRequired();

        builder.Property(pr => pr.ChainageTo)
            .IsRequired();

        builder.Property(pr => pr.SurfaceType)
            .IsRequired();

        builder.Property(pr => pr.DistressType)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(pr => pr.Degree)
            .IsRequired();

        builder.Property(pr => pr.Extent)
            .IsRequired();

        builder.Property(pr => pr.RutDepthMm)
            .IsRequired();

        builder.Property(pr => pr.RidingQuality)
            .HasMaxLength(255);

        builder.Property(pr => pr.SkidResistance)
            .HasMaxLength(255);

        builder.Property(pr => pr.StdRef)
            .HasMaxLength(255);

        builder.Property(pr => pr.RecommendedAction)
            .HasMaxLength(500);

        builder.Property(pr => pr.Latitude)
            .IsRequired();

        builder.Property(pr => pr.Longitude)
            .IsRequired();

        builder.Property(pr => pr.ImagePaths)
            .IsRequired();

        builder.Property(pr => pr.Notes)
            .HasMaxLength(1000);

        builder.Property(pr => pr.LengthM);
        builder.Property(pr => pr.WidthM);
        builder.Property(pr => pr.DepthMm);

        builder.Property(pr => pr.CreatedBy)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(pr => pr.CreatedAt)
            .IsRequired();

        builder.Property(pr => pr.UpdatedAt)
            .IsRequired();

        // Composite indexes match the paged queries: filter by parent, order by chainage.
        // They also serve as the foreign key indexes.
        builder.HasIndex(pr => new { pr.WorkspaceId, pr.ChainageFrom });
        builder.HasIndex(pr => new { pr.SectionId, pr.ChainageFrom });
        builder.HasIndex(pr => pr.ChainageFrom);
        builder.HasIndex(pr => pr.CreatedAt);
        builder.HasIndex(pr => pr.DistressType);
        // Bounding-box prefilter for nearest-observation lookups from GPS.
        builder.HasIndex(pr => new { pr.Latitude, pr.Longitude });

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(pr => pr.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<RoadSection>()
            .WithMany()
            .HasForeignKey(pr => pr.SectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
