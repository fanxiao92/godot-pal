using Godot;

namespace Pal.Game.Resources;

/// <summary>#217 one authored store row (battle-data.json $.stores): the store WORD id and its
/// 1..9 item words in store order; Core validates every item's shop-object price binding.</summary>
[GlobalClass]
public partial class PalBattleStore : Resource
{
    [Export] public int StoreId { get; set; } = 0;
    [Export] public int[] Items { get; set; } = [];
}
