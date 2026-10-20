using System;
using Godot;
using Pal.Core;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalMapScene : Node2D
{
    [Export] public PalSceneData Data { get; set; } = null!;
    private (Texture2D Texture, Vector2 Position)[]? bitmaps;
    private Func<SceneDrawCommand, Texture2D>? textureFor;

    // Resolve before AddChild: resource failures must reach Main, not Godot's _Ready bridge.
    // Core owns the complete painter order and bitmap top-lefts, including repeated covers.
    public void InitializeScene(StaticScenePlan plan, Func<SceneDrawCommand, Texture2D> textureFor)
    {
        if (bitmaps != null) throw new InvalidOperationException("Static scene already initialized");
        var resolved = Resolve(plan, textureFor);
        this.textureFor = textureFor;
        bitmaps = resolved;
        TextureFilter = TextureFilterEnum.Nearest;
        QueueRedraw();
    }

    public void UpdateScene(StaticScenePlan plan)
    {
        if (textureFor == null) throw new InvalidOperationException("InitializeScene is required before UpdateScene");
        var resolved = Resolve(plan, textureFor);
        bitmaps = resolved;
        QueueRedraw();
    }

    private static (Texture2D Texture, Vector2 Position)[] Resolve(StaticScenePlan plan,
        Func<SceneDrawCommand, Texture2D> textureFor)
    {
        var resolved = new (Texture2D, Vector2)[plan.Commands.Count];
        for (int i = 0; i < plan.Commands.Count; i++)
        {
            var command = plan.Commands[i];
            var texture = textureFor(command) ?? throw new InvalidOperationException("Scene texture resolver returned null");
            resolved[i] = (texture, new Vector2(command.Left, command.Top));
        }
        return resolved;
    }

    public override void _Ready()
    {
        if (bitmaps == null) throw new InvalidOperationException("InitializeScene is required before entering the tree");
    }

    public override void _Draw()
    {
        if (bitmaps == null) return;
        foreach (var (texture, position) in bitmaps) DrawTexture(texture, position);
    }
}
