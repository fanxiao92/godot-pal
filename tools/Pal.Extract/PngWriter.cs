using System.Buffers.Binary;
using System.IO.Compression;

namespace Pal.Extract;

/// <summary>Lossless 8-bit RGBA PNG. Per-pixel alpha cannot be represented by a palette transparency key.</summary>
public static class PngWriter
{
    public static byte[] Encode(IndexedImage image, ReadOnlySpan<RgbColor> palette)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (palette.Length != 256) throw new InvalidDataException("PNG requires exactly 256 palette colors.");
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            var row = new byte[1 + image.Width * 4]; // Filter 0; no timestamps or other metadata.
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    int source = y * image.Width + x;
                    int target = 1 + x * 4;
                    var color = palette[image.Pixels[source]];
                    row[target] = color.R;
                    row[target + 1] = color.G;
                    row[target + 2] = color.B;
                    row[target + 3] = image.Alpha[source];
                }
                zlib.Write(row);
            }
        }
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        Span<byte> header = stackalloc byte[13];
        header.Clear();
        BinaryPrimitives.WriteInt32BigEndian(header, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], image.Height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(output, "IHDR"u8, header);
        WriteChunk(output, "IDAT"u8, compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        output.Write(word);
        output.Write(type);
        output.Write(data);
        uint crc = UpdateCrc(0xFFFFFFFF, type);
        crc = UpdateCrc(crc, data);
        BinaryPrimitives.WriteUInt32BigEndian(word, ~crc);
        output.Write(word);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xEDB88320u;
        }
        return crc;
    }
}
