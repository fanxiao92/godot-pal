using Godot;

namespace Pal.Game.Resources;

[GlobalClass] public partial class PalBattleResourceReference : Resource { [Export] public string Id {get;set;}=""; [Export] public string Kind {get;set;}=""; [Export] public new string Reference {get;set;}=""; [Export] public string Sha256 {get;set;}=""; [Export] public int[] OwnerTickets {get;set;}=[]; }
