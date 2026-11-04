using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Pal.Extract;
using Xunit;

namespace Pal.Extract.Tests;

// Ball image decoder (ball.mkf, #93) - the item bitmap unit. Unlike abc (#92) the
// reference reads ball chunks raw (PAL_MKFReadChunk in itemmenu.c/script.c/uigame.c)
// and blits them directly as single PAL RLE bitmaps: no YJ1/YJ2 decompression and no
// sprite WORD offset table. Fixtures are hand-authored bitmap byte literals; expected
// pixels come from hand-walking the RLE commands, never from the decoder under test.
public sealed class BallImageDecoderTests
{
    // 0x00000002 file header, 4x2 bitmap: opaque 1 (index 7), skip 2, opaque 3
    // (8,9,10), opaque 2 (11,12). The first word 0x0002 would be a four-word sprite
    // table in abc's shape; ball never reads an offset table.
    private static byte[] Bitmap => [2, 0, 0, 0, 4, 0, 2, 0,
        1, 7, 0x82, 3, 8, 9, 10, 2, 11, 12];

    // 1x1 bitmap: a single opaque pixel (index 5).
    private static byte[] OnePixel => [2, 0, 0, 0, 1, 0, 1, 0, 1, 5];

    [Fact]
    public void Decode_returns_one_frame_per_chunk_without_a_sprite_table()
    {
        var sprite = BallImageDecoder.Decode(Bitmap, IndexedImage.MaxPixels);
        Assert.Equal(18, sprite.DecodedByteLength); // no decompression: the chunk is the bitmap
        Assert.Equal(1, sprite.FrameCount);
        var frame = Assert.Single(sprite.Frames);
        Assert.Equal((0, 4, 2, 0), (frame.FrameIndex, frame.Width, frame.Height, frame.SourceOffset));
        Assert.Equal(new byte[] { 7, 0, 0, 8, 9, 10, 11, 12 }, frame.Pixels);
        Assert.Equal(new byte[] { 255, 0, 0, 255, 255, 255, 255, 255 }, frame.Alpha);
    }

    [Fact]
    public void Decode_decodes_the_bare_dimensions_when_the_file_header_is_absent()
    {
        // PAL_RLEGetWidth/BlitToSurface treat the 0x00000002 header as optional; a 2x1
        // bitmap stored bare (bytes 02 00 01 00 differ from the header) decodes too.
        // All real ball chunks carry the header; this pins the conditional skip.
        var sprite = BallImageDecoder.Decode([2, 0, 1, 0, 2, 7, 8], IndexedImage.MaxPixels);
        var frame = Assert.Single(sprite.Frames);
        Assert.Equal((2, 1), (frame.Width, frame.Height));
        Assert.Equal(new byte[] { 7, 8 }, frame.Pixels);
        Assert.Equal(new byte[] { 255, 255 }, frame.Alpha);
    }

    [Fact]
    public void Decode_enforces_the_shared_pixel_budget()
    {
        var sprite = BallImageDecoder.Decode(OnePixel, 1);
        Assert.Equal(new byte[] { 5 }, Assert.Single(sprite.Frames).Pixels);
        var error = Assert.Throws<InvalidDataException>(() => BallImageDecoder.Decode(Bitmap, 7));
        Assert.Contains("pixel limit", error.Message);
    }

    [Theory]
    // Header only: dimensions truncated. / Zero width: empty image. / Run of 3 with one
    // byte left: pixels truncated. / Opaque run of 4 on a 1-pixel image: run exceeds it.
    [InlineData(new byte[] { 2, 0, 0, 0, 4, 0 }, "dimensions are truncated")]
    [InlineData(new byte[] { 2, 0, 0, 0, 0, 0, 1, 0 }, "dimensions exceed the pixel limit or are empty")]
    [InlineData(new byte[] { 2, 0, 0, 0, 4, 0, 2, 0, 3, 8 }, "RLE pixels are truncated")]
    [InlineData(new byte[] { 2, 0, 0, 0, 1, 0, 1, 0, 4, 5 }, "RLE run makes no progress or exceeds the image")]
    public void Decode_rejects_damaged_bitmaps(byte[] chunk, string message)
    {
        var error = Assert.Throws<InvalidDataException>(() => BallImageDecoder.Decode(chunk, IndexedImage.MaxPixels));
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void Decode_rejects_data_beyond_the_final_run()
    {
        // The runs above consume all 18 bytes exactly; two extra bytes are not word
        // padding but trailing corruption. One alignment byte stays tolerated.
        byte[] padded = [.. Bitmap, 0, 0];
        var error = Assert.Throws<InvalidDataException>(() => BallImageDecoder.Decode(padded, IndexedImage.MaxPixels));
        Assert.Contains("data beyond its final run", error.Message);
    }

    [Fact]
    public void DescribeChunk_records_raw_storage_identity_and_the_single_frame()
    {
        var sprite = BallImageDecoder.Decode(Bitmap, IndexedImage.MaxPixels);
        var entry = BallImageDecoder.DescribeChunk(9, Bitmap, sprite);
        Assert.Equal(9, (int)entry["index"]!);
        Assert.Equal(18, (int)entry["byteLength"]!);
        // Independent expectation: hash the fixture bytes again in the test.
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Bitmap)), (string)entry["sha256"]!);
        Assert.Equal("rle", (string)entry["compression"]!);
        Assert.Equal(18, (int)entry["decodedByteLength"]!);
        Assert.Equal(1, (int)entry["frameCount"]!);
        Assert.Equal(1, (int)entry["decodedFrameCount"]!);
        Assert.Equal("sprites/9.json", (string)entry["sprite"]!);
    }

    [Fact]
    public void DescribeChunk_marks_empty_chunks_without_decoded_fields()
    {
        var entry = BallImageDecoder.DescribeChunk(0, [], null);
        Assert.Equal(0, (int)entry["byteLength"]!);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Array.Empty<byte>())), (string)entry["sha256"]!);
        Assert.Equal("empty", (string)entry["compression"]!);
        Assert.Null(entry["frameCount"]);
        Assert.Null(entry["sprite"]);
    }

    [Fact]
    public void DescribeSprite_documents_the_single_frame_with_planes_and_png_reference()
    {
        var sprite = BallImageDecoder.Decode(Bitmap, IndexedImage.MaxPixels);
        var document = BallImageDecoder.DescribeSprite(3, "ball.mkf", sprite);
        Assert.Equal(1, (int)document["formatVersion"]!);
        Assert.Equal("faithful", (string)document["profile"]!);
        Assert.Equal(3, (int)document["chunk"]!);
        Assert.Equal("ball.mkf", (string)document["source"]!["archive"]!);
        Assert.Equal("uncompressed PAL RLE bitmap", (string)document["source"]!["encoding"]!);
        Assert.Equal("single RLE bitmap: optional 0x00000002 header, WORD width/height, RLE runs",
            (string)document["source"]!["frameLayout"]!);
        Assert.Equal(1, (int)document["frameCount"]!);
        var frame = Assert.Single(document["frames"]!.AsArray());
        Assert.Equal("sprites/3/0.png", (string)frame!["png"]!);
        Assert.Equal((4, 2, 0), ((int)frame!["width"]!, (int)frame!["height"]!, (int)frame!["sourceOffset"]!));
        Assert.Equal(new byte[] { 7, 0, 0, 8, 9, 10, 11, 12 }, Convert.FromBase64String((string)frame!["indices"]!));
        Assert.Equal(new byte[] { 255, 0, 0, 255, 255, 255, 255, 255 }, Convert.FromBase64String((string)frame!["alpha"]!));
    }
}
