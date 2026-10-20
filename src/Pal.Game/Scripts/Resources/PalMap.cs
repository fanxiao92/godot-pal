using Godot;
using Godot.Collections;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalMap : Resource
{
    // Packed arrays use the original [y,x,h] order: (y * Width + x) * HalfRows + h. TopFrames uses -1 for absent.
    [Export] public int MapId { get; set; } = 0;
    [Export] public int Width { get; set; } = 64;
    [Export] public int Height { get; set; } = 128;
    [Export] public int HalfRows { get; set; } = 2;
    [Export] public int DecodedByteLength { get; set; } = 65536;
    [Export] public string CellOrder { get; set; } = "[y,x,h]";
    [Export] public Dictionary Source { get; set; } = new();
    [Export] public Dictionary Coordinates { get; set; } = new();
    [Export] public Dictionary PreviewMetadata { get; set; } = new();
    [Export] public Texture2D Preview { get; set; } = null!;
    [Export] public PalTiles Tiles { get; set; } = null!;
    [Export] public long[] RawCells { get; set; } = System.Array.Empty<long>();
    [Export] public int[] BottomFrames { get; set; } = System.Array.Empty<int>();
    [Export] public int[] TopFrames { get; set; } = System.Array.Empty<int>();
    [Export] public int[] BottomHeights { get; set; } = System.Array.Empty<int>();
    [Export] public int[] TopHeights { get; set; } = System.Array.Empty<int>();
    [Export] public byte[] Blocked { get; set; } = System.Array.Empty<byte>();
}
