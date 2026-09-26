using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoadOps.Domain.Entities;

namespace RoadOps.Infrastructure.Configurations;

public class RoadSectionConfiguration : IEntityTypeConfiguration<RoadSection>
{
    public void Configure(EntityTypeBuilder<RoadSection> builder)
    {
        builder.ToTable("road_sections");

        builder.HasKey(rs => rs.Id);

        builder.Property(rs => rs.Id)
            .HasMaxLength(36)
            .IsRequired();

        builder.Property(rs => rs.SectionName)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(rs => rs.WorkspaceId)
            .HasMaxLength(36)
            .IsRequired();

        builder.Property(rs => rs.CreatedBy)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(rs => rs.CreatedAt)
            .IsRequired();

        builder.Property(rs => rs.UpdatedAt)
            .IsRequired();

        builder.HasIndex(rs => new { rs.WorkspaceId, rs.CreatedAt });
        builder.HasIndex(rs => rs.SectionName);

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(rs => rs.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
