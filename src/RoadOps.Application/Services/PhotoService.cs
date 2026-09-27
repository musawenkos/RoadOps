using System.Security.Cryptography;
using RoadOps.Application.Common;
using RoadOps.Application.Field;
using RoadOps.Application.Repositories;
using RoadOps.Domain.Entities;

namespace RoadOps.Application.Services;

/// <summary>
/// Accepts photo uploads from the field app. The type is taken from the file's magic bytes (JPEG, PNG or WebP only),
/// the size is capped, and the storage name is generated here, so nothing about the stored file comes from the client.
/// </summary>
public class PhotoService
{
    public const long MaxPhotoBytes = 10 * 1024 * 1024;

    private readonly IPhotoRepository _photos;
    private readonly IPhotoStorage _storage;
    private readonly TimeProvider _time;

    public PhotoService(IPhotoRepository photos, IPhotoStorage storage, TimeProvider time)
    {
        _photos = photos;
        _storage = storage;
        _time = time;
    }

    public async Task<UploadedPhoto> UploadAsync(Stream content, string caller, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(caller))
        {
            throw new InvalidOperationException("An authenticated caller is required.");
        }

        // Read at most one byte more than allowed, so an oversized upload is detected without buffering all of it.
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxPhotoBytes)
            {
                throw new ArgumentException($"Photos must be at most {MaxPhotoBytes / (1024 * 1024)} MB.", nameof(content));
            }

            buffer.Write(chunk, 0, read);
        }

        if (buffer.Length == 0)
        {
            throw new ArgumentException("The photo is empty.", nameof(content));
        }

        var bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
        var format = PhotoFormats.Detect(bytes[..Math.Min(bytes.Length, PhotoFormats.HeaderLength)])
            ?? throw new ArgumentException("Only JPEG, PNG and WebP photos are accepted.", nameof(content));

        var now = _time.GetUtcNow();
        var id = Guid.NewGuid().ToString("N");
        var photo = new Photo
        {
            Id = id,
            StorageKey = $"{now:yyyy}/{now:MM}/{id}.{format.Extension}",
            ContentType = format.ContentType,
            SizeBytes = buffer.Length,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            UploadedBy = caller,
            UploadedAt = now,
        };

        buffer.Position = 0;
        await _storage.SaveAsync(photo.StorageKey, buffer, cancellationToken);
        try
        {
            await _photos.AddAsync(photo, cancellationToken);
        }
        catch
        {
            // Don't leave an orphaned file when the database write fails.
            await _storage.DeleteAsync(photo.StorageKey, CancellationToken.None);
            throw;
        }

        return new UploadedPhoto(photo.Id, photo.ContentType, photo.SizeBytes, photo.Sha256);
    }

    /// <summary>Opens a photo for download, or returns null if the id is unknown.</summary>
    public async Task<(Photo Photo, Stream Content)?> OpenAsync(string photoId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(photoId) || photoId.Length > 36)
        {
            return null;
        }

        var photo = await _photos.GetByIdAsync(photoId, cancellationToken);
        if (photo is null)
        {
            return null;
        }

        var content = await _storage.OpenReadAsync(photo.StorageKey, cancellationToken);
        return content is null ? null : (photo, content);
    }
}
