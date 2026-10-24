using Xunit;

namespace Pal.Extract.Tests;

public class IndexedImageTests
{
    [Fact]
    public void Constructor_keeps_row_major_indices_alpha_and_source_metadata()
    {
        var image = new IndexedImage(2, 1, [0, 255], [255, 0], frameIndex: 17, sourceOffset: 42);
        Assert.Equal(2, image.Width);
        Assert.Equal(1, image.Height);
        Assert.Equal(17, image.FrameIndex);
        Assert.Equal(42, image.SourceOffset);
        Assert.Equal(new byte[] { 0, 255 }, image.Pixels);
        Assert.Equal(new byte[] { 255, 0 }, image.Alpha);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    [InlineData(65536, 65536)]
    [InlineData(int.MaxValue, int.MaxValue)]
    [InlineData(4096, 4097)]
    public void Constructor_rejects_invalid_dimensions_before_needing_pixel_storage(int width, int height) =>
        Assert.Throws<InvalidDataException>(() => new IndexedImage(width, height, [], []));

    [Fact]
    public void Constructor_requires_one_index_and_alpha_byte_per_pixel()
    {
        Assert.Throws<InvalidDataException>(() => new IndexedImage(2, 1, [1], [255, 255]));
        Assert.Throws<InvalidDataException>(() => new IndexedImage(2, 1, [1, 2], [255]));
        Assert.Throws<InvalidDataException>(() => new IndexedImage(1, 1, [1, 2], [255, 255]));
    }
}
