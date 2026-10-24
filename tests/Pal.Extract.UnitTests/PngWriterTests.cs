using System.IO.Compression;
using System.Text;
using Xunit;

namespace Pal.Extract.Tests;

public class PngWriterTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    [InlineData(257)]
    public void Encode_requires_exactly_256_palette_colors(int count)
    {
        var image = new IndexedImage(1, 1, [0], [255]);
        Assert.Throws<InvalidDataException>(() => PngWriter.Encode(image, new RgbColor[count]));
    }

    [Fact]
    public void Encode_writes_deterministic_rgba_png_with_independent_alpha_and_valid_chunks()
    {
        var colors = new RgbColor[256];
        colors[0] = new RgbColor(12, 34, 56);
        colors[7] = new RgbColor(252, 128, 4);
        var image = new IndexedImage(2, 2, [0, 7, 0, 7], [255, 255, 0, 128]);
        var png = PngWriter.Encode(image, colors);
        Assert.Equal(png, PngWriter.Encode(image, colors));
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        using var input = new MemoryStream(png, 8, png.Length - 8);
        using var reader = new BinaryReader(input);
        var types = new List<string>();
        using var compressed = new MemoryStream();
        while (input.Position < input.Length)
        {
            int length = ReadBigEndian(reader);
            var type = reader.ReadBytes(4);
            var payload = reader.ReadBytes(length);
            Assert.Equal(length, payload.Length);
            uint checksum = unchecked((uint)ReadBigEndian(reader));
            Assert.Equal(CrcOracle([.. type, .. payload]), checksum);
            string name = Encoding.ASCII.GetString(type);
            types.Add(name);
            switch (name)
            {
                case "IHDR":
                    Assert.Equal(new byte[] { 0, 0, 0, 2, 0, 0, 0, 2, 8, 6, 0, 0, 0 }, payload);
                    break;
                case "IDAT": compressed.Write(payload); break;
                case "IEND":
                    Assert.Empty(payload);
                    Assert.Equal(0xAE426082u, checksum); // Published PNG IEND CRC.
                    break;
                default: Assert.Fail($"Unexpected PNG chunk: {name}"); break;
            }
        }
        Assert.Equal(new[] { "IHDR", "IDAT", "IEND" }, types);
        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var scanlines = new MemoryStream();
        zlib.CopyTo(scanlines);
        Assert.Equal(new byte[]
        {
            0, 12, 34, 56, 255, 252, 128, 4, 255,
            0, 12, 34, 56, 0, 252, 128, 4, 128
        }, scanlines.ToArray());
    }

    // Test-only reader; no production byte/chunk helpers are used.
    private static int ReadBigEndian(BinaryReader reader) =>
        (reader.ReadByte() << 24) | (reader.ReadByte() << 16) | (reader.ReadByte() << 8) | reader.ReadByte();

    private static uint CrcOracle(byte[] bytes)
    {
        uint remainder = 0xFFFFFFFF;
        foreach (byte value in bytes)
        {
            uint bits = value;
            for (int bit = 0; bit < 8; bit++, bits >>= 1)
                remainder = (remainder & 1) != (bits & 1) ? (remainder >> 1) ^ 0xEDB88320 : remainder >> 1;
        }
        return ~remainder;
    }
}
