using Godot;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalBattleVisualTeam : Resource
{
    [Export] public int TeamId { get; set; } = -1;
    // Rejection-only legacy sentinels. Native loading never projects these into slots;
    // retained to diagnose scalar-only/mixed serialized declarations, not compatibility.
    [Export] public int TargetId { get; set; } = -1;
    [Export] public string BackgroundUri { get; set; } = "";
    [Export] public string EnemySpriteUri { get; set; } = "";
    [Export] public Vector2I EnemyPosition { get; set; } = new(int.MinValue, int.MinValue);
    // Required even for one enemy. Slot order is the instance address; duplicate IDs
    // remain distinct. Prepare must write this array explicitly.
    [Export] public PalBattleVisualEnemySlot[] EnemySlots { get; set; } = [];
    [Export] public int IdleFrames { get; set; } = -1;
    [Export] public int IdleAnimSpeed { get; set; } = -1;
    [Export] public int MagicFrames { get; set; } = -1;
    [Export] public int AttackFrames { get; set; } = -1;
    [Export] public int ActWaitFrames { get; set; } = -1;
}
