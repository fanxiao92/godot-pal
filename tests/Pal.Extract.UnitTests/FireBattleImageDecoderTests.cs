using System.Security.Cryptography;
using Pal.Extract;
using Xunit;

namespace Pal.Extract.Tests;

// FIRE (#146): fight.c's player defensive/offensive and enemy magic consumers all
// decompress MAGIC.wEffect from fpFIRE before reading PAL sprite frames. These
// artificial Windows YJ2 payloads exercise that existing public decoder, not a new
// FIRE decoder. Expected offsets and planes are hand-walked from the byte literals;
// Yj2TestData only wraps the supplied bytes and never generates expected images.
public sealed class FireBattleImageDecoderTests
{
    // WORD table [3, 7, 10]: two nominal frames and a real extra frame at byte 20.
    private static byte[] ExtraFrameSprite => [3, 0, 7, 0, 10, 0,
        4, 0, 1, 0, 1, 7, 0x83, 0,
        1, 0, 1, 0, 1, 9,
        1, 0, 1, 0, 1, 4];

    // WORD table [3, 7, 0]: the final zero is a sentinel, not frame 2.
    private static byte[] SentinelSprite => [3, 0, 7, 0, 0, 0,
        4, 0, 1, 0, 1, 7, 0x83, 0,
        1, 0, 1, 0, 1, 9];

    [Fact]
    public void Decode_fire_shape_preserves_nominal_frames_and_zero_sentinel()
    {
        var sprite = BattleImageDecoder.Decode(Compress(SentinelSprite), 20, 5);
        Assert.Equal(20, sprite.DecodedByteLength);
        Assert.Equal(2, sprite.FrameCount);
        Assert.Equal(2, sprite.Frames.Length);
        Assert.Equal((0, 4, 1, 6), (sprite.Frames[0].FrameIndex, sprite.Frames[0].Width,
            sprite.Frames[0].Height, sprite.Frames[0].SourceOffset));
        Assert.Equal(new byte[] { 7, 0, 0, 0 }, sprite.Frames[0].Pixels);
        Assert.Equal(new byte[] { 255, 0, 0, 0 }, sprite.Frames[0].Alpha);
        Assert.Equal((1, 1, 1, 14), (sprite.Frames[1].FrameIndex, sprite.Frames[1].Width,
            sprite.Frames[1].Height, sprite.Frames[1].SourceOffset));
        Assert.Equal(new byte[] { 9 }, sprite.Frames[1].Pixels);
        Assert.Equal(new byte[] { 255 }, sprite.Frames[1].Alpha);
    }

    [Fact]
    public void Decode_fire_shape_exports_a_real_extra_frame_without_changing_nominal_count()
    {
        var sprite = BattleImageDecoder.Decode(Compress(ExtraFrameSprite), 26, 6);
        Assert.Equal(26, sprite.DecodedByteLength);
        Assert.Equal(2, sprite.FrameCount);
        Assert.Equal(3, sprite.Frames.Length);
        Assert.Equal((2, 1, 1, 20), (sprite.Frames[2].FrameIndex, sprite.Frames[2].Width,
            sprite.Frames[2].Height, sprite.Frames[2].SourceOffset));
        Assert.Equal(new byte[] { 4 }, sprite.Frames[2].Pixels);
        Assert.Equal(new byte[] { 255 }, sprite.Frames[2].Alpha);
    }

    [Fact]
    public void Decode_fire_shape_drops_a_dangling_optional_pointer_not_a_nominal_frame()
    {
        byte[] raw = SentinelSprite;
        raw[4] = 5; // byte 10 is inside frame 0, not an extra frame boundary.
        var sprite = BattleImageDecoder.Decode(Compress(raw), 20, 5);
        Assert.Equal(2, sprite.FrameCount);
        Assert.Equal(2, sprite.Frames.Length);
        Assert.Equal(new[] { 6, 14 }, sprite.Frames.Select(frame => frame.SourceOffset));
        Assert.Equal(new byte[] { 7, 0, 0, 0 }, sprite.Frames[0].Pixels);
        Assert.Equal(new byte[] { 9 }, sprite.Frames[1].Pixels);
    }

    [Fact]
    public void Describe_fire_preserves_chunk_identity_planes_and_source_layout()
    {
        byte[] chunk = Compress(ExtraFrameSprite);
        var sprite = BattleImageDecoder.Decode(chunk, 26, 6);
        var entry = BattleImageDecoder.DescribeChunk(17, chunk, sprite);
        Assert.Equal(17, (int)entry["index"]!);
        Assert.Equal(chunk.Length, (int)entry["byteLength"]!);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(chunk)), (string)entry["sha256"]!);
        Assert.Equal("yj2", (string)entry["compression"]!);
        Assert.Equal(26, (int)entry["decodedByteLength"]!);
        Assert.Equal(2, (int)entry["frameCount"]!);
        Assert.Equal(3, (int)entry["decodedFrameCount"]!);
        Assert.Equal("sprites/17.json", (string)entry["sprite"]!);
        var document = BattleImageDecoder.DescribeSprite(17, "fire.mkf", sprite);
        Assert.Equal(1, (int)document["formatVersion"]!);
        Assert.Equal("faithful", (string)document["profile"]!);
        Assert.Equal(17, (int)document["chunk"]!);
        Assert.Equal("fire.mkf", (string)document["source"]!["archive"]!);
        Assert.Equal(17, (int)document["source"]!["chunk"]!);
        Assert.Equal("Windows YJ2", (string)document["source"]!["encoding"]!);
        Assert.Equal("PAL sprite WORD offset table + RLE frames", (string)document["source"]!["frameLayout"]!);
        Assert.Equal(26, (int)document["decodedByteLength"]!);
        Assert.Equal(2, (int)document["frameCount"]!);
        var frames = document["frames"]!.AsArray();
        Assert.Equal(3, frames.Count);
        Assert.Equal("sprites/17/2.png", (string)frames[2]!["png"]!);
        Assert.Equal(new byte[] { 7, 0, 0, 0 }, Convert.FromBase64String((string)frames[0]!["indices"]!));
        Assert.Equal(new byte[] { 255, 0, 0, 0 }, Convert.FromBase64String((string)frames[0]!["alpha"]!));
        var empty = BattleImageDecoder.DescribeChunk(0, [], null);
        Assert.Equal(new[] { "byteLength", "compression", "index", "sha256" }, empty.Select(pair => pair.Key).Order());
        Assert.Equal("empty", (string)empty["compression"]!);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Array.Empty<byte>())), (string)empty["sha256"]!);
    }

    [Theory]
    [InlineData("table", "offset table length is invalid")]
    [InlineData("nominal-rle", "exceeds the image")]
    public void Decode_fire_rejects_bad_tables_and_nominal_frames(string kind, string message)
    {
        byte[] raw = kind == "table" ? [4, 0, 8, 0] : SentinelSprite;
        if (kind == "nominal-rle") raw[18] = 0x82; // two-pixel skip in a 1x1 nominal frame.
        var error = Assert.Throws<InvalidDataException>(() => BattleImageDecoder.Decode(Compress(raw), 1024, 1024));
        Assert.Contains(message, error.Message);
    }

    [Theory]
    [InlineData(new byte[] { (byte)'Y', (byte)'J', (byte)'_', (byte)'1' }, "YJ1")]
    [InlineData(new byte[] { 1, 0, 0, 0 }, "Truncated YJ2 bitstream")]
    public void Decode_fire_rejects_dos_and_truncated_windows_compression(byte[] chunk, string message)
    {
        var error = Assert.Throws<InvalidDataException>(() => BattleImageDecoder.Decode(chunk, 1024, 1024));
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void Decode_fire_enforces_declared_decoded_bytes_before_allocating_the_payload()
    {
        Assert.Equal(26, BattleImageDecoder.Decode(Compress(ExtraFrameSprite), 26, 6).DecodedByteLength);
        var exact = Assert.Throws<InvalidDataException>(() => BattleImageDecoder.Decode(Compress(ExtraFrameSprite), 25, 6));
        Assert.Contains("YJ2 declared length exceeds the output limit", exact.Message);
        // 8 MiB + 1 declared bytes, no payload needed: reject before reading symbols.
        var oversized = Assert.Throws<InvalidDataException>(() => BattleImageDecoder.Decode([1, 0, 128, 0], 8 * 1024 * 1024, 6));
        Assert.Contains("YJ2 declared length exceeds the output limit", oversized.Message);
    }

    [Fact]
    public void Decode_fire_shares_the_pixel_budget_between_nominal_frames()
    {
        // Frame 0 needs 4 pixels; frame 1 needs 1. A fresh budget per frame would pass 4.
        Assert.Equal(2, BattleImageDecoder.Decode(Compress(SentinelSprite), 20, 5).Frames.Length);
        var error = Assert.Throws<InvalidDataException>(() => BattleImageDecoder.Decode(Compress(SentinelSprite), 20, 4));
        Assert.Contains("pixel limit", error.Message);
    }

    private static byte[] Compress(byte[] raw)
    {
        var encoder = new Yj2TestData(raw.Length);
        foreach (byte value in raw) encoder.Literal(value);
        return encoder.End();
    }
}
