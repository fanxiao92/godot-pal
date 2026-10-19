namespace Pal.Extract;

public readonly record struct RgbColor(byte R, byte G, byte B);

public sealed record DecodedPalette(RgbColor[] Colors, bool UsesNightColors);

public static class PaletteDecoder
{
    /// <summary>Decodes a raw PAT chunk (not an MKF archive). RGB uses PAL_GetPalette's shift by two.</summary>
    public static DecodedPalette Decode(ReadOnlySpan<byte> raw, bool night)
    {
        if (raw.Length is not (768 or 1536))
            throw new InvalidDataException("PAT palette must contain 768 day bytes or 1536 day/night bytes.");
        foreach (byte component in raw)
            if (component > 63) throw new InvalidDataException("PAT RGB component is outside the six-bit range.");
        bool usesNightColors = night && raw.Length > 768;
        int offset = usesNightColors ? 768 : 0;
        var colors = new RgbColor[256];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = new RgbColor((byte)(raw[offset + i * 3] << 2), (byte)(raw[offset + i * 3 + 1] << 2), (byte)(raw[offset + i * 3 + 2] << 2));
        return new DecodedPalette(colors, usesNightColors);
    }
}
