using Godot;
using Godot.Collections;

namespace Pal.Game.Resources;

/// <summary>One global event-object record (script.c EVENTOBJECT word set) from outside the
/// bound scene's static bundle; consumed only by the #53 first-battle closure path.</summary>
[GlobalClass]
public partial class PalGlobalEventObject : Resource
{
    [Export] public int OwnerId { get; set; }
    [Export] public int X { get; set; }
    [Export] public int Y { get; set; }
    [Export] public int Direction { get; set; }
    [Export] public int State { get; set; }
    [Export] public int VanishTime { get; set; }
    [Export] public int TriggerMode { get; set; }
    [Export] public int TriggerEntry { get; set; }
    [Export] public int AutoEntry { get; set; }
    [Export] public int AutoIdleFrameCount { get; set; }
    [Export] public int SpriteId { get; set; }
    [Export] public int SpriteFrames { get; set; }
    [Export] public int CurrentFrame { get; set; }
    [Export] public int ScriptIdleFrameCount { get; set; }
    [Export] public int SpritePtrOffset { get; set; }
    [Export] public int AutoSpriteFrames { get; set; }
    [Export] public int Layer { get; set; }
}
