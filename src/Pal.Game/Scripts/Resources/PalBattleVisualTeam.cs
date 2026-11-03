using Godot;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalBattleVisualTeam : Resource
{
    [Export] public int TeamId { get; set; } = -1;
    [Export] public int TargetId { get; set; } = -1;
    [Export] public string BackgroundUri { get; set; } = "";
    [Export] public string EnemySpriteUri { get; set; } = "";
    [Export] public Vector2I EnemyPosition { get; set; } = new(int.MinValue, int.MinValue);
    // Optional strict slot binding (one per story enemy slot, slot order preserved). When
    // present it replaces the legacy single TargetId/EnemyPosition pair; a multi-enemy story
    // team must use it, duplicate enemy IDs are addressed per slot, never merged.
    [Export] public PalBattleVisualEnemySlot[] EnemySlots { get; set; } = [];
    [Export] public int IdleFrames { get; set; } = -1;
    [Export] public int IdleAnimSpeed { get; set; } = -1;
    [Export] public int MagicFrames { get; set; } = -1;
    [Export] public int AttackFrames { get; set; } = -1;
    [Export] public int ActWaitFrames { get; set; } = -1;
}
