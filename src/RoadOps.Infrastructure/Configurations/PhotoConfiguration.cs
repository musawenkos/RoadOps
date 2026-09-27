using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RoadOps.Domain.Entities;

namespace RoadOps.Infrastructure.Configurations;

public class PhotoConfiguration : IEntityTypeConfiguration<Photo>
{
    public void Configure(EntityTypeBuilder<Photo> builder)
    {
        builder.ToTable("photos");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id).HasMaxLength(36).IsRequired();
        builder.Property(p => p.StorageKey).HasMaxLength(255).IsRequired();
        builder.Property(p => p.ContentType).HasMaxLength(50).IsRequired();
        builder.Property(p => p.SizeBytes).IsRequired();
        builder.Property(p => p.Sha256).HasMaxLength(64).IsRequired();
        builder.Property(p => p.RecordId).HasMaxLength(36);
        builder.Property(p => p.UploadedBy).HasMaxLength(255).IsRequired();
        builder.Property(p => p.UploadedAt).IsRequired();

        builder.HasIndex(p => p.RecordId);
        builder.HasIndex(p => new { p.UploadedBy, p.UploadedAt });

        // Deleting an observation keeps the photo row (and file) but unlinks it.
        builder.HasOne<PavedRoadRecord>()
            .WithMany()
            .HasForeignKey(p => p.RecordId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
