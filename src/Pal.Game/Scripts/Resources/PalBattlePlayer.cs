using Godot;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalBattlePlayer : Resource
{
    [Export] public int RoleId { get; set; } = 0;
    [Export] public int Hp { get; set; } = 0;
    [Export] public int MaxHp { get; set; } = 0;
    [Export] public int Attack { get; set; } = 0;
    [Export] public int Defense { get; set; } = 0;
    [Export] public int Dexterity { get; set; } = 0;
    [Export] public int DyingScript { get; set; } = 0;
    [Export] public int FriendDeathScript { get; set; } = 0;
    [Export] public int CoveredByRoleId { get; set; } = 0;
    [Export] public bool HasCoveredByRoleId { get; set; } = false;
    [Export] public bool AttackAll { get; set; } = false;
    [Export] public int[] Equipment { get; set; } = [];
    [Export] public int[] Statuses { get; set; } = [];
    [Export] public int[] Poisons { get; set; } = [];
}
