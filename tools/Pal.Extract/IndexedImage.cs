namespace Pal.Extract;

/// <summary>Row-major palette indices and independent 0..255 alpha; index zero is not a transparency key.</summary>
public sealed class IndexedImage
{
    public const int MaxPixels = 16 * 1024 * 1024;

    public int Width { get; }
    public int Height { get; }
    public int FrameIndex { get; }
    public int SourceOffset { get; }
    public byte[] Pixels { get; }
    public byte[] Alpha { get; }

    public IndexedImage(int width, int height, byte[] pixels, byte[] alpha, int frameIndex = 0, int sourceOffset = 0)
    {
        if (width <= 0 || height <= 0 || (long)width * height > MaxPixels)
            throw new InvalidDataException("Image dimensions exceed the pixel limit or are empty.");
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(alpha);
        if (pixels.Length != width * height || alpha.Length != pixels.Length)
            throw new InvalidDataException("Image pixel and alpha lengths must match its dimensions.");
        Width = width;
        Height = height;
        FrameIndex = frameIndex;
        SourceOffset = sourceOffset;
        Pixels = pixels;
        Alpha = alpha;
    }
}
