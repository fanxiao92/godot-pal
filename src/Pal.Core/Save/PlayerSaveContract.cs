using System.Collections.ObjectModel;
using Pal.Core.Shared;
using Pal.Core.State;
using Pal.Core.Battle;
using Pal.Core.Events;

namespace Pal.Core.Save;

/// <summary>Frozen M6-C v1 player-save contract identity and defensive limits (#221; parent #148).
/// A modern, versioned save snapshot over the A/B live owners: five manual slots (D3=A), strict
/// fail-closed construction-time validation, no clamping and no silent repair. Limits mirror the
/// fixed SDLPal reference (palcommon.h) and the live owners' single-source constants. This file
/// freezes data shapes and validation only — it owns no FileAccess, menu, capture or restore
/// wiring; see docs/milestones/m6/m6c-save-matrix.md for the per-domain retain/clear/rebuild matrix.</summary>
public static class PlayerSaveContract
{
    /// <summary>The only supported format version; missing or unknown versions reject, never
    /// migrate (a later migration needs its own approved contract).</summary>
    public const int CurrentVersion = 1;

    /// <summary>D3=A: five manual save slots numbered 1..5 (reference PAL_SaveSlotMenu rows).
    /// The slot number maps to an app-controlled user:// name; payload never chooses paths.</summary>
    public const int FirstSlot = SaveSlotBounds.FirstSlot, LastSlot = SaveSlotBounds.LastSlot, SaveSlotCount = SaveSlotBounds.SaveSlotCount;

    /// <summary>palcommon.h MAX_INVENTORY; single-sourced from the live owner.</summary>
    public const int InventoryCapacity = InventoryLiveState.Capacity;

    /// <summary>palcommon.h MAX_POISONS; single-sourced from the live owner. Poison columns are
    /// never stored in v1 — the reference clears them on load (global.c PAL_LoadGame_Common
    /// memset rgPoisonStatus) — but the cleared restore shape (16 id/script WORD pairs per party
    /// row) is frozen here for the restore ticket.</summary>
    public const int PoisonCapacity = PartyPoisonState.Capacity;

    /// <summary>palcommon.h MAX_PLAYER_ROLES; single-sourced from the equipment owner.</summary>
    public const int RoleCount = BattleEquipmentState.RoleCount;

    /// <summary>palcommon.h MAX_PLAYER_EQUIPMENTS; single-sourced from the equipment owner.</summary>
    public const int EquipmentSlotCount = BattleEquipmentState.SlotCount;

    /// <summary>global.h kStatusAll; single-sourced from the equipment owner. Status words are
    /// never stored in v1: the restore clears them (global.c PAL_InitGameData memset
    /// rgPlayerStatus) and replays the persistent equipment overlay (PAL_UpdateEquipments).</summary>
    public const int RoleStatusCount = BattleEquipmentState.StatusCount;

    /// <summary>palcommon.h MAX_LEVELS.</summary>
    public const int MaxLevel = 99;

    /// <summary>palcommon.h MAX_PLAYERS_IN_PARTY (script.c 0x0075 party slots; the poison owner
    /// accepts up to five rows, but the saved exploration party is three).</summary>
    public const int MaxPartyMembers = 3;

    /// <summary>ALLEXPERIENCE hidden tracks: health, magic, attack, magicPower, defense,
    /// dexterity, flee (the authored growth content carries all seven).</summary>
    public const int MaxHiddenExperienceTracks = 7;

    /// <summary>The only hidden track names a v1 snapshot may carry — exactly the seven
    /// ALLEXPERIENCE tracks every live-state validator (FirstMandatoryBattleProfile,
    /// ClassicBattleLifecycleSession, ModernBattleContentValidation) requires, Ordinal. A key
    /// outside this set can never correspond to a real capture or restore and rejects at
    /// construction.</summary>
    public static IReadOnlySet<string> HiddenExperienceTrackNames { get; } = new HashSet<string>(
        new[] { "health", "magic", "attack", "magicPower", "defense", "dexterity", "flee" }, StringComparer.Ordinal);

    /// <summary>palcommon.h MAX_PLAYER_MAGICS (rgwMagic rows per role).</summary>
    public const int MaxLearnedMagics = 32;

    /// <summary>palcommon.h MAX_EVENT_OBJECTS (per-scene event-object domain bound).</summary>
    public const int MaxEventObjects = 5500;

    /// <summary>palcommon.h MAX_SCENES (rgScene rows, incl. cross-scene recorded changes).</summary>
    public const int MaxSceneScriptEntries = 300;

    /// <summary>palcommon.h MAX_OBJECTS times the setObjectScript slot domain 0..5
    /// (TriggerEventCollection validates slotIndex 0..5).</summary>
    public const int MaxObjectScriptEntries = 600 * 6;

    /// <summary>Longest accepted gameplay content-identity reference string.</summary>
    public const int MaxContentIdentityChars = 256;

    /// <summary>Frozen pre-decode byte-length cap (#148 Decision 20). The bounded v1 worst case
    /// (six roles, 256 inventory rows, 5500 objects, 300 scene and 3600 object script slots as
    /// JSON numbers) stays under 1 MiB with headroom; longer files reject before any decode.</summary>
    public const int MaxFileBytes = 1024 * 1024;
}

/// <summary>One experience track WORD triple (reference EXPERIENCE: wExp/wLevel/wCount; the
/// reserved fourth word is not gameplay state).</summary>
public sealed class PlayerSaveExperience
{
    public int Value { get; }
    public int Level { get; }
    public int Count { get; }

    public PlayerSaveExperience(int value, int level, int count)
    {
        if (value is < 0 or > 65535 || level is < 0 or > 65535 || count is < 0 or > 65535)
            throw new InvalidDataException("$.experience: value/level/count must be WORDs (0..65535).");
        (Value, Level, Count) = (value, level, count);
    }

    public override bool Equals(object? obj) => obj is PlayerSaveExperience other
        && Value == other.Value && Level == other.Level && Count == other.Count;
    public override int GetHashCode() => HashCode.Combine(Value, Level, Count);
}

/// <summary>One role's saved record (RoleLiveState/BattleRoleSnapshot view). Statuses are
/// deliberately absent: the restore clears the nine words and replays the persistent equipment
/// overlay. Effective equipment panels are never stored — they are recomputed projections.</summary>
public sealed class PlayerSaveRole
{
    public int RoleId { get; }
    public int Level { get; }
    public int Hp { get; }
    public int MaxHp { get; }
    public int Mp { get; }
    public int MaxMp { get; }
    public PlayerSaveExperience PrimaryExperience { get; }
    public IReadOnlyDictionary<string, PlayerSaveExperience> HiddenExperience { get; }
    public IReadOnlyList<int> Equipment { get; }
    public IReadOnlyList<int> Magics { get; }
    public int CoveredByRoleId { get; }
    public int Attack { get; }
    public int MagicPower { get; }
    public int Defense { get; }
    public int Dexterity { get; }
    public int FleeRate { get; }

    public PlayerSaveRole(int roleId, int level, int hp, int maxHp, int mp, int maxMp,
        PlayerSaveExperience primaryExperience, IReadOnlyDictionary<string, PlayerSaveExperience> hiddenExperience,
        IReadOnlyList<int> equipment, IReadOnlyList<int> magics, int coveredByRoleId,
        int attack, int magicPower, int defense, int dexterity, int fleeRate)
    {
        if (roleId is < 0 or >= PlayerSaveContract.RoleCount)
            throw new InvalidDataException("$.roles.roleId: expected 0..5 (MAX_PLAYER_ROLES).");
        if (level is < 0 or > PlayerSaveContract.MaxLevel)
            throw new InvalidDataException("$.roles.level: expected 0..99 (palcommon.h MAX_LEVELS).");
        if (hp is < 0 or > 65535 || maxHp is < 0 or > 65535 || hp > maxHp)
            throw new InvalidDataException("$.roles.hp: expected WORD vitals with hp <= maxHp.");
        if (mp is < 0 or > 65535 || maxMp is < 0 or > 65535 || mp > maxMp)
            throw new InvalidDataException("$.roles.mp: expected WORD vitals with mp <= maxMp.");
        if (primaryExperience is null)
            throw new InvalidDataException("$.roles.primaryExperience: the primary track is required.");
        if (hiddenExperience is null || hiddenExperience.Count > PlayerSaveContract.MaxHiddenExperienceTracks
            || hiddenExperience.Keys.Any(key => string.IsNullOrEmpty(key))
            || hiddenExperience.Values.Any(value => value is null))
            throw new InvalidDataException($"$.roles.hiddenExperience: expected at most {PlayerSaveContract.MaxHiddenExperienceTracks} named, non-null tracks.");
        if (hiddenExperience.Keys.Any(key => !PlayerSaveContract.HiddenExperienceTrackNames.Contains(key)))
            throw new InvalidDataException("$.roles.hiddenExperience: track names must be the canonical ALLEXPERIENCE tracks (health/magic/attack/magicPower/defense/dexterity/flee).");
        if (equipment is null || equipment.Count != PlayerSaveContract.EquipmentSlotCount || equipment.Any(word => word is < 0 or > 65535))
            throw new InvalidDataException($"$.roles.equipment: expected exactly {PlayerSaveContract.EquipmentSlotCount} WORDs (0..65535).");
        if (magics is null || magics.Count > PlayerSaveContract.MaxLearnedMagics || magics.Any(word => word is < 0 or > 65535))
            throw new InvalidDataException($"$.roles.magics: expected at most {PlayerSaveContract.MaxLearnedMagics} WORDs (0..65535).");
        if (coveredByRoleId is < 0 or >= PlayerSaveContract.RoleCount)
            throw new InvalidDataException("$.roles.coveredByRoleId: expected 0..5.");
        if (attack is < 0 or > 65535 || magicPower is < 0 or > 65535 || defense is < 0 or > 65535
            || dexterity is < 0 or > 65535 || fleeRate is < 0 or > 65535)
            throw new InvalidDataException("$.roles.panel: permanent base panel words must be WORDs (0..65535).");
        RoleId = roleId;
        Level = level;
        Hp = hp;
        MaxHp = maxHp;
        Mp = mp;
        MaxMp = maxMp;
        PrimaryExperience = primaryExperience;
        HiddenExperience = new ReadOnlyDictionary<string, PlayerSaveExperience>(
            new Dictionary<string, PlayerSaveExperience>(hiddenExperience, StringComparer.Ordinal));
        Equipment = Array.AsReadOnly(equipment.ToArray());
        Magics = Array.AsReadOnly(magics.ToArray());
        CoveredByRoleId = coveredByRoleId;
        Attack = attack;
        MagicPower = magicPower;
        Defense = defense;
        Dexterity = dexterity;
        FleeRate = fleeRate;
    }
}

/// <summary>One occupied inventory row. A depleted row (amount 0 with the id retained) stays
/// occupied — it is saved exactly like a stocked row; empty rows are simply absent.</summary>
public sealed class PlayerSaveInventorySlot
{
    public int ItemId { get; }
    public int Amount { get; }
    public int AmountInUse { get; }

    public PlayerSaveInventorySlot(int itemId, int amount, int amountInUse)
    {
        if (itemId is < 1 or > 65535)
            throw new InvalidDataException("$.inventory.itemId: expected a nonzero WORD (1..65535).");
        if (amount is < 0 or > 99)
            throw new InvalidDataException("$.inventory.amount: expected 0..99 (reference amount cap); no clamping.");
        if (amountInUse is < 0 or > 65535)
            throw new InvalidDataException("$.inventory.amountInUse: expected a WORD (0..65535).");
        (ItemId, Amount, AmountInUse) = (itemId, amount, amountInUse);
    }

    public override bool Equals(object? obj) => obj is PlayerSaveInventorySlot other
        && ItemId == other.ItemId && Amount == other.Amount && AmountInUse == other.AmountInUse;
    public override int GetHashCode() => HashCode.Combine(ItemId, Amount, AmountInUse);
}

/// <summary>The party leader words the saved scene needs to continue exploration: position,
/// direction, layer and walk phase. The walking mid-step flag and the built plan are not stored —
/// capture happens at a stable boundary and the plan is rebuilt after restore.</summary>
public sealed class PlayerSaveLeader
{
    public short X { get; }
    public short Y { get; }
    public int Direction { get; }
    public int Layer { get; }
    public int StepPhase { get; }

    public PlayerSaveLeader(short x, short y, int direction, int layer, int stepPhase)
    {
        if (direction is < 0 or > 3)
            throw new InvalidDataException("$.world.leader.direction: expected 0..3.");
        if (stepPhase is < 0 or > 3)
            throw new InvalidDataException("$.world.leader.stepPhase: expected 0..3.");
        if (layer is < 0 or > 65535)
            throw new InvalidDataException("$.world.leader.layer: expected a WORD (0..65535).");
        (X, Y, Direction, Layer, StepPhase) = (x, y, direction, layer, stepPhase);
    }

    public override bool Equals(object? obj) => obj is PlayerSaveLeader other
        && X == other.X && Y == other.Y && Direction == other.Direction && Layer == other.Layer && StepPhase == other.StepPhase;
    public override int GetHashCode() => HashCode.Combine(X, Y, Direction, Layer, StepPhase);
}

/// <summary>One event object's dynamic words (DynamicEventObject / reference EVENTOBJECT):
/// position, facing, frame, state (negative = hidden by 0x0052), vanish countdown, trigger mode,
/// trigger/auto script entries, the shared auto idle counter and the owner's trigger idle counter.
/// Sprite identity and frame counts are static content, rebuilt from the prepared scene — never
/// stored here.</summary>
public sealed class PlayerSaveObject
{
    public int ObjectId { get; }
    public int X { get; }
    public int Y { get; }
    public int Direction { get; }
    public int CurrentFrame { get; }
    public int State { get; }
    public int VanishTime { get; }
    public int TriggerMode { get; }
    public int TriggerEntry { get; }
    public int AutoEntry { get; }
    public int AutoIdleCount { get; }
    public int TriggerIdleCount { get; }

    public PlayerSaveObject(int objectId, int x, int y, int direction, int currentFrame,
        int state, int vanishTime, int triggerMode, int triggerEntry, int autoEntry, int autoIdleCount,
        int triggerIdleCount)
    {
        if (objectId is < 1 or > 65535)
            throw new InvalidDataException("$.world.objects.objectId: expected a one-based WORD (1..65535).");
        if (x is < 0 or > 65535 || y is < 0 or > 65535 || direction is < 0 or > 65535 || currentFrame is < 0 or > 65535)
            throw new InvalidDataException("$.world.objects: x/y/direction/currentFrame must be WORDs (0..65535).");
        if (state is < -32768 or > 32767)
            throw new InvalidDataException("$.world.objects.state: expected a signed SHORT.");
        if (vanishTime is < -32768 or > 32767)
            throw new InvalidDataException("$.world.objects.vanishTime: expected a signed SHORT.");
        if (triggerMode is < 0 or > 65535 || triggerEntry is < 0 or > 65535 || autoEntry is < 0 or > 65535)
            throw new InvalidDataException("$.world.objects: triggerMode/triggerEntry/autoEntry must be WORDs (0..65535).");
        if (autoIdleCount is < 0 or > 65535)
            throw new InvalidDataException("$.world.objects.autoIdleCount: expected a WORD (0..65535).");
        if (triggerIdleCount is < 0 or > 65535)
            throw new InvalidDataException("$.world.objects.triggerIdleCount: expected a WORD (0..65535).");
        ObjectId = objectId;
        X = x;
        Y = y;
        Direction = direction;
        CurrentFrame = currentFrame;
        State = state;
        VanishTime = vanishTime;
        TriggerMode = triggerMode;
        TriggerEntry = triggerEntry;
        AutoEntry = autoEntry;
        AutoIdleCount = autoIdleCount;
        TriggerIdleCount = triggerIdleCount;
    }

    public override bool Equals(object? obj) => obj is PlayerSaveObject other
        && ObjectId == other.ObjectId && X == other.X && Y == other.Y && Direction == other.Direction
        && CurrentFrame == other.CurrentFrame && State == other.State && VanishTime == other.VanishTime
        && TriggerMode == other.TriggerMode && TriggerEntry == other.TriggerEntry && AutoEntry == other.AutoEntry
        && AutoIdleCount == other.AutoIdleCount && TriggerIdleCount == other.TriggerIdleCount;
    public override int GetHashCode() => HashCode.Combine(HashCode.Combine(ObjectId, X, Y, Direction, CurrentFrame, State),
        HashCode.Combine(VanishTime, TriggerMode, TriggerEntry, AutoEntry, AutoIdleCount, TriggerIdleCount));
}

/// <summary>One scene's retained enter/teleport script slots (reference SCENE wScriptOnEnter/
/// wScriptOnTeleport; script.c 0x006D writes). Scenes other than the current one keep their
/// recorded changes — saving never truncates to the visible nodes.</summary>
public sealed class PlayerSaveSceneScript
{
    public int SceneId { get; }
    public int OnEnterEntry { get; }
    public int OnTeleportEntry { get; }

    public PlayerSaveSceneScript(int sceneId, int onEnterEntry, int onTeleportEntry)
    {
        if (sceneId is < 0 or > 65535)
            throw new InvalidDataException("$.world.sceneScripts.sceneId: expected a WORD (0..65535).");
        if (onEnterEntry is < 0 or > 65535 || onTeleportEntry is < 0 or > 65535)
            throw new InvalidDataException("$.world.sceneScripts: onEnter/onTeleport must be WORDs (0..65535).");
        (SceneId, OnEnterEntry, OnTeleportEntry) = (sceneId, onEnterEntry, onTeleportEntry);
    }

    public override bool Equals(object? obj) => obj is PlayerSaveSceneScript other
        && SceneId == other.SceneId && OnEnterEntry == other.OnEnterEntry && OnTeleportEntry == other.OnTeleportEntry;
    public override int GetHashCode() => HashCode.Combine(SceneId, OnEnterEntry, OnTeleportEntry);
}

/// <summary>One retained object script-slot write (script.c 0x0090 rgwData[2+slot], plus the
/// player hook seeds stored in the same dictionary). Slot indices 0..5 match the event VM's
/// validated domain; cross-scene writes are retained data and never executed by loading.</summary>
public sealed class PlayerSaveObjectScript
{
    public int ObjectId { get; }
    public int SlotIndex { get; }
    public int Entry { get; }

    public PlayerSaveObjectScript(int objectId, int slotIndex, int entry)
    {
        if (objectId is < 1 or > 65535)
            throw new InvalidDataException("$.world.objectScripts.objectId: expected a one-based WORD (1..65535).");
        if (slotIndex is < 0 or > 5)
            throw new InvalidDataException("$.world.objectScripts.slotIndex: expected 0..5 (setObjectScript domain).");
        if (entry is < 0 or > 65535)
            throw new InvalidDataException("$.world.objectScripts.entry: expected a WORD (0..65535).");
        (ObjectId, SlotIndex, Entry) = (objectId, slotIndex, entry);
    }

    public override bool Equals(object? obj) => obj is PlayerSaveObjectScript other
        && ObjectId == other.ObjectId && SlotIndex == other.SlotIndex && Entry == other.Entry;
    public override int GetHashCode() => HashCode.Combine(ObjectId, SlotIndex, Entry);
}

/// <summary>The last setMusic control words (reference wNumMusic); null before the first one.</summary>
public sealed class PlayerSaveMusic
{
    public int MusicId { get; }
    public int Mode { get; }

    public PlayerSaveMusic(int musicId, int mode)
    {
        if (musicId is < 0 or > 65535 || mode is < 0 or > 65535)
            throw new InvalidDataException("$.world.music: musicId/mode must be WORDs (0..65535).");
        (MusicId, Mode) = (musicId, mode);
    }

    public override bool Equals(object? obj) => obj is PlayerSaveMusic other
        && MusicId == other.MusicId && Mode == other.Mode;
    public override int GetHashCode() => HashCode.Combine(MusicId, Mode);
}

/// <summary>The saved world domain: current scene identity, the leader words, every event
/// object's dynamic words, retained scene/object script slots, and the battlefield control word.</summary>
public sealed class PlayerSaveWorld
{
    public int SceneId { get; }
    public PlayerSaveLeader Leader { get; }
    public IReadOnlyList<PlayerSaveObject> Objects { get; }
    public IReadOnlyList<PlayerSaveSceneScript> SceneScripts { get; }
    public IReadOnlyList<PlayerSaveObjectScript> ObjectScripts { get; }
    public int Battlefield { get; }
    public PlayerSaveMusic? Music { get; }

    public PlayerSaveWorld(int sceneId, PlayerSaveLeader leader, IReadOnlyList<PlayerSaveObject> objects,
        IReadOnlyList<PlayerSaveSceneScript> sceneScripts, IReadOnlyList<PlayerSaveObjectScript> objectScripts,
        int battlefield, PlayerSaveMusic? music)
    {
        if (sceneId is < 0 or > 65535)
            throw new InvalidDataException("$.world.sceneId: expected a WORD (0..65535).");
        if (leader is null)
            throw new InvalidDataException("$.world.leader: the leader words are required.");
        if (objects is null || objects.Count > PlayerSaveContract.MaxEventObjects || objects.Any(obj => obj is null))
            throw new InvalidDataException($"$.world.objects: expected at most {PlayerSaveContract.MaxEventObjects} non-null objects.");
        if (objects.Select(obj => obj.ObjectId).Distinct().Count() != objects.Count)
            throw new InvalidDataException("$.world.objects: object ids must be unique.");
        if (sceneScripts is null || sceneScripts.Count > PlayerSaveContract.MaxSceneScriptEntries || sceneScripts.Any(entry => entry is null))
            throw new InvalidDataException($"$.world.sceneScripts: expected at most {PlayerSaveContract.MaxSceneScriptEntries} non-null entries.");
        if (sceneScripts.Select(entry => entry.SceneId).Distinct().Count() != sceneScripts.Count)
            throw new InvalidDataException("$.world.sceneScripts: scene ids must be unique.");
        if (objectScripts is null || objectScripts.Count > PlayerSaveContract.MaxObjectScriptEntries || objectScripts.Any(entry => entry is null))
            throw new InvalidDataException($"$.world.objectScripts: expected at most {PlayerSaveContract.MaxObjectScriptEntries} non-null entries.");
        if (objectScripts.Select(entry => (entry.ObjectId, entry.SlotIndex)).Distinct().Count() != objectScripts.Count)
            throw new InvalidDataException("$.world.objectScripts: (objectId, slotIndex) keys must be unique.");
        if (battlefield is < 0 or > 65535)
            throw new InvalidDataException("$.world.battlefield: expected a WORD (0..65535).");
        SceneId = sceneId;
        Leader = leader;
        Objects = Array.AsReadOnly(objects.ToArray());
        SceneScripts = Array.AsReadOnly(sceneScripts.ToArray());
        ObjectScripts = Array.AsReadOnly(objectScripts.ToArray());
        Battlefield = battlefield;
        Music = music;
    }
}

/// <summary>The frozen v1 player-save snapshot root. Fail-closed: every domain violation rejects
/// at construction with a field-locatable diagnostic — no clamping, no silent repair, no default
/// versioning. This is an immutable data view, not a second live-state engine.</summary>
public sealed class PlayerSaveV1
{
    public int FormatVersion { get; }
    public int SaveSlot { get; }
    public int SavedTimes { get; }
    public string ContentIdentity { get; }
    public IReadOnlyList<PlayerSaveRole> Roles { get; }
    public IReadOnlyList<int> PartyRoleIds { get; }
    public IReadOnlyList<PlayerSaveInventorySlot> Inventory { get; }
    public uint Cash { get; }
    public PlayerSaveWorld World { get; }

    public PlayerSaveV1(int formatVersion, int saveSlot, int savedTimes, string contentIdentity,
        IReadOnlyList<PlayerSaveRole> roles, IReadOnlyList<int> partyRoleIds,
        IReadOnlyList<PlayerSaveInventorySlot> inventory, uint cash, PlayerSaveWorld world)
    {
        if (formatVersion != PlayerSaveContract.CurrentVersion)
            throw new InvalidDataException($"$.formatVersion: expected exactly {PlayerSaveContract.CurrentVersion}; unknown versions reject without migration.");
        if (saveSlot is < PlayerSaveContract.FirstSlot or > PlayerSaveContract.LastSlot)
            throw new InvalidDataException($"$.saveSlot: expected {PlayerSaveContract.FirstSlot}..{PlayerSaveContract.LastSlot} (five manual slots).");
        if (savedTimes is < 0 or > 65535)
            throw new InvalidDataException("$.savedTimes: expected a WORD (0..65535).");
        if (string.IsNullOrWhiteSpace(contentIdentity) || contentIdentity.Length > PlayerSaveContract.MaxContentIdentityChars)
            throw new InvalidDataException($"$.contentIdentity: expected 1..{PlayerSaveContract.MaxContentIdentityChars} non-whitespace characters.");
        if (roles is null || roles.Count != PlayerSaveContract.RoleCount || roles.Any(role => role is null))
            throw new InvalidDataException($"$.roles: expected exactly {PlayerSaveContract.RoleCount} non-null role records.");
        if (roles.Select(role => role!.RoleId).Distinct().Count() != roles.Count)
            throw new InvalidDataException("$.roles: role ids must be unique.");
        if (partyRoleIds is null || partyRoleIds.Count is < 1 or > PlayerSaveContract.MaxPartyMembers
            || partyRoleIds.Any(roleId => roleId is < 0 or >= PlayerSaveContract.RoleCount)
            || partyRoleIds.Distinct().Count() != partyRoleIds.Count)
            throw new InvalidDataException($"$.partyRoleIds: expected 1..{PlayerSaveContract.MaxPartyMembers} distinct role ids 0..5.");
        if (inventory is null || inventory.Count > PlayerSaveContract.InventoryCapacity || inventory.Any(slot => slot is null))
            throw new InvalidDataException($"$.inventory: expected at most {PlayerSaveContract.InventoryCapacity} non-null occupied rows.");
        if (inventory.Select(slot => slot!.ItemId).Distinct().Count() != inventory.Count)
            throw new InvalidDataException("$.inventory: item ids must be unique.");
        if (world is null)
            throw new InvalidDataException("$.world: the world snapshot is required.");
        FormatVersion = formatVersion;
        SaveSlot = saveSlot;
        SavedTimes = savedTimes;
        ContentIdentity = contentIdentity;
        Roles = Array.AsReadOnly(roles.ToArray());
        PartyRoleIds = Array.AsReadOnly(partyRoleIds.ToArray());
        Inventory = Array.AsReadOnly(inventory.ToArray());
        Cash = cash;
        World = world;
    }
}
