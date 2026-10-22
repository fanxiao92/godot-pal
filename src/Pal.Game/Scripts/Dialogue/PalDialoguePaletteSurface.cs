using System;
using System.IO;
using System.Linq;
using Godot;
using Pal.Game.Resources;

namespace Pal.Game.Dialogue;

/// <summary>Small indexed-background target for hosts that apply a dialogue scene-palette change.</summary>
public partial class PalDialoguePaletteSurface : Control
{
    private Color[] _palette = [];
    private PalDialogueIndexedImage? _image;
    private ImageTexture? _texture;
    private Image? _renderedImage;

    public void SetImage(PalDialogueIndexedImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        image.Validate();
        _image = image;
        UpdateTexture();
    }

    public void SetPalette(Color[] palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        if (palette.Length != 256) throw new InvalidDataException("Indexed palette surface requires 256 colors.");
        _palette = palette.ToArray();
        UpdateTexture();
    }

    public Color GetRenderedPixel(int x, int y)
    {
        if (_renderedImage is null || x < 0 || y < 0 || x >= _renderedImage.GetWidth() || y >= _renderedImage.GetHeight())
            throw new ArgumentOutOfRangeException(nameof(x));
        return _renderedImage.GetPixel(x, y);
    }

    public override void _Draw()
    {
        if (_texture is not null) DrawTexture(_texture, Vector2.Zero);
    }

    private void UpdateTexture()
    {
        if (_palette.Length != 256 || _image is null) return;
        _renderedImage = Image.CreateEmpty(_image.Width, _image.Height, false, Image.Format.Rgba8);
        for (int y = 0; y < _image.Height; y++)
        for (int x = 0; x < _image.Width; x++)
        {
            int index = y * _image.Width + x;
            Color color = _palette[_image.PaletteIndices[index]];
            color.A *= _image.Alpha[index] / 255.0f;
            _renderedImage.SetPixel(x, y, color);
        }
        _texture = ImageTexture.CreateFromImage(_renderedImage);
        QueueRedraw();
    }
}
