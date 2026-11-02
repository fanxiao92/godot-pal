using Godot;

namespace Pal.Game.Resources;

[GlobalClass]
public partial class PalBattleEnemy : Resource
{
    [Export] public int Id { get; set; } = 0;
    [Export] public int Level { get; set; } = 0;
    [Export] public int Hp { get; set; } = 0;
    [Export] public int Attack { get; set; } = 0;
    [Export] public int Defense { get; set; } = 0;
    [Export] public int Dexterity { get; set; } = 0;
    [Export] public int PhysicalResistance { get; set; } = 0;
    [Export] public int TurnStartScript { get; set; } = 0;
    [Export] public int ReadyScript { get; set; } = 0;
    [Export] public int BattleEndScript { get; set; } = 0;
    [Export] public int MagicId { get; set; } = 0;
    [Export] public int DualMove { get; set; } = 0;
    [Export] public int AttackEquivItem { get; set; } = 0;
    [Export] public int AttackEquivItemRate { get; set; } = 0;
    [Export] public int Experience { get; set; } = 0;
    [Export] public int Cash { get; set; } = 0;
    [Export] public int[] Statuses { get; set; } = [];
    [Export] public int[] Poisons { get; set; } = [];
}
