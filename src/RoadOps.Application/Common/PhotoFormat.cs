namespace RoadOps.Application.Common;

public sealed record PhotoFormat(string ContentType, string Extension);

/// <summary>Identifies allowed image types from their leading bytes ("magic numbers"), never from a file name or header.</summary>
public static class PhotoFormats
{
    public static readonly PhotoFormat Jpeg = new("image/jpeg", "jpg");
    public static readonly PhotoFormat Png = new("image/png", "png");
    public static readonly PhotoFormat Webp = new("image/webp", "webp");

    /// <summary>How many leading bytes <see cref="Detect"/> needs.</summary>
    public const int HeaderLength = 12;

    public static PhotoFormat? Detect(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return Jpeg;
        }

        if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return Png;
        }

        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
        {
            return Webp;
        }

        return null;
    }
}
