using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoadOps.Application.Common;
using RoadOps.Domain.Entities;

namespace RoadOps.Infrastructure.Configurations;

public class InspectorSessionConfiguration : IEntityTypeConfiguration<InspectorSession>
{
    public void Configure(EntityTypeBuilder<InspectorSession> builder)
    {
        builder.ToTable("inspector_sessions");

        builder.HasKey(s => s.Inspector);

        builder.Property(s => s.Inspector).HasMaxLength(255).IsRequired();
        builder.Property(s => s.WorkspaceId).HasMaxLength(36);
        builder.Property(s => s.SectionId).HasMaxLength(36);
        builder.Property(s => s.FollowUps).HasMaxLength(FieldRules.MaxNotesLength);
        builder.Property(s => s.UpdatedAt).IsRequired();

        // Deleting a survey or section forgets that part of the position, not the whole summary.
        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(s => s.WorkspaceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<RoadSection>()
            .WithMany()
            .HasForeignKey(s => s.SectionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
