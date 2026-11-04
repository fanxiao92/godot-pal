using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Pal.Extract;
using Xunit;

namespace Pal.Extract.Tests;

// Battle image decoder (abc.mkf, #92) - the M5 complete battle image package unit for
// Windows YJ2 chunks. Fixtures are independently authored PAL sprite byte literals
// (WORD offset table + RLE frames, reference/sdlpal/palcommon.c PAL_SpriteGetFrame)
// compressed through the synthetic Yj2TestData encoder; expected pixels come from
// hand-walking the RLE commands, never from the decoder under test.
public sealed class BattleImageDecoderTests
{
    // Table [3, 7, 10]: frame 0 at byte 6, frame 1 at 14, the final word 10 points at
    // a real follow-on frame at 20 (PAL_SpriteGetFrame's extra index).
    private static byte[] SpriteWithRealExtra => [3, 0, 7, 0, 10, 0,
        4, 0, 1, 0, 1, 7, 0x83, 0,        // frame 0: 4x1 - index 7, three transparent
        1, 0, 1, 0, 1, 9,                  // frame 1: 1x1 - index 9
        1, 0, 1, 0, 1, 4];                // extra frame 2: 1x1 - index 4

    // Same sprite, but the final word is the zero sentinel used by 134 of the 153
    // real abc.mkf chunks; the extra index is not a frame.
    private static byte[] SpriteWithSentinelExtra => [3, 0, 7, 0, 0, 0,
        4, 0, 1, 0, 1, 7, 0x83, 0,
        1, 0, 1, 0, 1, 9];

    // Final word 5 points at byte 10, inside frame 0's data: the dangling-pointer
    // shape of real chunks 24/25/30/59/71/86. Frame 1 must still end at the buffer end.
    private static byte[] SpriteWithDanglingExtra => [3, 0, 7, 0, 5, 0,
        4, 0, 1, 0, 1, 7, 0x83, 0,
        1, 0, 1, 0, 1, 9];

    private static byte[] Compress(byte[] raw)
    {
        var encoder = new Yj2TestData(raw.Length);
        foreach (byte value in raw) encoder.Literal(value);
        return encoder.End();
    }

    [Fact]
    public void Decode_exports_nominal_frames_plus_a_real_extra_table_entry()
    {
        var sprite = BattleImageDecoder.Decode(Compress(SpriteWithRealExtra), 1024, IndexedImage.MaxPixels);
        Assert.Equal(26, sprite.DecodedByteLength);
        Assert.Equal(2, sprite.FrameCount); // PAL_SpriteGetNumFrames stays the nominal count
        Assert.Equal(3, sprite.Frames.Length);
        var frame0 = sprite.Frames[0];
        Assert.Equal(4, frame0.Width);
        Assert.Equal(1, frame0.Height);
        Assert.Equal(6, frame0.SourceOffset);
        Assert.Equal(new byte[] { 7, 0, 0, 0 }, frame0.Pixels);
        Assert.Equal(new byte[] { 255, 0, 0, 0 }, frame0.Alpha);
        var frame1 = sprite.Frames[1];
        Assert.Equal(1, frame1.Width);
        Assert.Equal(1, frame1.Height);
        Assert.Equal(14, frame1.SourceOffset);
        Assert.Equal(new byte[] { 9 }, frame1.Pixels);
        var extra = sprite.Frames[2];
        Assert.Equal((2, 1, 1, 20), (extra.FrameIndex, extra.Width, extra.Height, extra.SourceOffset));
        Assert.Equal(new byte[] { 4 }, extra.Pixels);
    }

    [Fact]
    public void Decode_skips_the_zero_sentinel_extra_entry_without_decoding_it()
    {
        var sprite = BattleImageDecoder.Decode(Compress(SpriteWithSentinelExtra), 1024, IndexedImage.MaxPixels);
        Assert.Equal(2, sprite.FrameCount);
        Assert.Equal(2, sprite.Frames.Length);
        Assert.Equal(1, sprite.Frames[1].FrameIndex);
        Assert.Equal(new byte[] { 9 }, sprite.Frames[1].Pixels);
    }

    [Fact]
    public void Decode_rejects_a_dangling_extra_pointer_as_a_frame_boundary()
    {
        // With the dangling word included, frame 0 would be sliced to byte 10 and its
        // RLE truncated; the decoder must drop that word and recover the real boundary.
        var sprite = BattleImageDecoder.Decode(Compress(SpriteWithDanglingExtra), 1024, IndexedImage.MaxPixels);
        Assert.Equal(2, sprite.Frames.Length);
        Assert.Equal(new byte[] { 7, 0, 0, 0 }, sprite.Frames[0].Pixels);
        Assert.Equal(1, sprite.Frames[1].Width);
        Assert.Equal(1, sprite.Frames[1].Height);
    }

    [Fact]
    public void Decode_enforces_the_shared_pixel_budget_across_frames()
    {
        // The two nominal frames need 4 + 1 pixels.
        Assert.Equal(2, BattleImageDecoder.Decode(Compress(SpriteWithSentinelExtra), 1024, 5).Frames.Length);
        var error = Assert.Throws<InvalidDataException>(
            () => BattleImageDecoder.Decode(Compress(SpriteWithRealExtra), 1024, 4));
        Assert.Contains("pixel limit", error.Message);
    }

    [Theory]
    [InlineData(new byte[] { 1, 0, 0, 0 }, "YJ2")]
    [InlineData(new byte[] { (byte)'Y', (byte)'J', (byte)'_', (byte)'1', 0, 0, 0, 0 }, "YJ1")]
    public void Decode_rejects_broken_or_unsupported_compression(byte[] chunk, string message)
    {
        var error = Assert.Throws<InvalidDataException>(() => BattleImageDecoder.Decode(chunk, 1024, IndexedImage.MaxPixels));
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void Decode_rejects_a_frame_table_larger_than_the_sprite_data()
    {
        // word[0] claims four table words (8 bytes) but the sprite stops after 4.
        var error = Assert.Throws<InvalidDataException>(
            () => BattleImageDecoder.Decode(Compress([4, 0, 8, 0]), 1024, IndexedImage.MaxPixels));
        Assert.Contains("offset table length is invalid", error.Message);
    }

    [Fact]
    public void Decode_rejects_a_sprite_without_any_nominal_frame()
    {
        var error = Assert.Throws<InvalidDataException>(
            () => BattleImageDecoder.Decode(Compress([1, 0]), 1024, IndexedImage.MaxPixels));
        Assert.Contains("frame count is empty", error.Message);
    }

    [Fact]
    public void Decode_reports_nominal_frame_corruption_even_with_an_optional_entry_present()
    {
        // Frame 1's RLE skips three pixels of a 1x1 image; the retry without the extra
        // word must fail with the same corruption, not silently drop frames.
        byte[] sprite = [3, 0, 7, 0, 0, 0,
            4, 0, 1, 0, 1, 7, 0x83, 0,
            1, 0, 1, 0, 0x83, 0, 0, 0, 0, 0];
        var error = Assert.Throws<InvalidDataException>(
            () => BattleImageDecoder.Decode(Compress(sprite), 1024, IndexedImage.MaxPixels));
        Assert.Contains("exceeds the image", error.Message);
    }

    [Fact]
    public void DescribeChunk_records_source_identity_compression_and_artifact_mapping()
    {
        byte[] chunk = Compress(SpriteWithRealExtra);
        var sprite = BattleImageDecoder.Decode(chunk, 1024, IndexedImage.MaxPixels);
        var entry = BattleImageDecoder.DescribeChunk(42, chunk, sprite);
        Assert.Equal(42, (int)entry["index"]!);
        Assert.Equal(chunk.Length, (int)entry["byteLength"]!);
        // Independent expectation: hash the fixture bytes again in the test.
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(chunk)), (string)entry["sha256"]!);
        Assert.Equal("yj2", (string)entry["compression"]!);
        Assert.Equal(26, (int)entry["decodedByteLength"]!);
        Assert.Equal(2, (int)entry["frameCount"]!);
        Assert.Equal(3, (int)entry["decodedFrameCount"]!);
        Assert.Equal("sprites/42.json", (string)entry["sprite"]!);
    }

    [Fact]
    public void DescribeChunk_marks_empty_chunks_without_decoded_fields()
    {
        var entry = BattleImageDecoder.DescribeChunk(0, [], null);
        Assert.Equal(0, (int)entry["byteLength"]!);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Array.Empty<byte>())), (string)entry["sha256"]!);
        Assert.Equal("empty", (string)entry["compression"]!);
        Assert.Null(entry["frameCount"]);
        Assert.Null(entry["sprite"]);
    }

    [Fact]
    public void DescribeSprite_documents_every_frame_with_planes_and_png_references()
    {
        var sprite = BattleImageDecoder.Decode(Compress(SpriteWithRealExtra), 1024, IndexedImage.MaxPixels);
        var document = BattleImageDecoder.DescribeSprite(7, "abc.mkf", sprite);
        Assert.Equal(1, (int)document["formatVersion"]!);
        Assert.Equal("faithful", (string)document["profile"]!);
        Assert.Equal(7, (int)document["chunk"]!);
        Assert.Equal("abc.mkf", (string)document["source"]!["archive"]!);
        Assert.Equal("Windows YJ2", (string)document["source"]!["encoding"]!);
        Assert.Equal(2, (int)document["frameCount"]!);
        var frames = document["frames"]!.AsArray();
        Assert.Equal(3, frames.Count);
        Assert.Equal("sprites/7/2.png", (string)frames[2]!["png"]!);
        Assert.Equal(20, (int)frames[2]!["sourceOffset"]!);
        Assert.Equal(Convert.FromBase64String((string)frames[0]!["indices"]!), new byte[] { 7, 0, 0, 0 });
        Assert.Equal(Convert.FromBase64String((string)frames[0]!["alpha"]!), new byte[] { 255, 0, 0, 0 });
    }
}
