using Xunit;

namespace Pal.Extract.Tests;

public class PaletteDecoderTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(767)]
    [InlineData(769)]
    [InlineData(1535)]
    [InlineData(1537)]
    public void Decode_rejects_incomplete_or_oversized_palette_chunks(int length) =>
        Assert.Throws<InvalidDataException>(() => PaletteDecoder.Decode(new byte[length], night: true));

    [Theory]
    [InlineData(64)]
    [InlineData(255)]
    public void Decode_rejects_components_outside_six_bit_range(byte value)
    {
        var raw = new byte[1536];
        raw[1535] = value;
        Assert.Throws<InvalidDataException>(() => PaletteDecoder.Decode(raw, night: true));
    }

    [Fact]
    public void Decode_selects_night_half_and_falls_back_to_day_when_absent()
    {
        var raw = new byte[1536];
        new byte[] { 1, 2, 3 }.CopyTo(raw, 0);
        new byte[] { 63, 32, 16 }.CopyTo(raw, 768);
        Assert.Equal(new RgbColor(4, 8, 12), PaletteDecoder.Decode(raw, night: false).Colors[0]);
        var night = PaletteDecoder.Decode(raw, night: true);
        Assert.True(night.UsesNightColors);
        Assert.Equal(new RgbColor(252, 128, 64), night.Colors[0]);
        var fallback = PaletteDecoder.Decode(raw[..768], night: true);
        Assert.False(fallback.UsesNightColors);
        Assert.Equal(new RgbColor(4, 8, 12), fallback.Colors[0]);
    }

    [Fact]
    public void Decode_expands_six_bit_rgb_without_bit_replication()
    {
        var raw = new byte[768];
        new byte[] { 0, 1, 63, 32, 15, 2 }.CopyTo(raw, 0);
        var palette = PaletteDecoder.Decode(raw, night: false);
        Assert.False(palette.UsesNightColors);
        Assert.Equal(256, palette.Colors.Length);
        Assert.Equal(new RgbColor(0, 4, 252), palette.Colors[0]);
        Assert.Equal(new RgbColor(128, 60, 8), palette.Colors[1]);
        Assert.Equal(new RgbColor(0, 0, 0), palette.Colors[255]);
    }
}
