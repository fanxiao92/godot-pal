using Godot;

namespace Pal.Game.Resources;

[GlobalClass] public partial class PalBattleFieldSource : Resource { [Export] public string Path {get;set;}=""; [Export] public string SourceKind {get;set;}=""; [Export] public bool HasSourceAddress {get;set;} [Export] public int SourceAddress {get;set;} [Export] public string AppliesTo {get;set;}=""; }
