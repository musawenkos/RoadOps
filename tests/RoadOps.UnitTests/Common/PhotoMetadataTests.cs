using System.Buffers.Binary;
using System.Text;
using RoadOps.Application.Common;

namespace RoadOps.UnitTests.Common;

public class PhotoMetadataTests
{
    private static readonly byte[] App0 = [0xFF, 0xE0, 0x00, 0x10, .. "JFIF\0"u8, 1, 1, 0, 0, 1, 0, 1, 0, 0];
    private static readonly byte[] Dqt = [0xFF, 0xDB, 0x00, 0x04, 0xAA, 0xBB];
    private static readonly byte[] Sos = [0xFF, 0xDA, 0x00, 0x04, 0x01, 0x02];

    // Entropy-coded data with a stuffed 0xFF (FF 00) and a restart marker (FF D0), which must be copied as image data.
    private static readonly byte[] ScanData = [0x12, 0xFF, 0x00, 0x34, 0xFF, 0xD0, 0x56];
    private static readonly byte[] Eoi = [0xFF, 0xD9];

    /// <summary>An EXIF APP1 segment with orientation 6 and a GPS-looking string standing in for the GPS IFD.</summary>
    private static byte[] ExifApp1(ushort orientation, bool littleEndian)
    {
        var tiff = new byte[8 + 2 + 2 * 12 + 4 + 16];
        if (littleEndian)
        {
            "II"u8.CopyTo(tiff);
            BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(2), 42);
            BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(4), 8);
            BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(8), 2);
            BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(10), 0x010F); // Make, ASCII
            BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(12), 2);
            BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(22), 0x0112); // Orientation, SHORT
            BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(24), 3);
            BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(26), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(30), orientation);
        }
        else
        {
            "MM"u8.CopyTo(tiff);
            BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(2), 42);
            BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(4), 8);
            BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(8), 1);
            BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(10), 0x0112);
            BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(12), 3);
            BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(14), 1);
            BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(18), orientation);
        }

        "GPS -25.74,28.19"u8.CopyTo(tiff.AsSpan(tiff.Length - 16));
        byte[] payload = [.. "Exif\0\0"u8, .. tiff];
        return [0xFF, 0xE1, (byte)((payload.Length + 2) >> 8), (byte)(payload.Length + 2), .. payload];
    }

    private static byte[] Segment(byte marker, string text)
    {
        var payload = Encoding.ASCII.GetBytes(text);
        return [0xFF, marker, (byte)((payload.Length + 2) >> 8), (byte)(payload.Length + 2), .. payload];
    }

    private static bool Contains(byte[] haystack, string text) => haystack.AsSpan().IndexOf(Encoding.ASCII.GetBytes(text)) >= 0;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Jpeg_DropsExifXmpCommentsAndTrailingData_KeepsImageAndOrientation(bool littleEndian)
    {
        byte[] jpeg =
        [
            0xFF, 0xD8, .. App0, .. ExifApp1(6, littleEndian), .. Segment(0xE1, "http://ns.adobe.com/xap/1.0/\0<x:xmpmeta/>"),
            .. Segment(0xED, "Photoshop 3.0 IPTC byline"), .. Segment(0xFE, "serial 12345"), .. Dqt, .. Sos, .. ScanData, .. Eoi,
            .. "PK\u0003\u0004 appended zip"u8,
        ];

        var clean = PhotoMetadata.Strip(jpeg, PhotoFormats.Jpeg);

        Assert.False(Contains(clean, "GPS"));
        Assert.False(Contains(clean, "xmpmeta"));
        Assert.False(Contains(clean, "IPTC"));
        Assert.False(Contains(clean, "serial"));
        Assert.False(Contains(clean, "PK"));
        byte[] orientationOnly = [0xFF, 0xE1, 0x00, 0x22, .. "Exif\0\0MM"u8, 0x00, 0x2A, 0, 0, 0, 8, 0, 1, 0x01, 0x12, 0, 3, 0, 0, 0, 1, 0, 6, 0, 0, 0, 0, 0, 0];
        Assert.Equal([0xFF, 0xD8, .. App0, .. orientationOnly, .. Dqt, .. Sos, .. ScanData, .. Eoi], clean);
    }

    [Fact]
    public void Jpeg_WithoutMetadata_IsUnchanged()
    {
        byte[] jpeg = [0xFF, 0xD8, .. App0, .. Dqt, .. Sos, .. ScanData, .. Eoi];

        Assert.Equal(jpeg, PhotoMetadata.Strip(jpeg, PhotoFormats.Jpeg));
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 })] // segment runs past the end
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xDB, 0x00, 0x04, 0xAA, 0xBB })] // no EOI
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0x3C, 0x68, 0x74, 0x6D, 0x6C })] // "<html" after the magic bytes
    public void Jpeg_Malformed_IsRejected(byte[] jpeg)
    {
        Assert.Throws<ArgumentException>(() => PhotoMetadata.Strip(jpeg, PhotoFormats.Jpeg));
    }

    private static byte[] PngChunk(string type, byte[] data)
    {
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        // The CRC is left at zero: chunks are copied through as they are, never re-checked.
        return chunk;
    }

    [Fact]
    public void Png_DropsTextExifAndTimeChunks_KeepsImageAndColourChunks()
    {
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var ihdr = PngChunk("IHDR", new byte[13]);
        var srgb = PngChunk("sRGB", [0]);
        var idat = PngChunk("IDAT", [1, 2, 3]);
        var iend = PngChunk("IEND", []);
        byte[] png =
        [
            .. signature, .. ihdr, .. PngChunk("eXIf", "MM GPS"u8.ToArray()), .. srgb, .. PngChunk("tEXt", "Author\0Inspector"u8.ToArray()),
            .. PngChunk("tIME", new byte[7]), .. idat, .. PngChunk("prVt", "private"u8.ToArray()), .. iend, .. "trailing"u8,
        ];

        Assert.Equal([.. signature, .. ihdr, .. srgb, .. idat, .. iend], PhotoMetadata.Strip(png, PhotoFormats.Png));
    }

    private static byte[] WebpChunk(string fourCc, byte[] data) =>
        [.. Encoding.ASCII.GetBytes(fourCc), (byte)data.Length, (byte)(data.Length >> 8), 0, 0, .. data, .. data.Length % 2 == 1 ? new byte[] { 0 } : []];

    private static byte[] Riff(params byte[][] chunks)
    {
        byte[] body = [.. "WEBP"u8, .. chunks.SelectMany(c => c)];
        var size = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)body.Length);
        return [.. "RIFF"u8, .. size, .. body];
    }

    [Fact]
    public void Webp_DropsExifAndXmpChunksAndClearsTheirFlags()
    {
        byte[] vp8x = [0x0C | 0x10, 0, 0, 0, 0, 0, 0, 0, 0, 0]; // EXIF + XMP + alpha flags
        var image = WebpChunk("VP8L", [1, 2, 3]);
        var webp = Riff(WebpChunk("VP8X", vp8x), image, WebpChunk("EXIF", "GPS -25.7"u8.ToArray()), WebpChunk("XMP ", "<x:xmpmeta/>"u8.ToArray()));

        var clean = PhotoMetadata.Strip(webp, PhotoFormats.Webp);

        byte[] vp8xClean = [0x10, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        Assert.Equal(Riff(WebpChunk("VP8X", vp8xClean), image), clean);
    }
}
