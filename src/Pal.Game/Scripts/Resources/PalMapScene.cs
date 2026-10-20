using Godot;
using Pal.Game.Map;

namespace Pal.Game.Resources;

// Native map root. Object/party behavior is deliberately not activated here.
[GlobalClass]
public partial class PalMapScene : Node2D
{
    [Export] public PalSceneData Data { get; set; } = null!;
    // T2 test viewport origin only, not a party spawn. T3 replaces this with the leader camera.
    [Export] public Vector2I TestViewpoint { get; set; } = Vector2I.Zero;

    private bool initialized;

    public override void _Ready() => InitializeMap();

    // Main calls before AddChild so native-resource failures cannot be swallowed by Godot's _Ready bridge.
    internal void InitializeMap()
    {
        if (initialized) return;
        var textures = NativeMapValidation.Textures(Data);
        TextureFilter = TextureFilterEnum.Nearest;
        var map = Data.Map;
        // PAL_MapBlitToSurface: layer -> y -> h -> x, not packed [y,x,h] order.
        // C# integer division, like C, truncates toward zero (also for negative origins).
        int sx = TestViewpoint.X / 32 - 1, dx = (TestViewpoint.X + 320) / 32 + 2;
        int sy = TestViewpoint.Y / 16 - 1, dy = (TestViewpoint.Y + 200) / 16 + 2;
        for (int layer = 0; layer < 2; layer++)
        for (int y = sy; y < dy; y++)
        for (int h = 0; h < 2; h++)
        for (int x = sx; x < dx; x++)
        {
            // The reference casts to BYTE before querying the fixed 64x128 map.
            int mx = unchecked((byte)x), my = unchecked((byte)y);
            int index = mx < 64 && my < 128 ? (my * 64 + mx) * 2 + h : -1;
            int frame = index < 0 ? (layer == 0 ? map.BottomFrames[0] : -1)
                : layer == 0 ? map.BottomFrames[index] : map.TopFrames[index];
            if (frame < 0) continue;
            AddChild(new Sprite2D
            {
                Texture = textures[frame], Centered = false,
                TextureFilter = TextureFilterEnum.Nearest,
                Position = new Vector2(x * 32 + h * 16 - 16 - TestViewpoint.X, y * 16 + h * 8 - 8 - TestViewpoint.Y)
            });
        }
        initialized = true;
    }
}
