using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoadOps.Domain.Entities;

namespace RoadOps.Infrastructure.Configurations;

public class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> builder)
    {
        builder.ToTable("workspaces");

        builder.HasKey(w => w.Id);

        builder.Property(w => w.Id)
            .HasMaxLength(36)
            .IsRequired();

        builder.Property(w => w.Name)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(w => w.AssessmentType)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(w => w.Corridor)
            .HasMaxLength(12)
            .IsRequired();

        builder.Property(w => w.SurveyYear)
            .IsRequired();

        builder.Property(w => w.Status)
            .IsRequired();

        builder.Property(w => w.CreatedBy)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(w => w.CreatedAt)
            .IsRequired();

        builder.Property(w => w.UpdatedAt)
            .IsRequired();

        builder.HasIndex(w => w.Name);
        builder.HasIndex(w => w.Status);
        builder.HasIndex(w => w.CreatedAt);
        builder.HasIndex(w => new { w.Corridor, w.SurveyYear });
    }
}
