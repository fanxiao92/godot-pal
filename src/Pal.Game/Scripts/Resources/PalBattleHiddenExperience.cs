using Godot;

namespace Pal.Game.Resources;

[GlobalClass] public partial class PalBattleHiddenExperience : Resource { [Export] public PalBattleExperience? Health {get;set;} [Export] public PalBattleExperience? Magic {get;set;} [Export] public PalBattleExperience? Attack {get;set;} [Export] public PalBattleExperience? MagicPower {get;set;} [Export] public PalBattleExperience? Defense {get;set;} [Export] public PalBattleExperience? Dexterity {get;set;} [Export] public PalBattleExperience? Flee {get;set;} }
