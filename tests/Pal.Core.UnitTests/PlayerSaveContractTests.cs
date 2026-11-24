using Xunit;

namespace Pal.Core.Tests;

/// <summary>M6-C leaf #221: the frozen v1 player-save snapshot contract. Oracles are the fixed
/// SDLPal reference save/load domains (global.c PAL_SaveGame_Common/PAL_LoadGame_Common/
/// PAL_InitGameData/PAL_UpdateEquipments, palcommon.h limits) and independent hand-written
/// literal data — never values produced by the contract itself.</summary>
public sealed class PlayerSaveContractTests
{
    [Fact]
    public void Contract_identity_freezes_v1_version_five_slots_and_reference_limits()
    {
        Assert.True(PlayerSaveContract.CurrentVersion == 1,
            "v1 contract identity must freeze CurrentVersion = 1 (got " + PlayerSaveContract.CurrentVersion + ")");
        Assert.True(PlayerSaveContract.FirstSlot == 1 && PlayerSaveContract.LastSlot == 5
            && PlayerSaveContract.SaveSlotCount == 5,
            "v1 contract identity must freeze five manual slots 1..5 (D3=A, PAL_SaveSlotMenu rows)");
        Assert.True(PlayerSaveContract.InventoryCapacity == 256,
            "v1 contract identity must freeze the inventory capacity 256 (palcommon.h MAX_INVENTORY)");
        Assert.True(PlayerSaveContract.PoisonCapacity == 16,
            "v1 contract identity must freeze the poison capacity 16 (palcommon.h MAX_POISONS)");
        Assert.True(PlayerSaveContract.RoleCount == 6 && PlayerSaveContract.EquipmentSlotCount == 6
            && PlayerSaveContract.RoleStatusCount == 9,
            "v1 contract identity must freeze six roles, six equipment slots and nine status words");
        Assert.True(PlayerSaveContract.MaxLevel == 99 && PlayerSaveContract.MaxPartyMembers == 3,
            "v1 contract identity must freeze the level cap 99 and three party slots");
        Assert.True(PlayerSaveContract.MaxHiddenExperienceTracks == 7 && PlayerSaveContract.MaxLearnedMagics == 32,
            "v1 contract identity must freeze seven hidden experience tracks and 32 magic slots");
        Assert.True(PlayerSaveContract.HiddenExperienceTrackNames.SetEquals(
            new[] { "health", "magic", "attack", "magicPower", "defense", "dexterity", "flee" }),
            "v1 contract identity must freeze the canonical hidden track name domain (Ordinal)");
        Assert.True(PlayerSaveContract.MaxEventObjects == 5500 && PlayerSaveContract.MaxSceneScriptEntries == 300
            && PlayerSaveContract.MaxObjectScriptEntries == 3600,
            "v1 contract identity must freeze the reference-bounded world collections");
        Assert.True(PlayerSaveContract.MaxFileBytes == 1024 * 1024,
            "v1 contract identity must freeze the 1 MiB pre-decode file cap");
    }

    [Fact]
    public void Frozen_limits_come_from_the_live_owners_single_source()
    {
        Assert.Equal(InventoryLiveState.Capacity, PlayerSaveContract.InventoryCapacity);
        Assert.Equal(PartyPoisonState.Capacity, PlayerSaveContract.PoisonCapacity);
        Assert.Equal(BattleEquipmentState.RoleCount, PlayerSaveContract.RoleCount);
        Assert.Equal(BattleEquipmentState.SlotCount, PlayerSaveContract.EquipmentSlotCount);
        Assert.Equal(BattleEquipmentState.StatusCount, PlayerSaveContract.RoleStatusCount);
    }

    [Fact]
    public void Literal_fixture_round_trips_every_required_field()
    {
        var roles = new List<PlayerSaveRole>(Roles());
        var party = new List<int> { 2, 0, 5 };
        var inventory = new List<PlayerSaveInventorySlot> { new(7, 3, 2), new(65535, 0, 65535) };
        var objects = new List<PlayerSaveObject>
        {
            new(60, 1450, 898, 2, 1, 1, 0, 1, 392, 0, 5, 11),
            new(3, 1920, 1000, 3, 0, -2, 800, 0, 0, 77, 0, 0),
        };
        var sceneScripts = new List<PlayerSaveSceneScript> { new(12, 4, 9), new(13, 40, 0) };
        var objectScripts = new List<PlayerSaveObjectScript> { new(1, 0, 328), new(60, 1, 0) };
        var save = new PlayerSaveV1(1, 3, 65535, "modern-context/first-battle@sha256:9f2c",
            roles, party, inventory, 123456789,
            new PlayerSaveWorld(12, new PlayerSaveLeader(-3192, 1032, 3, 1, 2),
                objects, sceneScripts, objectScripts, 7, new PlayerSaveMusic(3, 1)));

        // Header: version, slot, saved times and content identity are all required fields.
        Assert.Equal(1, save.FormatVersion);
        Assert.Equal(3, save.SaveSlot);
        Assert.Equal(65535, save.SavedTimes);
        Assert.Equal("modern-context/first-battle@sha256:9f2c", save.ContentIdentity);

        // Six complete role records; identity is the RoleId, never the party position.
        Assert.Equal(6, save.Roles.Count);
        var first = save.Roles[0];
        Assert.Equal(0, first.RoleId);
        Assert.Equal(1, first.Level);
        Assert.Equal(90, first.Hp);
        Assert.Equal(90, first.MaxHp);
        Assert.Equal(12, first.Mp);
        Assert.Equal(30, first.MaxMp);
        Assert.Equal(new PlayerSaveExperience(0, 1, 0), first.PrimaryExperience);
        var worn = save.Roles[5];
        Assert.Equal(5, worn.RoleId);
        Assert.Equal(99, worn.Level);
        Assert.Equal(1, worn.Hp);
        Assert.Equal(65535, worn.MaxHp);
        Assert.Equal(0, worn.Mp);
        Assert.Equal(65535, worn.MaxMp);
        Assert.Equal(new[] { 1, 0, 0, 0, 0, 0 }, worn.Equipment);
        Assert.Equal(new[] { 65, 0 }, worn.Magics);
        Assert.Equal(4, worn.CoveredByRoleId);
        Assert.Equal((210, 205, 200, 195, 190), (worn.Attack, worn.MagicPower, worn.Defense, worn.Dexterity, worn.FleeRate));

        // Permanent base panel words and both experience tracks survive with hand-written values.
        Assert.Equal(new PlayerSaveExperience(17, 3, 4), first.HiddenExperience["health"]);
        Assert.Equal(new PlayerSaveExperience(87, 3, 1), first.HiddenExperience["attack"]);
        Assert.Equal(7, first.HiddenExperience.Count);
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 0 }, first.Equipment);
        Assert.Empty(first.Magics);
        Assert.Equal(0, first.CoveredByRoleId);
        Assert.Equal((12, 11, 10, 9, 8), (first.Attack, first.MagicPower, first.Defense, first.Dexterity, first.FleeRate));

        // Party identity/order, occupied inventory rows (a depleted row stays occupied) and DWORD cash.
        Assert.Equal(new[] { 2, 0, 5 }, save.PartyRoleIds);
        Assert.Equal(new PlayerSaveInventorySlot(7, 3, 2), save.Inventory[0]);
        Assert.Equal(new PlayerSaveInventorySlot(65535, 0, 65535), save.Inventory[1]);
        Assert.Equal(123456789u, save.Cash);

        // World domain: scene identity, leader words, event-object dynamic words, script slots.
        Assert.Equal(12, save.World.SceneId);
        Assert.Equal(new PlayerSaveLeader(-3192, 1032, 3, 1, 2), save.World.Leader);
        Assert.Equal(new PlayerSaveObject(60, 1450, 898, 2, 1, 1, 0, 1, 392, 0, 5, 11), save.World.Objects[0]);
        Assert.NotEqual(new PlayerSaveObject(60, 1450, 898, 2, 1, 1, 0, 1, 392, 0, 5, 12), save.World.Objects[0]);
        Assert.Equal(new PlayerSaveObject(3, 1920, 1000, 3, 0, -2, 800, 0, 0, 77, 0, 0), save.World.Objects[1]);
        Assert.Equal(65535, new PlayerSaveObject(3, 1920, 1000, 3, 0, -2, 800, 0, 0, 77, 0, 65535).TriggerIdleCount);
        Assert.Equal(new PlayerSaveSceneScript(12, 4, 9), save.World.SceneScripts[0]);
        Assert.Equal(new PlayerSaveSceneScript(13, 40, 0), save.World.SceneScripts[1]);
        Assert.Equal(new PlayerSaveObjectScript(1, 0, 328), save.World.ObjectScripts[0]);
        Assert.Equal(new PlayerSaveObjectScript(60, 1, 0), save.World.ObjectScripts[1]);
        Assert.Equal(7, save.World.Battlefield);
        Assert.Equal(new PlayerSaveMusic(3, 1), save.World.Music);

        // The snapshot owns defensive copies: later source mutations never reach the frozen view.
        party.Add(1);
        inventory.Add(new PlayerSaveInventorySlot(9, 9, 9));
        objects.Add(new PlayerSaveObject(99, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        sceneScripts.Add(new PlayerSaveSceneScript(99, 0, 0));
        objectScripts.Add(new PlayerSaveObjectScript(99, 0, 0));
        ((List<PlayerSaveRole>)roles)[0] = Role(roleId: 4);
        Assert.Equal(3, save.PartyRoleIds.Count);
        Assert.Equal(2, save.Inventory.Count);
        Assert.Equal(2, save.World.Objects.Count);
        Assert.Equal(2, save.World.SceneScripts.Count);
        Assert.Equal(2, save.World.ObjectScripts.Count);
        Assert.Equal(0, save.Roles[0].RoleId);
        Assert.Throws<NotSupportedException>(() => ((IList<PlayerSaveRole>)save.Roles)[0] = null!);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)save.PartyRoleIds)[0] = 1);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, PlayerSaveExperience>)save.Roles[5].HiddenExperience)["flee"] = null!);
    }

    [Fact]
    public void Rejects_header_violations_fail_closed()
    {
        Assert.Throws<InvalidDataException>(() => Save(formatVersion: 0));
        Assert.Throws<InvalidDataException>(() => Save(formatVersion: 2));
        Assert.Throws<InvalidDataException>(() => Save(saveSlot: 0));
        Assert.Throws<InvalidDataException>(() => Save(saveSlot: 6));
        Assert.Throws<InvalidDataException>(() => Save(savedTimes: -1));
        Assert.Throws<InvalidDataException>(() => Save(savedTimes: 65536));
        Assert.Throws<InvalidDataException>(() => Save(contentIdentity: ""));
        Assert.Throws<InvalidDataException>(() => Save(contentIdentity: "  "));
        Assert.Throws<InvalidDataException>(() => Save(contentIdentity: new string('x', 257)));
        Assert.Throws<InvalidDataException>(() => Save(contentIdentity: null!));
    }

    [Fact]
    public void Rejects_role_domain_violations_fail_closed()
    {
        Assert.Throws<InvalidDataException>(() => Save(role: Role(roleId: 6)));
        Assert.Throws<InvalidDataException>(() => Save(roles: Roles(roleCount: 5)));
        Assert.Throws<InvalidDataException>(() => Save(roles: Roles(roleCount: 7)));
        Assert.Throws<InvalidDataException>(() => Save(roles: Roles(duplicateRoleId: true)));
        Assert.Throws<InvalidDataException>(() => Save(role: Role(level: 100)));
        Assert.Throws<InvalidDataException>(() => Save(role: Role(hp: 91)));
        Assert.Throws<InvalidDataException>(() => Save(role: Role(mp: 31)));
        Assert.Throws<InvalidDataException>(() => Save(role: Role(hp: -1)));
        Assert.Throws<InvalidDataException>(() => Save(role: Role(maxHp: 65536)));
        Assert.Throws<InvalidDataException>(() => Save(role: Role(equipment: new[] { 1, 0, 0, 0, 0 })));
        Assert.Throws<InvalidDataException>(() => Save(role: Role(equipment: new[] { 1, 0, 0, 0, 0, 65536 })));
        Assert.Throws<InvalidDataException>(() => Save(role: Role(magics: Enumerable.Repeat(65, 33).ToArray())));
        Assert.Throws<InvalidDataException>(() => Save(role: Role(magics: new[] { -1 })));
        Assert.Throws<InvalidDataException>(() => Save(role: Role(hiddenExtraTrack: true)));
        Assert.Throws<InvalidDataException>(() => Save(role: Role(hidden: new Dictionary<string, PlayerSaveExperience> { [""] = new(1, 1, 1) })));
        Assert.Throws<InvalidDataException>(() => Save(role: Role(hidden: new Dictionary<string, PlayerSaveExperience?> { ["health"] = null }!)));
        // Track names outside the canonical ALLEXPERIENCE domain reject even when the count stays <= 7.
        Assert.Throws<InvalidDataException>(() => Save(role: Role(hidden: new Dictionary<string, PlayerSaveExperience> { ["healt"] = new(1, 1, 1) })));
        Assert.Throws<InvalidDataException>(() => Save(role: Role(hidden: new Dictionary<string, PlayerSaveExperience>
        {
            ["health"] = new(17, 3, 4), ["magic"] = new(0, 3, 0), ["attack"] = new(87, 3, 1),
            ["magicPower"] = new(5, 2, 9), ["defense"] = new(0, 3, 0), ["dexterity"] = new(1, 1, 1),
            ["escape"] = new(0, 3, 0),
        })));
        Assert.Throws<InvalidDataException>(() => Save(role: Role(coveredByRoleId: 6)));
        Assert.Throws<InvalidDataException>(() => Save(role: Role(attack: -1)));
        Assert.Throws<InvalidDataException>(() => Save(role: Role(fleeRate: 65536)));
        Assert.Throws<InvalidDataException>(() => Save(role: new PlayerSaveRole(0, 1, 90, 90, 12, 30,
            null!, Hidden(null, false), new[] { 0, 0, 0, 0, 0, 0 }, Array.Empty<int>(), 0, 12, 11, 10, 9, 8)));
    }

    [Fact]
    public void Rejects_inventory_violations_fail_closed_without_clamping()
    {
        Assert.Throws<InvalidDataException>(() => Save(inventory: new[] { new PlayerSaveInventorySlot(0, 1, 0) }));
        Assert.Throws<InvalidDataException>(() => Save(inventory: new[] { new PlayerSaveInventorySlot(65536, 1, 0) }));
        // The reference amount cap is 99; 100 rejects instead of clamping (fail-closed).
        Assert.Throws<InvalidDataException>(() => Save(inventory: new[] { new PlayerSaveInventorySlot(7, 100, 0) }));
        Assert.Throws<InvalidDataException>(() => Save(inventory: new[] { new PlayerSaveInventorySlot(7, -1, 0) }));
        Assert.Throws<InvalidDataException>(() => Save(inventory: new[] { new PlayerSaveInventorySlot(7, 1, 65536) }));
        Assert.Throws<InvalidDataException>(() => Save(inventory: new[]
        {
            new PlayerSaveInventorySlot(7, 1, 0), new PlayerSaveInventorySlot(7, 2, 0),
        }));
        Assert.Throws<InvalidDataException>(() => Save(inventory:
            Enumerable.Range(1, 257).Select(id => new PlayerSaveInventorySlot(id, 1, 0)).ToArray()));
        // 256 distinct occupied rows stay legal, including depleted and fully reserved ones.
        var full = Enumerable.Range(1, 256).Select(id => new PlayerSaveInventorySlot(id, 0, id)).ToArray();
        Assert.Equal(256, Save(inventory: full).Inventory.Count);
    }

    [Fact]
    public void Rejects_party_violations_fail_closed()
    {
        Assert.Throws<InvalidDataException>(() => Save(party: Array.Empty<int>()));
        Assert.Throws<InvalidDataException>(() => Save(party: new[] { 2, 0, 5, 1 }));
        Assert.Throws<InvalidDataException>(() => Save(party: new[] { 2, 2, 5 }));
        Assert.Throws<InvalidDataException>(() => Save(party: new[] { 6 }));
        Assert.Throws<InvalidDataException>(() => Save(party: new[] { -1 }));
    }

    [Fact]
    public void Rejects_world_violations_fail_closed()
    {
        Assert.Throws<InvalidDataException>(() => Save(leader: new PlayerSaveLeader(-3192, 1032, 4, 1, 2)));
        Assert.Throws<InvalidDataException>(() => Save(leader: new PlayerSaveLeader(-3192, 1032, -1, 1, 2)));
        Assert.Throws<InvalidDataException>(() => Save(leader: new PlayerSaveLeader(-3192, 1032, 3, 1, 4)));
        Assert.Throws<InvalidDataException>(() => Save(leader: new PlayerSaveLeader(-3192, 1032, 3, 65536, 2)));
        Assert.Throws<InvalidDataException>(() => Save(sceneId: 65536));
        Assert.Throws<InvalidDataException>(() => Save(objects: new[] { new PlayerSaveObject(60, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0) }.Concat(Objects()).ToArray()));
        Assert.Throws<InvalidDataException>(() => Save(objects: new[] { new PlayerSaveObject(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0) }));
        Assert.Throws<InvalidDataException>(() => Save(objects: new[] { new PlayerSaveObject(60, 0, 0, 0, 0, -32769, 0, 0, 0, 0, 0, 0) }));
        Assert.Throws<InvalidDataException>(() => Save(objects: new[] { new PlayerSaveObject(60, 0, 0, 0, 0, 0, 32768, 0, 0, 0, 0, 0) }));
        Assert.Throws<InvalidDataException>(() => Save(objects: new[] { new PlayerSaveObject(60, 0, 0, 0, 0, 0, 0, 0, 65536, 0, 0, 0) }));
        Assert.Throws<InvalidDataException>(() => Save(objects: new[] { new PlayerSaveObject(60, 0, 0, 0, 0, 0, 0, 0, 0, 0, -1, 0) }));
        Assert.Throws<InvalidDataException>(() => Save(objects: new[] { new PlayerSaveObject(60, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -1) }));
        Assert.Throws<InvalidDataException>(() => Save(objects: new[] { new PlayerSaveObject(60, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 65536) }));
        Assert.Throws<InvalidDataException>(() => Save(objects:
            Enumerable.Range(1, 5501).Select(id => new PlayerSaveObject(id, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)).ToArray()));
        Assert.Throws<InvalidDataException>(() => Save(sceneScripts: new[] { new PlayerSaveSceneScript(12, 4, 9), new PlayerSaveSceneScript(12, 5, 5) }));
        Assert.Throws<InvalidDataException>(() => Save(sceneScripts:
            Enumerable.Range(1, 301).Select(id => new PlayerSaveSceneScript(id, 0, 0)).ToArray()));
        Assert.Throws<InvalidDataException>(() => Save(sceneScripts: new[] { new PlayerSaveSceneScript(12, 65536, 0) }));
        Assert.Throws<InvalidDataException>(() => Save(objectScripts: new[] { new PlayerSaveObjectScript(60, 6, 0) }));
        Assert.Throws<InvalidDataException>(() => Save(objectScripts: new[] { new PlayerSaveObjectScript(0, 0, 0) }));
        Assert.Throws<InvalidDataException>(() => Save(objectScripts: new[] { new PlayerSaveObjectScript(60, 0, 0), new PlayerSaveObjectScript(60, 0, 1) }));
        Assert.Throws<InvalidDataException>(() => Save(objectScripts:
            Enumerable.Range(0, 3601).Select(i => new PlayerSaveObjectScript(1 + i / 6, i % 6, 0)).ToArray()));
        Assert.Throws<InvalidDataException>(() => Save(battlefield: 65536));
        Assert.Throws<InvalidDataException>(() => Save(music: new PlayerSaveMusic(3, 65536)));
        Assert.Throws<InvalidDataException>(() => Save(music: new PlayerSaveMusic(65536, 1)));
        Assert.Throws<InvalidDataException>(() => new PlayerSaveV1(1, 3, 65535, "modern-context/first-battle@sha256:9f2c",
            Roles(), new[] { 2, 0, 5 }, new[] { new PlayerSaveInventorySlot(7, 3, 2) }, 123456789, null!));
        // Null music stays legal: an independent context may never have run setMusic.
        Assert.Null(Save(music: null).World.Music);
    }

    [Fact]
    public void Reference_diagnostic_paths_name_the_violating_field()
    {
        Assert.StartsWith("$.formatVersion", Assert.Throws<InvalidDataException>(() => Save(formatVersion: 2)).Message);
        Assert.StartsWith("$.saveSlot", Assert.Throws<InvalidDataException>(() => Save(saveSlot: 6)).Message);
        Assert.StartsWith("$.contentIdentity", Assert.Throws<InvalidDataException>(() => Save(contentIdentity: "")).Message);
        Assert.StartsWith("$.roles.roleId", Assert.Throws<InvalidDataException>(() => Save(role: Role(roleId: 6))).Message);
        Assert.StartsWith("$.roles.level", Assert.Throws<InvalidDataException>(() => Save(role: Role(level: 100))).Message);
        Assert.StartsWith("$.roles.hiddenExperience", Assert.Throws<InvalidDataException>(
            () => Save(role: Role(hidden: new Dictionary<string, PlayerSaveExperience> { ["healt"] = new(1, 1, 1) }))).Message);
        Assert.StartsWith("$.inventory.itemId", Assert.Throws<InvalidDataException>(() => Save(inventory: new[] { new PlayerSaveInventorySlot(0, 1, 0) })).Message);
        Assert.StartsWith("$.inventory.amount", Assert.Throws<InvalidDataException>(() => Save(inventory: new[] { new PlayerSaveInventorySlot(7, 100, 0) })).Message);
        Assert.StartsWith("$.partyRoleIds", Assert.Throws<InvalidDataException>(() => Save(party: new[] { 2, 2, 5 })).Message);
        Assert.StartsWith("$.world.objects.state", Assert.Throws<InvalidDataException>(() => Save(objects: new[] { new PlayerSaveObject(60, 0, 0, 0, 0, -32769, 0, 0, 0, 0, 0, 0) })).Message);
        Assert.StartsWith("$.world.objects.triggerIdleCount", Assert.Throws<InvalidDataException>(
            () => Save(objects: new[] { new PlayerSaveObject(60, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 65536) })).Message);
        Assert.StartsWith("$.world.objectScripts.slotIndex", Assert.Throws<InvalidDataException>(() => Save(objectScripts: new[] { new PlayerSaveObjectScript(60, 6, 0) })).Message);
    }

    /// <summary>Independent hand-written literal fixture helpers; every value is chosen from the
    /// reference domains, none is derived from the contract under test.</summary>
    private static PlayerSaveV1 Save(int formatVersion = 1, int saveSlot = 3, int savedTimes = 65535,
        string contentIdentity = "modern-context/first-battle@sha256:9f2c",
        IReadOnlyList<PlayerSaveRole>? roles = null, int roleSlot = 0, PlayerSaveRole? role = null,
        IReadOnlyList<int>? party = null, IReadOnlyList<PlayerSaveInventorySlot>? inventory = null,
        int sceneId = 12, PlayerSaveLeader? leader = null, IReadOnlyList<PlayerSaveObject>? objects = null,
        IReadOnlyList<PlayerSaveSceneScript>? sceneScripts = null, IReadOnlyList<PlayerSaveObjectScript>? objectScripts = null,
        int battlefield = 7, PlayerSaveMusic? music = null, PlayerSaveWorld? world = null)
    {
        var list = (roles ?? Roles()).ToList();
        if (role is not null) list[roleSlot] = role;
        return new(formatVersion, saveSlot, savedTimes, contentIdentity, list,
            party ?? new[] { 2, 0, 5 }, inventory ?? new[] { new PlayerSaveInventorySlot(7, 3, 2), new PlayerSaveInventorySlot(65535, 0, 65535) },
            123456789, world ?? new PlayerSaveWorld(sceneId, leader ?? new PlayerSaveLeader(-3192, 1032, 3, 1, 2),
            objects ?? Objects(), sceneScripts ?? new[] { new PlayerSaveSceneScript(12, 4, 9), new PlayerSaveSceneScript(13, 40, 0) },
            objectScripts ?? new[] { new PlayerSaveObjectScript(1, 0, 328), new PlayerSaveObjectScript(60, 1, 0) }, battlefield, music));
    }

    private static PlayerSaveRole Role(int roleId = 0, int level = 1, int hp = 90, int maxHp = 90, int mp = 12, int maxMp = 30,
        PlayerSaveExperience? primary = null, IReadOnlyDictionary<string, PlayerSaveExperience>? hidden = null,
        bool hiddenExtraTrack = false, IReadOnlyList<int>? equipment = null, IReadOnlyList<int>? magics = null,
        int coveredByRoleId = 0, int attack = 12, int magicPower = 11, int defense = 10, int dexterity = 9, int fleeRate = 8) =>
        roleId == 5
            ? new(5, level == 1 ? 99 : level, hp == 90 ? 1 : hp, maxHp == 90 ? 65535 : maxHp, mp == 12 ? 0 : mp, maxMp == 30 ? 65535 : maxMp,
                primary ?? new PlayerSaveExperience(39, 4, 4), Hidden(hidden, hiddenExtraTrack),
                equipment ?? new[] { 1, 0, 0, 0, 0, 0 }, magics ?? new[] { 65, 0 }, coveredByRoleId == 0 ? 4 : coveredByRoleId,
                attack == 12 ? 210 : attack, magicPower == 11 ? 205 : magicPower, defense == 10 ? 200 : defense,
                dexterity == 9 ? 195 : dexterity, fleeRate == 8 ? 190 : fleeRate)
            : new(roleId, level, hp, maxHp, mp, maxMp, primary ?? new PlayerSaveExperience(0, 1, 0),
                Hidden(hidden, hiddenExtraTrack), equipment ?? new[] { 0, 0, 0, 0, 0, 0 }, magics ?? Array.Empty<int>(),
                coveredByRoleId, attack, magicPower, defense, dexterity, fleeRate);

    private static IReadOnlyDictionary<string, PlayerSaveExperience> Hidden(
        IReadOnlyDictionary<string, PlayerSaveExperience>? hidden, bool extraTrack)
    {
        var tracks = hidden is not null
            ? new Dictionary<string, PlayerSaveExperience>(hidden, StringComparer.Ordinal)
            : new Dictionary<string, PlayerSaveExperience>(StringComparer.Ordinal)
            {
                ["health"] = new(17, 3, 4),
                ["magic"] = new(0, 3, 0),
                ["attack"] = new(87, 3, 1),
                ["magicPower"] = new(5, 2, 9),
                ["defense"] = new(0, 3, 0),
                ["dexterity"] = new(1, 1, 1),
                ["flee"] = new(0, 3, 0),
            };
        if (extraTrack) tracks["extra"] = new(1, 1, 1);
        return tracks;
    }

    private static IReadOnlyList<PlayerSaveRole> Roles(int roleCount = 6, bool duplicateRoleId = false) =>
        Enumerable.Range(0, roleCount).Select(i => Role(duplicateRoleId && i == 5 ? 4 : i)).ToArray();

    private static IReadOnlyList<PlayerSaveObject> Objects() => new[]
    {
        new PlayerSaveObject(60, 1450, 898, 2, 1, 1, 0, 1, 392, 0, 5, 11),
        new PlayerSaveObject(3, 1920, 1000, 3, 0, -2, 800, 0, 0, 77, 0, 0),
    };
}
