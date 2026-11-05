using System.Security.Cryptography;
using Pal.Extract;
using Xunit;

namespace Pal.Extract.Tests;

// Battle image decoding for f.mkf (#94) - the player battle sprite / summoned god
// archive. The reference reads f chunks with PAL_MKFDecompressChunk exactly like
// abc.mkf (battle.c PAL_LoadBattleSprites for rgwSpriteNumInBattle players,
// fight.c:3135 wSummonEffect+10 for summoned gods), so this path reuses the Windows
// YJ2 + PAL sprite WORD offset table decoder instead of adding a parallel one. The
// fixtures below are independently authored sprite byte literals compressed through
// the synthetic Yj2TestData encoder; expected pixels come from hand-walking the RLE
// commands, never from the decoder under test.
public sealed class FBattleImageDecoderTests
{
    // Player-battle-sprite shape (real chunks 0-9): table [3, 7, 0] - two nominal
    // frames, and the final word is the zero sentinel every one of the 19 real f.mkf
    // chunks carries, so decodedFrameCount stays the nominal count. Frame 0 spans
    // bytes 6..14 (2x1: opaque 5, skip one, one align byte), frame 1 spans 14..20.
    private static byte[] SentinelSprite => [3, 0, 7, 0, 0, 0,
        2, 0, 1, 0, 1, 5, 0x81, 0,
        1, 0, 1, 0, 1, 6];

    // Summoned-god shape: every real chunk 10-18 also ends in the sentinel, but the
    // table still permits PAL_SpriteGetFrame's extra index; this fixture has a real
    // follow-on frame there (table [3, 7, 10]), which must decode as frame 2.
    private static byte[] ExtraFrameSprite => [3, 0, 7, 0, 10, 0,
        2, 0, 1, 0, 1, 5, 0x81, 0,
        1, 0, 1, 0, 1, 6,
        1, 0, 1, 0, 1, 7];

    private static byte[] Compress(byte[] raw)
    {
        var encoder = new Yj2TestData(raw.Length);
        foreach (byte value in raw) encoder.Literal(value);
        return encoder.End();
    }

    [Fact]
    public void Decode_exports_the_nominal_frames_when_the_final_table_word_is_a_sentinel()
    {
        var sprite = BattleImageDecoder.Decode(Compress(SentinelSprite), 1024, IndexedImage.MaxPixels);
        Assert.Equal(20, sprite.DecodedByteLength);
        Assert.Equal(2, sprite.FrameCount); // PAL_SpriteGetNumFrames: first word minus one
        Assert.Equal(2, sprite.Frames.Length); // the sentinel adds no frame
        var frame0 = sprite.Frames[0];
        Assert.Equal((2, 1, 6), (frame0.Width, frame0.Height, frame0.SourceOffset));
        // Hand-walked frame 0: run 1 writes index 5, run 0x81 - 0x80 = 1 skips a pixel.
        Assert.Equal(new byte[] { 5, 0 }, frame0.Pixels);
        Assert.Equal(new byte[] { 255, 0 }, frame0.Alpha);
        var frame1 = sprite.Frames[1];
        Assert.Equal((1, 1, 14), (frame1.Width, frame1.Height, frame1.SourceOffset));
        Assert.Equal(new byte[] { 6 }, frame1.Pixels);
    }

    [Fact]
    public void Decode_still_exports_a_real_extra_table_entry_when_one_is_present()
    {
        // The extra-index rule is shared with abc; real f.mkf never exercises it
        // (all 19 chunks end in the sentinel), so the structural behavior is pinned
        // by this fixture rather than by real data.
        var sprite = BattleImageDecoder.Decode(Compress(ExtraFrameSprite), 1024, IndexedImage.MaxPixels);
        Assert.Equal(2, sprite.FrameCount);
        Assert.Equal(3, sprite.Frames.Length);
        var extra = sprite.Frames[2];
        Assert.Equal((2, 1, 1, 20), (extra.FrameIndex, extra.Width, extra.Height, extra.SourceOffset));
        Assert.Equal(new byte[] { 7 }, extra.Pixels);
    }

    [Fact]
    public void Decode_enforces_the_shared_pixel_budget_across_frames()
    {
        // The two sentinel frames need 2 + 1 pixels; the extra-entry fixture needs 4.
        Assert.Equal(2, BattleImageDecoder.Decode(Compress(SentinelSprite), 1024, 3).Frames.Length);
        Assert.Throws<InvalidDataException>(
            () => BattleImageDecoder.Decode(Compress(ExtraFrameSprite), 1024, 3));
    }

    [Theory]
    [InlineData(new byte[] { (byte)'Y', (byte)'J', (byte)'_', (byte)'1', 0, 0, 0, 0 }, "YJ1")]
    [InlineData(new byte[] { 1, 0, 0, 0 }, "YJ2")]
    public void Decode_rejects_dos_or_malformed_chunks(byte[] chunk, string message)
    {
        // f.mkf is one of the six archives PAL_IsWINVersion probes (global.c:55); its
        // DOS form would be YJ_1, which the Windows-baseline decoder must reject.
        var error = Assert.Throws<InvalidDataException>(
            () => BattleImageDecoder.Decode(chunk, 1024, IndexedImage.MaxPixels));
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void Decode_rejects_a_frame_table_larger_than_the_sprite_data()
    {
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
    public void DescribeChunk_labels_f_chunks_with_the_shared_yj2_compression()
    {
        byte[] chunk = Compress(SentinelSprite);
        var sprite = BattleImageDecoder.Decode(chunk, 1024, IndexedImage.MaxPixels);
        var entry = BattleImageDecoder.DescribeChunk(3, chunk, sprite);
        Assert.Equal("yj2", (string)entry["compression"]!);
        Assert.Equal(20, (int)entry["decodedByteLength"]!);
        Assert.Equal(2, (int)entry["frameCount"]!);
        Assert.Equal(2, (int)entry["decodedFrameCount"]!); // sentinel: no extra frame
        Assert.Equal("sprites/3.json", (string)entry["sprite"]!);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(chunk)), (string)entry["sha256"]!);
    }

    [Fact]
    public void DescribeSprite_documents_the_archive_and_shared_encoding()
    {
        var sprite = BattleImageDecoder.Decode(Compress(SentinelSprite), 1024, IndexedImage.MaxPixels);
        // The exporter passes the archive's own file name, so an f package's documents
        // name f.mkf while keeping abc's Windows YJ2 encoding and frame layout.
        var document = BattleImageDecoder.DescribeSprite(8, "f.mkf", sprite);
        Assert.Equal("f.mkf", (string)document["source"]!["archive"]!);
        Assert.Equal("Windows YJ2", (string)document["source"]!["encoding"]!);
        Assert.Equal("PAL sprite WORD offset table + RLE frames", (string)document["source"]!["frameLayout"]!);
        var frames = document["frames"]!.AsArray();
        Assert.Equal(2, frames.Count);
        Assert.Equal("sprites/8/1.png", (string)frames[1]!["png"]!);
        Assert.Equal(Convert.FromBase64String((string)frames[0]!["indices"]!), new byte[] { 5, 0 });
    }
}
