namespace RoadOps.Domain.Entities;

/// <summary>An uploaded site photo. The file lives in photo storage under a server-generated key.</summary>
public sealed class Photo
{
    public string Id { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    /// <summary>SHA-256 of the stored bytes (hex), for integrity checks and duplicate detection.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>The observation the photo is attached to, once attached.</summary>
    public string? RecordId { get; set; }

    public string UploadedBy { get; set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; set; }
    public DateTimeOffset? AttachedAt { get; set; }
}
