using Godot;
using Godot.Collections;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalSceneObject : Resource
{
    [Export] public int Id { get; set; } = 0;
    [Export] public int SVanishTime { get; set; } = 0;
    [Export] public int X { get; set; } = 0;
    [Export] public int Y { get; set; } = 0;
    [Export] public int SLayer { get; set; } = 0;
    [Export] public int WTriggerScript { get; set; } = 0;
    [Export] public int WAutoScript { get; set; } = 0;
    [Export] public int SState { get; set; } = 0;
    [Export] public int WTriggerMode { get; set; } = 0;
    [Export] public int WSpriteNum { get; set; } = 0;
    [Export] public int NSpriteFrames { get; set; } = 0;
    [Export] public int WDirection { get; set; } = 0;
    [Export] public int WCurrentFrameNum { get; set; } = 0;
    [Export] public int NScriptIdleFrame { get; set; } = 0;
    [Export] public int WSpritePtrOffset { get; set; } = 0;
    [Export] public int NSpriteFramesAuto { get; set; } = 0;
    [Export] public int WScriptIdleFrameCountAuto { get; set; } = 0;
    [Export] public int InitialFrameIndex { get; set; } = -1;
    [Export] public bool InitiallyVisible { get; set; } = false;
    [Export] public PalSprite Sprite { get; set; } = null!;
}
