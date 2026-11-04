using System.Security.Cryptography;
using Pal.Extract;
using Xunit;

namespace Pal.Extract.Tests;

// Battle image decoding for fbp.mkf (#95) - the battlefield background archive. The
// reference loads every fbp picture with PAL_MKFDecompressChunk into a 320*200 buffer
// (battle.c PAL_LoadBattleBackground by wNumBattleField, uigame.c menu backgrounds,
// ending.c PAL_ShowFBP/PAL_ScrollFBP) and PAL_FBPBlitToSurface copies the decompressed
// bytes linearly onto the screen surface: no sprite WORD offset table and no RLE, so
// the YJ2 payload must be exactly 64000 raw palette indices. Fixtures are synthesized
// through the independent Yj2TestData encoder (one literal per row plus distance-1
// matches that repeat it); expected planes come from the test's own pattern
// definition, never from the decoder under test.
public sealed class FbpImageDecoderTests
{
    // A full 320x200 screen where row y repeats palette index (y + base) & 0xff.
    // The encoder emits one literal per row and four overlapping distance-1 matches
    // (67+67+67+67+51 = 319 copies), so each fixture stays small and fast.
    private static byte[] CompressScreen(byte baseIndex)
    {
        var encoder = new Yj2TestData(320 * 200);
        for (int y = 0; y < 200; y++)
        {
            encoder.Literal((byte)(y + baseIndex));
            encoder.Match(67, 1).Match(67, 1).Match(67, 1).Match(67, 1).Match(51, 1);
        }
        return encoder.End();
    }

    private static byte[] ExpectedScreen(byte baseIndex)
    {
        var plane = new byte[320 * 200];
        for (int y = 0; y < 200; y++)
            for (int x = 0; x < 320; x++)
                plane[y * 320 + x] = (byte)(y + baseIndex);
        return plane;
    }

    [Fact]
    public void Decode_returns_one_fully_opaque_fullscreen_frame_per_chunk()
    {
        var sprite = FbpImageDecoder.Decode(CompressScreen(0), 64 * 1024);
        Assert.Equal(64000, sprite.DecodedByteLength);
        Assert.Equal(1, sprite.FrameCount);
        var frame = Assert.Single(sprite.Frames);
        Assert.Equal((0, 320, 200, 0), (frame.FrameIndex, frame.Width, frame.Height, frame.SourceOffset));
        // Independent expectation: row y is 320 copies of index y (row 7 = index 7).
        Assert.Equal(ExpectedScreen(0), frame.Pixels);
        Assert.All(frame.Alpha, a => Assert.Equal(255, a)); // backgrounds have no transparency
    }

    [Fact]
    public void Decode_decodes_every_screen_independently()
    {
        var sprite = FbpImageDecoder.Decode(CompressScreen(160), 64 * 1024);
        var plane = Assert.Single(sprite.Frames).Pixels;
        Assert.Equal(ExpectedScreen(160), plane);
        Assert.Equal(160, plane[0]);             // first pixel of row 0
        Assert.Equal(103, plane[^1]);            // row 199: (199 + 160) & 0xff rolls over
    }

    [Fact]
    public void Decode_enforces_the_shared_decoded_budget()
    {
        Assert.Equal(64000, FbpImageDecoder.Decode(CompressScreen(0), 64000).DecodedByteLength);
        var error = Assert.Throws<InvalidDataException>(() => FbpImageDecoder.Decode(CompressScreen(0), 63999));
        Assert.Contains("output limit", error.Message);
    }

    [Theory]
    // fbp.mkf is one of the six archives PAL_IsWINVersion probes (global.c:55); its
    // DOS form would be YJ_1, which the Windows-baseline decoder must reject.
    [InlineData(new byte[] { (byte)'Y', (byte)'J', (byte)'_', (byte)'1', 0, 0, 0, 0 }, "YJ1")]
    [InlineData(new byte[] { 1, 0, 0, 0 }, "Truncated YJ2")]
    public void Decode_rejects_dos_or_malformed_chunks(byte[] chunk, string message)
    {
        var error = Assert.Throws<InvalidDataException>(() => FbpImageDecoder.Decode(chunk, 64 * 1024));
        Assert.Contains(message, error.Message);
    }

    [Theory]
    // The reference decompresses fbp chunks into a fixed 320*200 buffer and blits all
    // 64000 bytes: a shorter payload is not a screen, and a longer declared length
    // fails the buffer limit inside the decompressor (YJ2_Decompress Length > DestSize).
    [InlineData(8)]
    [InlineData(64001)]
    public void Decode_rejects_bitmaps_that_are_not_the_fixed_screen(int length)
    {
        var encoder = new Yj2TestData(length);
        for (int i = 0; i < length; i++) encoder.Literal((byte)i);
        var error = Assert.Throws<InvalidDataException>(() => FbpImageDecoder.Decode(encoder.End(), 64 * 1024));
        Assert.Contains(length < 64000 ? "320x200" : "output limit", error.Message);
    }

    [Fact]
    public void DescribeChunk_labels_fbp_chunks_with_the_shared_yj2_compression()
    {
        byte[] chunk = CompressScreen(0);
        var sprite = FbpImageDecoder.Decode(chunk, 64 * 1024);
        var entry = FbpImageDecoder.DescribeChunk(3, chunk, sprite);
        Assert.Equal(3, (int)entry["index"]!);
        Assert.Equal("yj2", (string)entry["compression"]!);
        Assert.Equal(64000, (int)entry["decodedByteLength"]!);
        Assert.Equal(1, (int)entry["frameCount"]!);
        Assert.Equal(1, (int)entry["decodedFrameCount"]!);
        Assert.Equal("sprites/3.json", (string)entry["sprite"]!);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(chunk)), (string)entry["sha256"]!);
    }

    [Fact]
    public void DescribeChunk_marks_empty_chunks_without_decoded_fields()
    {
        // Real fbp.mkf chunk 5 and 58 are empty; PAL_MKFDecompressChunk returns no
        // bitmap for them and the consumers ignore the failure.
        var entry = FbpImageDecoder.DescribeChunk(5, [], null);
        Assert.Equal(0, (int)entry["byteLength"]!);
        Assert.Equal("empty", (string)entry["compression"]!);
        Assert.Null(entry["frameCount"]);
        Assert.Null(entry["sprite"]);
    }

    [Fact]
    public void DescribeSprite_documents_the_archive_and_raw_screen_layout()
    {
        var sprite = FbpImageDecoder.Decode(CompressScreen(0), 64 * 1024);
        var document = FbpImageDecoder.DescribeSprite(2, "fbp.mkf", sprite);
        Assert.Equal(1, (int)document["formatVersion"]!);
        Assert.Equal("faithful", (string)document["profile"]!);
        Assert.Equal(2, (int)document["chunk"]!);
        Assert.Equal("fbp.mkf", (string)document["source"]!["archive"]!);
        Assert.Equal("Windows YJ2", (string)document["source"]!["encoding"]!);
        Assert.Equal("single raw 320x200 indexed bitmap, linear row-major (no RLE, no offset table)",
            (string)document["source"]!["frameLayout"]!);
        var frame = Assert.Single(document["frames"]!.AsArray());
        Assert.Equal(320, (int)frame!["width"]!);
        Assert.Equal(200, (int)frame!["height"]!);
        Assert.Equal(0, (int)frame!["sourceOffset"]!);
        Assert.Equal("sprites/2/0.png", (string)frame!["png"]!);
        Assert.Equal(ExpectedScreen(0), Convert.FromBase64String((string)frame!["indices"]!));
    }
}
