using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Pal.Extract;

/// <summary>
/// Decodes one fbp.mkf chunk (battlefield background, #95) into its single fullscreen
/// frame. The reference loads every fbp picture through PAL_MKFDecompressChunk with a
/// 320*200 buffer (battle.c PAL_LoadBattleBackground for wNumBattleField, uigame.c
/// menu backgrounds, ending.c PAL_ShowFBP/PAL_ScrollFBP slides) and
/// PAL_FBPBlitToSurface then copies the decompressed bytes linearly onto a 320x200
/// 8-bit surface: unlike abc/f there is no sprite WORD offset table and unlike ball
/// no RLE runs - the YJ2 payload must decompress to exactly the 64000 raw palette
/// indices of one screen, fully opaque (index zero is a color, not a transparency key).
/// </summary>
public static class FbpImageDecoder
{
    public const int ScreenWidth = 320;
    public const int ScreenHeight = 200;
    public const int ScreenBytes = ScreenWidth * ScreenHeight;

    /// <exception cref="InvalidDataException">Chunk is not a Windows YJ2 320x200 background.</exception>
    public static DecodedBattleSprite Decode(byte[] chunk, int maximumDecodedBytes)
    {
        // PAL_MKFDecompressChunk is called with exactly a 320*200 destination buffer,
        // so a longer declared YJ2 length fails the shared limit before allocation.
        byte[] bitmap = Yj2Decoder.Decode(chunk, Math.Min(maximumDecodedBytes, ScreenBytes));
        if (bitmap.Length != ScreenBytes)
            throw new InvalidDataException("FBP background does not decompress to the fixed 320x200 screen bitmap.");
        var alpha = new byte[ScreenBytes];
        Array.Fill(alpha, (byte)255);
        return new DecodedBattleSprite(bitmap.Length, 1,
            [new IndexedImage(ScreenWidth, ScreenHeight, bitmap, alpha)]);
    }

    /// <summary>index.json manifest entry; empty chunks carry no decoded fields, as PAL_MKFDecompressChunk returns no bitmap for them.</summary>
    public static JsonObject DescribeChunk(int index, ReadOnlySpan<byte> chunk, DecodedBattleSprite? sprite) =>
        BattleImageDecoder.DescribeChunk(index, chunk, sprite, chunk.Length == 0 ? "empty" : "yj2");

    /// <summary>sprites/&lt;chunk&gt;.json document; indices and alpha are the authoritative planes, the PNG is a colorized view.</summary>
    public static JsonObject DescribeSprite(int chunkIndex, string archiveName, DecodedBattleSprite sprite) =>
        BattleImageDecoder.DescribeSprite(chunkIndex, archiveName, sprite,
            "Windows YJ2",
            "single raw 320x200 indexed bitmap, linear row-major (no RLE, no offset table)");
}
