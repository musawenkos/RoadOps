using System.Buffers.Binary;

namespace RoadOps.Application.Common;

/// <summary>
/// Removes metadata from uploaded photos without re-encoding them: EXIF (GPS position, device serial, capture time),
/// XMP, IPTC, comments and text chunks, and anything after the end of the image. Only the segments a viewer needs to
/// draw the picture are kept; for JPEG the EXIF orientation is written back so the photo is not shown sideways.
/// Walking the container also rejects files that only start with image magic bytes but are not a well-formed image.
/// </summary>
public static class PhotoMetadata
{
    public static byte[] Strip(ReadOnlySpan<byte> image, PhotoFormat format)
    {
        if (format == PhotoFormats.Jpeg) return StripJpeg(image);
        if (format == PhotoFormats.Png) return StripPng(image);
        if (format == PhotoFormats.Webp) return StripWebp(image);
        throw new ArgumentOutOfRangeException(nameof(format), format.ContentType, "Unsupported photo format.");
    }

    private static ArgumentException Malformed(string type) => new($"The file is not a valid {type} image.", "content");

    // JPEG: SOI, then marker segments (FF xx + 2-byte length), entropy-coded data after each SOS, then EOI.
    private static byte[] StripJpeg(ReadOnlySpan<byte> image)
    {
        using var output = new MemoryStream(image.Length);
        output.Write([0xFF, 0xD8]);
        ushort? orientation = null;
        var orientationWritten = false;
        var pos = 2;

        while (true)
        {
            if (pos + 1 >= image.Length || image[pos] != 0xFF) throw Malformed("JPEG");
            while (pos + 1 < image.Length && image[pos + 1] == 0xFF) pos++; // fill bytes
            if (pos + 1 >= image.Length) throw Malformed("JPEG");

            var marker = image[pos + 1];
            if (marker == 0xD9) // EOI: anything after it (e.g. an appended archive) is dropped
            {
                output.Write([0xFF, 0xD9]);
                return output.ToArray();
            }

            if (marker is 0x01 or >= 0xD0 and <= 0xD7) // standalone markers, no length
            {
                output.Write(image.Slice(pos, 2));
                pos += 2;
                continue;
            }

            if (pos + 3 >= image.Length) throw Malformed("JPEG");
            var length = BinaryPrimitives.ReadUInt16BigEndian(image[(pos + 2)..]);
            if (length < 2 || pos + 2 + length > image.Length) throw Malformed("JPEG");
            var segment = image.Slice(pos, 2 + length);
            pos += 2 + length;

            if (marker == 0xE1 && orientation is null)
            {
                orientation = ReadExifOrientation(segment[4..]);
            }

            // Keep APP0 (JFIF), APP2 (ICC colour profile), APP14 (Adobe colour transform) and all non-APP, non-COM
            // segments; drop APP1 (EXIF/XMP), the other APPn (IPTC, maker data) and COM.
            var keep = marker is 0xE0 or 0xE2 or 0xEE || marker is < 0xE0 or (> 0xEF and not 0xFE);
            if (!keep) continue;

            if (marker != 0xE0 && !orientationWritten)
            {
                if (orientation is > 1 and <= 8) output.Write(OrientationSegment(orientation.Value));
                orientationWritten = true;
            }

            output.Write(segment);

            if (marker == 0xDA) // SOS: copy entropy-coded data up to the next real marker
            {
                var start = pos;
                while (pos + 1 < image.Length && !(image[pos] == 0xFF && image[pos + 1] is not 0x00 and not (>= 0xD0 and <= 0xD7)))
                {
                    pos++;
                }

                output.Write(image[start..pos]);
            }
        }
    }

    private static ushort? ReadExifOrientation(ReadOnlySpan<byte> app1)
    {
        if (app1.Length < 14 || !app1[..6].SequenceEqual("Exif\0\0"u8)) return null;
        var tiff = app1[6..];
        var little = tiff[0] == (byte)'I' && tiff[1] == (byte)'I';
        if (!little && !(tiff[0] == (byte)'M' && tiff[1] == (byte)'M')) return null;


        var ifd = little ? BinaryPrimitives.ReadUInt32LittleEndian(tiff[4..]) : BinaryPrimitives.ReadUInt32BigEndian(tiff[4..]);
        if (ifd > (uint)tiff.Length - 2) return null;
        var count = U16(tiff, (int)ifd, little);
        for (var i = 0; i < count; i++)
        {
            var entry = (int)ifd + 2 + i * 12;
            if (entry + 12 > tiff.Length) return null;
            if (U16(tiff, entry, little) == 0x0112 && U16(tiff, entry + 2, little) == 3) return U16(tiff, entry + 8, little);
        }

        return null;

        static ushort U16(ReadOnlySpan<byte> data, int at, bool little) =>
            little ? BinaryPrimitives.ReadUInt16LittleEndian(data[at..]) : BinaryPrimitives.ReadUInt16BigEndian(data[at..]);
    }

    /// <summary>A minimal APP1 EXIF segment holding only the orientation tag (big-endian TIFF, one IFD entry).</summary>
    private static byte[] OrientationSegment(ushort orientation) =>
    [
        0xFF, 0xE1, 0x00, 0x22,
        (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
        (byte)'M', (byte)'M', 0x00, 0x2A, 0, 0, 0, 8,   // TIFF header, IFD0 at offset 8
        0x00, 0x01,                                     // one entry
        0x01, 0x12, 0x00, 0x03, 0, 0, 0, 1,             // Orientation, SHORT, count 1
        (byte)(orientation >> 8), (byte)orientation, 0, 0,
        0, 0, 0, 0,                                     // no next IFD
    ];

    // PNG: signature, then chunks (4-byte length, 4-byte type, data, CRC) ending with IEND.
    private static readonly HashSet<string> PngAncillaryKept = ["tRNS", "gAMA", "cHRM", "sRGB", "iCCP", "sBIT", "pHYs", "bKGD", "cICP"];

    private static byte[] StripPng(ReadOnlySpan<byte> image)
    {
        using var output = new MemoryStream(image.Length);
        output.Write(image[..8]);
        var pos = 8;
        while (true)
        {
            if (pos + 12 > image.Length) throw Malformed("PNG");
            var length = BinaryPrimitives.ReadUInt32BigEndian(image[pos..]);
            if (length > (uint)(image.Length - pos - 12)) throw Malformed("PNG");
            var type = System.Text.Encoding.ASCII.GetString(image.Slice(pos + 4, 4));
            var chunk = image.Slice(pos, 12 + (int)length);
            pos += chunk.Length;

            // Critical chunks start with an upper-case letter; of the ancillary ones only rendering hints are kept, so
            // eXIf, tEXt, zTXt, iTXt, tIME and any private chunks are dropped.
            if (char.IsUpper(type[0]) || PngAncillaryKept.Contains(type))
            {
                output.Write(chunk);
            }

            if (type == "IEND") return output.ToArray();
        }
    }

    // WebP: RIFF header, then chunks (fourcc, 4-byte little-endian size, data, padded to an even length).
    private static byte[] StripWebp(ReadOnlySpan<byte> image)
    {
        if (image.Length < 12) throw Malformed("WebP");
        var riffEnd = 8 + (long)BinaryPrimitives.ReadUInt32LittleEndian(image[4..]);
        if (riffEnd > image.Length) throw Malformed("WebP");

        using var output = new MemoryStream(image.Length);
        output.Write(image[..12]);
        var pos = 12;
        while (pos < riffEnd)
        {
            if (pos + 8 > riffEnd) throw Malformed("WebP");
            var size = BinaryPrimitives.ReadUInt32LittleEndian(image[(pos + 4)..]);
            var padded = size + (size & 1);
            if (padded > riffEnd - pos - 8) throw Malformed("WebP");
            var chunk = image.Slice(pos, 8 + (int)padded);
            pos += chunk.Length;

            var fourCc = chunk[..4];
            if (fourCc.SequenceEqual("EXIF"u8) || fourCc.SequenceEqual("XMP "u8)) continue;

            var start = (int)output.Position;
            output.Write(chunk);
            if (fourCc.SequenceEqual("VP8X"u8) && size >= 1)
            {
                output.GetBuffer()[start + 8] &= unchecked((byte)~0x0C); // clear the EXIF (0x08) and XMP (0x04) flags
            }
        }

        var result = output.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)(result.Length - 8));
        return result;
    }
}
