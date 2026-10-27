using Godot;

namespace Pal.Game.Resources;

[GlobalClass] public partial class PalBattleClosureEntry : Resource { [Export] public string Id {get;set;}=""; [Export] public string Category {get;set;}=""; [Export] public PalBattleClosureIdentity? Identity {get;set;} [Export] public int[] OwnerTickets {get;set;}=[]; [Export] public string Status {get;set;}=""; [Export] public string[] ResourceIds {get;set;}=[]; }
