using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Pal.Extract;

/// <summary>
/// Decodes one ball.mkf chunk (item bitmap) into its single frame (#93). The reference
/// reads ball chunks raw with PAL_MKFReadChunk - itemmenu.c, script.c and uigame.c blit
/// the chunk bytes selected by the item object's wBitmap directly with
/// PAL_RLEBlitToSurface - so unlike abc.mkf there is no MKF decompression and no sprite
/// WORD offset table: the optional 0x00000002 file header, WORD width/height and RLE
/// runs are the whole chunk, and every non-empty chunk is exactly one bitmap.
/// </summary>
public static class BallImageDecoder
{
    /// <exception cref="InvalidDataException">Chunk is not a decodable PAL RLE bitmap.</exception>
    public static DecodedBattleSprite Decode(byte[] chunk, int maximumPixels)
    {
        var frame = SpriteDecoder.DecodeRle(chunk, maximumPixels);
        return new DecodedBattleSprite(chunk.Length, 1, [frame]);
    }

    /// <summary>index.json manifest entry; empty chunks carry no decoded fields, as PAL_MKFReadChunk returns no bitmap for them.</summary>
    public static JsonObject DescribeChunk(int index, ReadOnlySpan<byte> chunk, DecodedBattleSprite? sprite) =>
        BattleImageDecoder.DescribeChunk(index, chunk, sprite, chunk.Length == 0 ? "empty" : "rle");

    /// <summary>sprites/&lt;chunk&gt;.json document; indices and alpha are the authoritative planes, the PNG is a colorized view.</summary>
    public static JsonObject DescribeSprite(int chunkIndex, string archiveName, DecodedBattleSprite sprite) =>
        BattleImageDecoder.DescribeSprite(chunkIndex, archiveName, sprite,
            "uncompressed PAL RLE bitmap",
            "single RLE bitmap: optional 0x00000002 header, WORD width/height, RLE runs");
}
