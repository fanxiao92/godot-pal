using System.Collections.ObjectModel;
using System.Text.Json;

namespace Pal.Core;

public sealed record BattleContextIdentity(string SnapshotKind, int SceneId, int MapId, int OwnerId, int TriggerAddress);
public sealed record BattleContextLayout(int EquipmentRoleCount, int EquipmentSlotCount, int LevelUpMagicRoleCount);
public sealed record BattleFieldSource(string Path, string SourceKind, int? SourceAddress, string AppliesTo);
public sealed record BattleContextProvenance(bool OptionalGrinding, bool PlayerEquipmentChanges, IReadOnlyList<BattleFieldSource> FieldSources);
public sealed record BattleExperienceSnapshot(int Value, int Level, int Count);
public sealed record BattleLeaderSnapshot(int X, int Y, string Direction, int Layer, int Phase);
public sealed record BattleSearchProof(int OwnerId, int SampleIndex, bool CollisionPass);
public sealed record BattleSceneEntrySnapshot(int SceneId, int EnterEntry, int TeleportEntry);
public sealed record BattleEventObjectSnapshot(int OwnerId, int X, int Y, int Direction, int State, int VanishTime, int TriggerMode,
    int TriggerEntry, int AutoEntry, int AutoIdleFrameCount, int SpriteId, int SpriteFrames, int CurrentFrame,
    int ScriptIdleFrameCount, int SpritePtrOffset, int AutoSpriteFrames, int Layer);

public sealed record BattleRoleSnapshot(
    int RoleId, int Level, int Hp, int MaxHp, int Mp, int MaxMp,
    BattleExperienceSnapshot PrimaryExperience,
    IReadOnlyDictionary<string, BattleExperienceSnapshot> HiddenExperience,
    IReadOnlyList<int> Equipment, IReadOnlyList<int> Magics, IReadOnlyList<int> Statuses, int CoveredByRoleId);

public sealed record BattleWorldSnapshot(
    int SceneId, int MapId, int PaletteId, bool NightPalette, int MusicId, int BattleMusicId,
    BattleLeaderSnapshot Leader, BattleSearchProof SearchProof,
    IReadOnlyList<BattleSceneEntrySnapshot> SceneEntries, IReadOnlyList<BattleEventObjectSnapshot> EventObjects);

/// <summary>Closed base type for the nine frozen closure identity variants.</summary>
public abstract record BattleClosureIdentity
{
    private protected BattleClosureIdentity() { }
}

/// <summary>Identifies the WORD-addressed pre-battle, battle, won, and end event range.</summary>
public sealed record BattleEventRangeIdentity(int StartAddress, int BattleAddress, int WonStartAddress, int EndAddress) : BattleClosureIdentity;

/// <summary>Identifies the frozen battle command and its WORD operands and semantic target fields.</summary>
public sealed record BattleCommandIdentity(int CommandAddress, IReadOnlyList<int> Operands, int EnemyTeamId,
    int EnemyObjectId, int EnemyId, int EnemyCount, int BattlefieldId, string SpecialValue, string ReverseEncoding) : BattleClosureIdentity;

/// <summary>Identifies the dying and friend-death role hook WORD-address ranges.</summary>
public sealed record BattleRoleHooksIdentity(int DyingRoleId, int DyingStartAddress, int DyingEndAddress,
    int FriendDeathRoleId, int FriendDeathStartAddress, int FriendDeathEndAddress, string BranchCoverage) : BattleClosureIdentity;

/// <summary>Identifies the six-role equipment layout and persistent equipment behavior.</summary>
public sealed record BattleEquipmentLayoutIdentity(int RoleCount, int SlotCount, int DualAttackObjectId,
    bool PersistentEquipmentStatus) : BattleClosureIdentity;

/// <summary>Identifies the five-role growth layout and required experience and cleanup behavior.</summary>
public sealed record BattleGrowthLayoutIdentity(int RoleCount, bool IncludesPrimaryExperience,
    bool IncludesHiddenExperience, bool IncludesRecoveryAndCleanup) : BattleClosureIdentity;

/// <summary>Identifies the dynamic auto-script WORD range and its immutable effect names.</summary>
public sealed record BattleDynamicAutoIdentity(int StartAddress, int EndAddress, IReadOnlyList<string> Effects,
    string Advance) : BattleClosureIdentity;

/// <summary>Identifies the lost-entry WORD address, reload command, and fail-closed behavior.</summary>
public sealed record BattleFailureBoundaryIdentity(int LostEntryAddress, int ReloadCommand, string Behavior) : BattleClosureIdentity;

/// <summary>Identifies the frozen enemy, role, and battlefield image resources.</summary>
public sealed record BattleImageSetIdentity(int EnemyId, string EnemyArchiveEntry, IReadOnlyList<string> RoleSpriteIds,
    int BattlefieldId, string BattlefieldArchiveEntry, bool IncludesLayoutAndEffectFrames) : BattleClosureIdentity;

/// <summary>Identifies the immutable language and localized content sets.</summary>
public sealed record BattleLocalizedDialogueIdentity(IReadOnlyList<string> Languages,
    IReadOnlyList<string> Contents) : BattleClosureIdentity;

public sealed record BattleClosureEntry(string Id, string Category, BattleClosureIdentity Identity,
    IReadOnlyList<int> OwnerTickets, string Status, IReadOnlyList<string> ResourceIds);
public sealed record BattleClosure(string Entry, IReadOnlyList<BattleClosureEntry> Entries);
public sealed record BattleResourceReference(string Id, string Kind, string Reference, string Sha256, IReadOnlyList<int> OwnerTickets);

public sealed class FirstMandatoryBattleSessionState
{
    public int[] PartyRoleIds { get; }
    public BattleRoleState[] Roles { get; }
    public BattleWorldState World { get; }

    internal FirstMandatoryBattleSessionState(FirstMandatoryBattleProfile profile)
    {
        PartyRoleIds = profile.PartyRoleIds.ToArray();
        Roles = profile.Roles.Select(role => new BattleRoleState(role)).ToArray();
        World = new BattleWorldState(profile.World);
    }
}

public sealed class BattleExperienceState
{
    public int Value { get; set; }
    public int Level { get; set; }
    public int Count { get; set; }
    internal BattleExperienceState(BattleExperienceSnapshot source) => (Value, Level, Count) = (source.Value, source.Level, source.Count);
}

public sealed class BattleRoleState
{
    public int RoleId { get; }
    public int Level { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public int Mp { get; set; }
    public int MaxMp { get; set; }
    public BattleExperienceState PrimaryExperience { get; }
    public Dictionary<string, BattleExperienceState> HiddenExperience { get; }
    public int[] Equipment { get; }
    public int[] Magics { get; set; }
    public int[] Statuses { get; }
    public int CoveredByRoleId { get; set; }

    internal BattleRoleState(BattleRoleSnapshot source)
    {
        (RoleId, Level, Hp, MaxHp, Mp, MaxMp, CoveredByRoleId) =
            (source.RoleId, source.Level, source.Hp, source.MaxHp, source.Mp, source.MaxMp, source.CoveredByRoleId);
        PrimaryExperience = new(source.PrimaryExperience);
        HiddenExperience = source.HiddenExperience.ToDictionary(pair => pair.Key, pair => new BattleExperienceState(pair.Value), StringComparer.Ordinal);
        Equipment = source.Equipment.ToArray();
        Magics = source.Magics.ToArray();
        Statuses = source.Statuses.ToArray();
    }
}

public sealed class BattleLeaderState
{
    public int X { get; set; }
    public int Y { get; set; }
    public string Direction { get; set; }
    public int Layer { get; set; }
    public int Phase { get; set; }
    internal BattleLeaderState(BattleLeaderSnapshot source) => (X, Y, Direction, Layer, Phase) = (source.X, source.Y, source.Direction, source.Layer, source.Phase);
}

public sealed class BattleSceneEntryState
{
    public int SceneId { get; }
    public int EnterEntry { get; set; }
    public int TeleportEntry { get; set; }
    internal BattleSceneEntryState(BattleSceneEntrySnapshot source) => (SceneId, EnterEntry, TeleportEntry) = (source.SceneId, source.EnterEntry, source.TeleportEntry);
}

public sealed class BattleEventObjectState
{
    public int OwnerId { get; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Direction { get; set; }
    public int State { get; set; }
    public int VanishTime { get; set; }
    public int TriggerMode { get; set; }
    public int TriggerEntry { get; set; }
    public int AutoEntry { get; set; }
    public int AutoIdleFrameCount { get; set; }
    public int SpriteId { get; set; }
    public int SpriteFrames { get; set; }
    public int CurrentFrame { get; set; }
    public int ScriptIdleFrameCount { get; set; }
    public int SpritePtrOffset { get; set; }
    public int AutoSpriteFrames { get; set; }
    public int Layer { get; set; }

    internal BattleEventObjectState(BattleEventObjectSnapshot s) =>
        (OwnerId, X, Y, Direction, State, VanishTime, TriggerMode, TriggerEntry, AutoEntry, AutoIdleFrameCount, SpriteId,
         SpriteFrames, CurrentFrame, ScriptIdleFrameCount, SpritePtrOffset, AutoSpriteFrames, Layer) =
        (s.OwnerId, s.X, s.Y, s.Direction, s.State, s.VanishTime, s.TriggerMode, s.TriggerEntry, s.AutoEntry, s.AutoIdleFrameCount,
         s.SpriteId, s.SpriteFrames, s.CurrentFrame, s.ScriptIdleFrameCount, s.SpritePtrOffset, s.AutoSpriteFrames, s.Layer);
}

public sealed class BattleWorldState
{
    public int SceneId { get; set; }
    public int MapId { get; set; }
    public int PaletteId { get; set; }
    public bool NightPalette { get; set; }
    public int MusicId { get; set; }
    public int BattleMusicId { get; set; }
    public BattleLeaderState Leader { get; }
    public BattleSearchProof SearchProof { get; }
    public BattleSceneEntryState[] SceneEntries { get; }
    public BattleEventObjectState[] EventObjects { get; }

    internal BattleWorldState(BattleWorldSnapshot source)
    {
        (SceneId, MapId, PaletteId, NightPalette, MusicId, BattleMusicId) =
            (source.SceneId, source.MapId, source.PaletteId, source.NightPalette, source.MusicId, source.BattleMusicId);
        Leader = new(source.Leader);
        SearchProof = source.SearchProof with { };
        SceneEntries = source.SceneEntries.Select(item => new BattleSceneEntryState(item)).ToArray();
        EventObjects = source.EventObjects.Select(item => new BattleEventObjectState(item)).ToArray();
    }
}

internal static class BattleModels
{
    private static int N(JsonElement value, string name) => value.GetProperty(name).GetInt32();
    private static string S(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static bool B(JsonElement value, string name) => value.GetProperty(name).GetBoolean();
    internal static IReadOnlyList<int> ReadOnlyInts(JsonElement value) => Array.AsReadOnly(value.EnumerateArray().Select(x => x.GetInt32()).ToArray());
    private static IReadOnlyList<string> Strings(JsonElement value) => Array.AsReadOnly(value.EnumerateArray().Select(x => x.GetString()!).ToArray());

    internal static BattleContextIdentity Identity(JsonElement e) => new(S(e, "snapshotKind"), N(e, "sceneId"), N(e, "mapId"), N(e, "ownerId"), N(e, "triggerAddress"));
    internal static BattleContextLayout Layout(JsonElement e) => new(N(e, "equipmentRoleCount"), N(e, "equipmentSlotCount"), N(e, "levelUpMagicRoleCount"));
    internal static BattleContextProvenance Provenance(JsonElement e) => new(B(e, "optionalGrinding"), B(e, "playerEquipmentChanges"),
        Array.AsReadOnly(e.GetProperty("fieldSources").EnumerateArray().Select(x => new BattleFieldSource(S(x, "path"), S(x, "sourceKind"), x.GetProperty("sourceAddress").ValueKind == JsonValueKind.Null ? null : N(x, "sourceAddress"), S(x, "appliesTo"))).ToArray()));
    private static BattleExperienceSnapshot Experience(JsonElement e) => new(N(e, "value"), N(e, "level"), N(e, "count"));
    internal static BattleRoleSnapshot Role(JsonElement e)
    {
        var hidden = new Dictionary<string, BattleExperienceSnapshot>(StringComparer.Ordinal);
        foreach (JsonProperty property in e.GetProperty("hiddenExperience").EnumerateObject())
            if (!property.Name.StartsWith('_')) hidden.Add(property.Name, Experience(property.Value));
        return new(N(e, "roleId"), N(e, "level"), N(e, "hp"), N(e, "maxHp"), N(e, "mp"), N(e, "maxMp"), Experience(e.GetProperty("primaryExperience")),
            new ReadOnlyDictionary<string, BattleExperienceSnapshot>(hidden), ReadOnlyInts(e.GetProperty("equipment")), ReadOnlyInts(e.GetProperty("magics")), ReadOnlyInts(e.GetProperty("statuses")), N(e, "coveredByRoleId"));
    }
    internal static BattleWorldSnapshot World(JsonElement e)
    {
        JsonElement leader = e.GetProperty("leader"), proof = e.GetProperty("searchProof");
        return new(N(e, "sceneId"), N(e, "mapId"), N(e, "paletteId"), B(e, "nightPalette"), N(e, "musicId"), N(e, "battleMusicId"),
            new(N(leader, "x"), N(leader, "y"), S(leader, "direction"), N(leader, "layer"), N(leader, "phase")),
            new(N(proof, "ownerId"), N(proof, "sampleIndex"), B(proof, "collisionPass")),
            Array.AsReadOnly(e.GetProperty("sceneEntries").EnumerateArray().Select(x => new BattleSceneEntrySnapshot(N(x, "sceneId"), N(x, "enterEntry"), N(x, "teleportEntry"))).ToArray()),
            Array.AsReadOnly(e.GetProperty("eventObjects").EnumerateArray().Select(x => new BattleEventObjectSnapshot(N(x, "ownerId"), N(x, "x"), N(x, "y"), N(x, "direction"), N(x, "state"), N(x, "vanishTime"), N(x, "triggerMode"), N(x, "triggerEntry"), N(x, "autoEntry"), N(x, "autoIdleFrameCount"), N(x, "spriteId"), N(x, "spriteFrames"), N(x, "currentFrame"), N(x, "scriptIdleFrameCount"), N(x, "spritePtrOffset"), N(x, "autoSpriteFrames"), N(x, "layer"))).ToArray()));
    }
    internal static BattleClosure Closure(JsonElement e) => new(S(e, "entry"), Array.AsReadOnly(e.GetProperty("entries").EnumerateArray().Select(x => new BattleClosureEntry(S(x, "id"), S(x, "category"), ClosureIdentity(x.GetProperty("identity")), ReadOnlyInts(x.GetProperty("ownerTickets")), S(x, "status"), Strings(x.GetProperty("resourceIds")))).ToArray()));
    private static BattleClosureIdentity ClosureIdentity(JsonElement e) => S(e, "kind") switch
    {
        "event-range" => new BattleEventRangeIdentity(N(e, "startAddress"), N(e, "battleAddress"), N(e, "wonStartAddress"), N(e, "endAddress")),
        "battle-command" => new BattleCommandIdentity(N(e, "commandAddress"), ReadOnlyInts(e.GetProperty("operands")), N(e, "enemyTeamId"),
            N(e, "enemyObjectId"), N(e, "enemyId"), N(e, "enemyCount"), N(e, "battlefieldId"), S(e, "specialValue"), S(e, "reverseEncoding")),
        "role-script-ranges" => new BattleRoleHooksIdentity(N(e, "dyingRoleId"), N(e, "dyingStartAddress"), N(e, "dyingEndAddress"),
            N(e, "friendDeathRoleId"), N(e, "friendDeathStartAddress"), N(e, "friendDeathEndAddress"), S(e, "branchCoverage")),
        "equipment-layout" => new BattleEquipmentLayoutIdentity(N(e, "roleCount"), N(e, "slotCount"), N(e, "dualAttackObjectId"), B(e, "persistentEquipmentStatus")),
        "growth-layout" => new BattleGrowthLayoutIdentity(N(e, "roleCount"), B(e, "includesPrimaryExperience"), B(e, "includesHiddenExperience"), B(e, "includesRecoveryAndCleanup")),
        "dynamic-auto-range" => new BattleDynamicAutoIdentity(N(e, "startAddress"), N(e, "endAddress"), Strings(e.GetProperty("effects")), S(e, "advance")),
        "failure-boundary" => new BattleFailureBoundaryIdentity(N(e, "lostEntryAddress"), N(e, "reloadCommand"), S(e, "behavior")),
        "battle-image-set" => new BattleImageSetIdentity(N(e, "enemyId"), S(e, "enemyArchiveEntry"), Strings(e.GetProperty("roleSpriteIds")),
            N(e, "battlefieldId"), S(e, "battlefieldArchiveEntry"), B(e, "includesLayoutAndEffectFrames")),
        "localized-dialogue-set" => new BattleLocalizedDialogueIdentity(Strings(e.GetProperty("languages")), Strings(e.GetProperty("contents"))),
        _ => throw new InvalidDataException("Closure identity kind is unsupported.")
    };
    internal static BattleResourceReference Resource(JsonElement e) => new(S(e, "id"), S(e, "kind"), S(e, "reference"), S(e, "sha256"), ReadOnlyInts(e.GetProperty("ownerTickets")));
}
