using System.Text;
using System.Text.Json.Nodes;
using Pal.Core;
using Xunit;

namespace Pal.Core.Tests;

/// <summary>M6-C leaf #223 (parent #148, frozen contract #221): the versioned, fail-closed v1
/// player-save JSON codec and content-identity binding. Independent oracles are the frozen
/// #221 storage-domain matrix (docs/milestones/m6/m6c-save-matrix.md), the fixed SDLPal
/// reference format behavior (global.c PAL_SaveGame_Common/PAL_LoadGame_Common domains,
/// palcommon.h limits) and hand-written literal JSON/snapshot fixtures — never values produced
/// by the codec under test.</summary>
public sealed class PlayerSaveJsonTests
{
    [Fact]
    public void Round_trips_a_legal_v1_snapshot_field_by_field()
    {
        PlayerSaveV1 save = Fixture.Save();
        byte[] encoded = PlayerSaveJson.EncodeToUtf8(save);
        Assert.True(encoded.Length > 0,
            "PAL_SAVE_JSON_RED: EncodeToUtf8 must produce the versioned v1 UTF-8 JSON text of the frozen storage domains");
        PlayerSaveV1 decoded = PlayerSaveJson.Decode(encoded, Fixture.Identity);

        // Header words round-trip verbatim (#221 D0/D1).
        Assert.Equal(1, decoded.FormatVersion);
        Assert.Equal(2, decoded.SaveSlot);
        Assert.Equal(9, decoded.SavedTimes);
        Assert.Equal(Fixture.Identity, decoded.ContentIdentity);

        // Six complete role records: identity, vitals, both experience tracks, equipment grid,
        // learned magic row, cover and the permanent base panel words (#221 D2–D4).
        Assert.Equal(6, decoded.Roles.Count);
        for (int i = 0; i < decoded.Roles.Count; i++)
        {
            PlayerSaveRole expected = save.Roles[i], actual = decoded.Roles[i];
            Assert.Equal(expected.RoleId, actual.RoleId);
            Assert.Equal(expected.Level, actual.Level);
            Assert.Equal(expected.Hp, actual.Hp);
            Assert.Equal(expected.MaxHp, actual.MaxHp);
            Assert.Equal(expected.Mp, actual.Mp);
            Assert.Equal(expected.MaxMp, actual.MaxMp);
            Assert.Equal(expected.PrimaryExperience, actual.PrimaryExperience);
            Assert.Equal(expected.HiddenExperience.Count, actual.HiddenExperience.Count);
            foreach (string track in expected.HiddenExperience.Keys)
                Assert.Equal(expected.HiddenExperience[track], actual.HiddenExperience[track]);
            Assert.Equal(expected.Equipment, actual.Equipment);
            Assert.Equal(expected.Magics, actual.Magics);
            Assert.Equal(expected.CoveredByRoleId, actual.CoveredByRoleId);
            Assert.Equal((expected.Attack, expected.MagicPower, expected.Defense, expected.Dexterity, expected.FleeRate),
                (actual.Attack, actual.MagicPower, actual.Defense, actual.Dexterity, actual.FleeRate));
        }
        Assert.Equal(new[] { 65, 0 }, decoded.Roles[5].Magics);
        Assert.Equal(new[] { 1, 0, 0, 0, 0, 0 }, decoded.Roles[5].Equipment);

        // Party identity/order, occupied inventory rows (the depleted row stays occupied), DWORD cash.
        Assert.Equal(new[] { 2, 0, 5 }, decoded.PartyRoleIds);
        Assert.Equal(new[]
        {
            new PlayerSaveInventorySlot(7, 3, 2),
            new PlayerSaveInventorySlot(65535, 0, 65535),
        }, decoded.Inventory);
        Assert.Equal(123456789u, decoded.Cash);

        // World domain: scene identity, leader words, object dynamic words (negative state and
        // vanish countdown included), retained cross-scene script slots, control words (#221 D10–D13).
        Assert.Equal(12, decoded.World.SceneId);
        Assert.Equal(new PlayerSaveLeader(-3192, 1032, 3, 1, 2), decoded.World.Leader);
        Assert.Equal(new[]
        {
            new PlayerSaveObject(60, 1450, 898, 2, 1, 1, 0, 1, 392, 0, 5, 11),
            new PlayerSaveObject(3, 1920, 1000, 3, 0, -2, 800, 0, 0, 77, 0, 0),
        }, decoded.World.Objects);
        Assert.Equal(new[]
        {
            new PlayerSaveSceneScript(12, 4, 9),
            new PlayerSaveSceneScript(13, 40, 0),
        }, decoded.World.SceneScripts);
        Assert.Equal(new[]
        {
            new PlayerSaveObjectScript(1, 0, 328),
            new PlayerSaveObjectScript(60, 1, 0),
        }, decoded.World.ObjectScripts);
        Assert.Equal(7, decoded.World.Battlefield);
        Assert.Equal(new PlayerSaveMusic(3, 1), decoded.World.Music);
    }

    [Fact]
    public void Decodes_the_hand_written_literal_fixture_and_reencodes_it_byte_for_byte()
    {
        // Decode a hand-written JSON text and check every domain against independent literals.
        PlayerSaveV1 save = PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson), Fixture.Identity);
        Assert.Equal(1, save.FormatVersion);
        Assert.Equal(2, save.SaveSlot);
        Assert.Equal(9, save.SavedTimes);
        Assert.Equal(Fixture.Identity, save.ContentIdentity);
        Assert.Equal(6, save.Roles.Count);
        Assert.Equal(new PlayerSaveExperience(0, 1, 0), save.Roles[0].PrimaryExperience);
        Assert.Empty(save.Roles[0].HiddenExperience);
        Assert.Equal((0, 480, 0, 10), (save.Roles[3].Hp, save.Roles[3].MaxHp, save.Roles[3].Mp, save.Roles[3].MaxMp));
        Assert.Equal(new PlayerSaveExperience(41, 2, 3), save.Roles[4].HiddenExperience["health"]);
        Assert.Equal(new PlayerSaveExperience(39, 4, 4), save.Roles[5].PrimaryExperience);
        Assert.Equal(new PlayerSaveExperience(87, 3, 1), save.Roles[5].HiddenExperience["attack"]);
        Assert.Equal(new PlayerSaveExperience(0, 3, 0), save.Roles[5].HiddenExperience["flee"]);
        Assert.Equal(new[] { 1, 0, 0, 0, 0, 0 }, save.Roles[5].Equipment);
        Assert.Equal(new[] { 65, 0 }, save.Roles[5].Magics);
        Assert.Equal(4, save.Roles[5].CoveredByRoleId);
        Assert.Equal((210, 205, 200, 195, 190), (save.Roles[5].Attack, save.Roles[5].MagicPower,
            save.Roles[5].Defense, save.Roles[5].Dexterity, save.Roles[5].FleeRate));
        Assert.Equal(new[] { 2, 0, 5 }, save.PartyRoleIds);
        Assert.Equal(new[]
        {
            new PlayerSaveInventorySlot(7, 3, 2),
            new PlayerSaveInventorySlot(65535, 0, 65535),
        }, save.Inventory);
        Assert.Equal(123456789u, save.Cash);
        Assert.Equal(new PlayerSaveLeader(-3192, 1032, 3, 1, 2), save.World.Leader);
        Assert.Equal(new PlayerSaveObject(3, 1920, 1000, 3, 0, -2, 800, 0, 0, 77, 0, 0), save.World.Objects[1]);
        Assert.Equal(new PlayerSaveSceneScript(13, 40, 0), save.World.SceneScripts[1]);
        Assert.Equal(new PlayerSaveObjectScript(1, 0, 328), save.World.ObjectScripts[0]);
        Assert.Equal(new PlayerSaveMusic(3, 1), save.World.Music);

        // Bidirectional pin: the independently constructed snapshot encodes to exactly the
        // hand-written canonical text — no self-generated oracle (encode→decode self-agreement
        // alone never proves field completeness, #148 Testing Decision 3).
        Assert.Equal(Fixture.LiteralJson, Encoding.UTF8.GetString(PlayerSaveJson.EncodeToUtf8(Fixture.Save())));
    }

    [Fact]
    public void Encoding_is_deterministic_with_a_frozen_field_and_track_order()
    {
        Assert.Equal(PlayerSaveJson.EncodeToUtf8(Fixture.Save()), PlayerSaveJson.EncodeToUtf8(Fixture.Save()));
        // Dictionary insertion order of the hidden tracks never leaks into the bytes: the fixture
        // builds role 5 as flee-then-attack while the frozen text carries attack before flee.
        Assert.Equal(Bytes(Fixture.LiteralJson), PlayerSaveJson.EncodeToUtf8(Fixture.Save()));
    }

    [Fact]
    public void Encoded_output_is_bom_free_compact_utf8_and_omits_an_absent_music_word()
    {
        byte[] bytes = PlayerSaveJson.EncodeToUtf8(Fixture.Save(withMusic: false));
        Assert.Equal((byte)'{', bytes[0]);
        var world = JsonNode.Parse(Encoding.UTF8.GetString(bytes))!["world"]!.AsObject();
        Assert.False(world.ContainsKey("music"),
            "an independent context that never ran setMusic omits the music key instead of writing null");
        Assert.Null(PlayerSaveJson.Decode(bytes, Fixture.Identity).World.Music);
    }

    [Fact]
    public void Rejects_pre_decode_limits_length_utf8_depth_and_truncation()
    {
        // Over-cap bytes reject on length alone, before any decoding of their content.
        InvalidDataException lengthError = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(new byte[PlayerSaveContract.MaxFileBytes + 1], Fixture.Identity));
        Assert.Contains($"over the frozen {PlayerSaveContract.MaxFileBytes}-byte pre-decode cap", lengthError.Message);

        // Exactly at the cap the length gate passes and garbage rejects as malformed JSON instead.
        InvalidDataException malformed = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(new byte[PlayerSaveContract.MaxFileBytes], Fixture.Identity));
        Assert.Contains("not well-formed JSON", malformed.Message);

        InvalidDataException encoding = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(new byte[] { 0x7B, 0x22, 0x61, 0x22, 0x3A, 0xFF, 0x7D }, Fixture.Identity));
        Assert.Contains("not valid UTF-8", encoding.Message);

        InvalidDataException truncated = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Encoding.UTF8.GetBytes(Fixture.LiteralJson)[..400], Fixture.Identity));
        Assert.Contains("not well-formed JSON", truncated.Message);

        InvalidDataException deep = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Encoding.UTF8.GetBytes("[[[[[[[[1]]]]]]]]"), Fixture.Identity));
        Assert.Contains($"depth {PlayerSaveJson.MaxJsonDepth}", deep.Message);
    }

    [Fact]
    public void Rejects_version_and_content_identity_header_violations_fail_closed()
    {
        InvalidDataException missing = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace("\"formatVersion\":1,", "")), Fixture.Identity));
        Assert.StartsWith("$.formatVersion", missing.Message);

        InvalidDataException unknown = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace("\"formatVersion\":1,", "\"formatVersion\":2,")), Fixture.Identity));
        Assert.StartsWith("$.formatVersion", unknown.Message);
        Assert.Contains("never migrates", unknown.Message);

        // An unknown version rejects before any field validation, even with extra v2-style fields.
        InvalidDataException future = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace(
                "\"formatVersion\":1,", "\"formatVersion\":2,\"futureField\":1,")), Fixture.Identity));
        Assert.Contains("never migrates", future.Message);

        InvalidDataException stringVersion = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace(
                "\"formatVersion\":1,", "\"formatVersion\":\"1\",")), Fixture.Identity));
        Assert.StartsWith("$.formatVersion", stringVersion.Message);

        InvalidDataException fractional = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace("\"saveSlot\":2,", "\"saveSlot\":2.5,")), Fixture.Identity));
        Assert.StartsWith("$.saveSlot", fractional.Message);
        Assert.Contains("integer", fractional.Message);

        // Content identity binds the save to the prepared gameplay content, Ordinal and exact —
        // no fuzzy or case-insensitive matching could quietly map one content set onto another.
        InvalidDataException mismatch = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson), "story/scene12@sha256:5e7f2c"));
        Assert.StartsWith("$.contentIdentity", mismatch.Message);
        Assert.Contains("modern-context/first-battle@sha256:9f2c", mismatch.Message);
        Assert.Contains("story/scene12@sha256:5e7f2c", mismatch.Message);
        Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson), "Modern-Context/First-Battle@sha256:9f2c"));

        InvalidDataException noExpected = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson), "  "));
        Assert.StartsWith("$.contentIdentity", noExpected.Message);
        Assert.Throws<InvalidDataException>(() => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson), null!));

        InvalidDataException blank = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace(Fixture.Identity, "")), Fixture.Identity));
        Assert.StartsWith("$.contentIdentity", blank.Message);
    }

    [Fact]
    public void Rejects_duplicate_keys_and_unknown_fields_anywhere_in_the_tree()
    {
        InvalidDataException duplicateRoot = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace(
                "\"saveSlot\":2,", "\"saveSlot\":2,\"saveSlot\":3,")), Fixture.Identity));
        Assert.Contains("duplicate", duplicateRoot.Message);
        Assert.Contains("saveSlot", duplicateRoot.Message);

        InvalidDataException duplicateNested = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace(
                "\"value\":87,\"level\":3,\"count\":1}", "\"value\":87,\"level\":3,\"count\":1,\"count\":2}")), Fixture.Identity));
        Assert.Contains("duplicate", duplicateNested.Message);

        InvalidDataException unknownRoot = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace(
                "\"cash\":123456789", "\"cash\":123456789,\"extra\":1")), Fixture.Identity));
        Assert.Contains("unknown field 'extra'", unknownRoot.Message);

        InvalidDataException unknownRole = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace(
                "\"coveredByRoleId\":4,", "\"coveredByRoleId\":4,\"bonus\":1,")), Fixture.Identity));
        Assert.Contains("unknown field 'bonus'", unknownRole.Message);

        InvalidDataException unknownWorld = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace(
                "\"battlefield\":7,", "\"battlefield\":7,\"nope\":1,")), Fixture.Identity));
        Assert.Contains("unknown field 'nope'", unknownWorld.Message);

        // The trigger idle word is a required v1 field: absence and a repeated key both reject
        // before any state domain is built.
        InvalidDataException missingTriggerIdle = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace(",\"triggerIdleCount\":11", "")), Fixture.Identity));
        Assert.Contains("triggerIdleCount", missingTriggerIdle.Message);
        InvalidDataException duplicateTriggerIdle = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace(
                "\"triggerIdleCount\":11", "\"triggerIdleCount\":11,\"triggerIdleCount\":12")), Fixture.Identity));
        Assert.Contains("duplicate", duplicateTriggerIdle.Message);
        Assert.Contains("triggerIdleCount", duplicateTriggerIdle.Message);
    }

    [Fact]
    public void Rejects_wrong_types_dimensions_and_collection_caps_at_decode_stage()
    {
        InvalidDataException wrongType = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace("\"hp\":90,", "\"hp\":\"90\",")), Fixture.Identity));
        Assert.StartsWith("$.roles[0].hp", wrongType.Message);
        Assert.Contains("integer", wrongType.Message);

        InvalidDataException triggerIdleType = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace(
                "\"triggerIdleCount\":11", "\"triggerIdleCount\":\"11\"")), Fixture.Identity));
        Assert.StartsWith("$.world.objects[0].triggerIdleCount", triggerIdleType.Message);
        Assert.Contains("integer", triggerIdleType.Message);

        InvalidDataException notArray = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace("\"partyRoleIds\":[2,0,5]", "\"partyRoleIds\":7")), Fixture.Identity));
        Assert.StartsWith("$.partyRoleIds", notArray.Message);

        InvalidDataException fourMembers = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace("\"partyRoleIds\":[2,0,5]", "\"partyRoleIds\":[2,0,5,1]")), Fixture.Identity));
        Assert.StartsWith("$.partyRoleIds", fourMembers.Message);

        InvalidDataException shortEquipment = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace(
                "\"equipment\":[0,0,0,0,0,0],\"magics\":[]", "\"equipment\":[0,0,0,0,0],\"magics\":[]")), Fixture.Identity));
        Assert.StartsWith("$.roles[0].equipment", shortEquipment.Message);

        InvalidDataException longMagics = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace(
                "\"magics\":[65,0]", "\"magics\":[" + string.Join(",", Enumerable.Repeat("65", 33)) + "]")), Fixture.Identity));
        Assert.StartsWith("$.roles[5].magics", longMagics.Message);
        Assert.Contains("32", longMagics.Message);

        // Five role records reject on dimension before any state domain is built.
        int start = Fixture.LiteralJson.IndexOf("{\"roleId\":0,", StringComparison.Ordinal);
        int end = Fixture.LiteralJson.IndexOf(",{\"roleId\":1,", StringComparison.Ordinal);
        InvalidDataException fiveRoles = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Remove(start, end - start + 1)), Fixture.Identity));
        Assert.StartsWith("$.roles", fiveRoles.Message);
        Assert.Contains("6", fiveRoles.Message);

        string rows = string.Join(",", Enumerable.Range(1, 257)
            .Select(id => $"{{\"itemId\":{id},\"amount\":1,\"amountInUse\":0}}"));
        InvalidDataException bigInventory = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace(
                "\"inventory\":[{\"itemId\":7,\"amount\":3,\"amountInUse\":2},{\"itemId\":65535,\"amount\":0,\"amountInUse\":65535}]",
                "\"inventory\":[" + rows + "]")), Fixture.Identity));
        Assert.StartsWith("$.inventory", bigInventory.Message);
        Assert.Contains("256", bigInventory.Message);

        string tracks = string.Join(",", new[] { "health", "magic", "attack", "magicPower", "defense", "dexterity", "flee", "extra" }
            .Select(name => $"\"{name}\":{{\"value\":1,\"level\":1,\"count\":1}}"));
        InvalidDataException eightTracks = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace(
                "\"hiddenExperience\":{\"health\":{\"value\":41,\"level\":2,\"count\":3}}",
                "\"hiddenExperience\":{" + tracks + "}")), Fixture.Identity));
        Assert.Contains("$.roles[4].hiddenExperience", eightTracks.Message);

        // An explicit null is malformed for an optional object key; absence is the only null form.
        InvalidDataException nullMusic = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace(
                "\"music\":{\"musicId\":3,\"mode\":1}", "\"music\":null")), Fixture.Identity));
        Assert.StartsWith("$.world.music", nullMusic.Message);
    }

    [Fact]
    public void Rejects_out_of_domain_values_fail_closed_with_locatable_diagnostics()
    {
        InvalidDataException amount = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace("\"amount\":3,", "\"amount\":100,")), Fixture.Identity));
        Assert.StartsWith("$.inventory.amount", amount.Message);

        InvalidDataException direction = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace(
                "\"direction\":3,\"layer\":1", "\"direction\":4,\"layer\":1")), Fixture.Identity));
        Assert.StartsWith("$.world.leader.direction", direction.Message);

        InvalidDataException coordinate = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace("\"x\":-3192", "\"x\":-32769")), Fixture.Identity));
        Assert.StartsWith("$.world.leader.x", coordinate.Message);

        InvalidDataException state = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace("\"state\":-2", "\"state\":32768")), Fixture.Identity));
        Assert.StartsWith("$.world.objects.state", state.Message);

        InvalidDataException triggerIdleRange = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace(
                "\"triggerIdleCount\":11", "\"triggerIdleCount\":65536")), Fixture.Identity));
        Assert.StartsWith("$.world.objects.triggerIdleCount", triggerIdleRange.Message);

        InvalidDataException cash = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace("\"cash\":123456789", "\"cash\":4294967296")), Fixture.Identity));
        Assert.StartsWith("$.cash", cash.Message);
        Assert.Contains("4294967295", cash.Message);

        InvalidDataException battlefield = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace("\"battlefield\":7", "\"battlefield\":65536")), Fixture.Identity));
        Assert.StartsWith("$.world.battlefield", battlefield.Message);

        InvalidDataException slot = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace("\"saveSlot\":2,", "\"saveSlot\":0,")), Fixture.Identity));
        Assert.StartsWith("$.saveSlot", slot.Message);

        // Dangling and duplicate formal references reject without partial application.
        InvalidDataException danglingRole = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace("\"partyRoleIds\":[2,0,5]", "\"partyRoleIds\":[2,0,9]")), Fixture.Identity));
        Assert.StartsWith("$.partyRoleIds", danglingRole.Message);
        InvalidDataException duplicateItem = Assert.Throws<InvalidDataException>(
            () => PlayerSaveJson.Decode(Bytes(Fixture.LiteralJson.Replace("\"itemId\":65535", "\"itemId\":7")), Fixture.Identity));
        Assert.StartsWith("$.inventory", duplicateItem.Message);
    }

    private static byte[] Bytes(string json) => Encoding.UTF8.GetBytes(json);

    /// <summary>Independent hand-written literal fixtures: every JSON number and every snapshot
    /// value is chosen from the frozen #221 domains; none is derived from the codec under test.
    /// The canonical text is exactly what a deterministic v1 encoder must emit for this data.</summary>
    private static class Fixture
    {
        public const string Identity = "modern-context/first-battle@sha256:9f2c";

        public const string LiteralJson =
            """{"formatVersion":1,"saveSlot":2,"savedTimes":9,"contentIdentity":"modern-context/first-battle@sha256:9f2c","roles":[{"roleId":0,"level":1,"hp":90,"maxHp":90,"mp":12,"maxMp":30,"primaryExperience":{"value":0,"level":1,"count":0},"hiddenExperience":{},"equipment":[0,0,0,0,0,0],"magics":[],"coveredByRoleId":0,"attack":12,"magicPower":11,"defense":10,"dexterity":9,"fleeRate":8},{"roleId":1,"level":7,"hp":100,"maxHp":100,"mp":20,"maxMp":20,"primaryExperience":{"value":10,"level":2,"count":3},"hiddenExperience":{},"equipment":[0,0,0,0,0,0],"magics":[],"coveredByRoleId":0,"attack":22,"magicPower":21,"defense":20,"dexterity":19,"fleeRate":18},{"roleId":2,"level":33,"hp":110,"maxHp":120,"mp":5,"maxMp":5,"primaryExperience":{"value":20,"level":3,"count":4},"hiddenExperience":{},"equipment":[0,0,0,0,0,0],"magics":[65],"coveredByRoleId":0,"attack":32,"magicPower":31,"defense":30,"dexterity":29,"fleeRate":28},{"roleId":3,"level":40,"hp":0,"maxHp":480,"mp":0,"maxMp":10,"primaryExperience":{"value":30,"level":4,"count":5},"hiddenExperience":{},"equipment":[0,0,0,0,0,0],"magics":[],"coveredByRoleId":0,"attack":42,"magicPower":41,"defense":40,"dexterity":39,"fleeRate":38},{"roleId":4,"level":50,"hp":55,"maxHp":66,"mp":7,"maxMp":8,"primaryExperience":{"value":40,"level":5,"count":6},"hiddenExperience":{"health":{"value":41,"level":2,"count":3}},"equipment":[0,0,1,0,0,0],"magics":[],"coveredByRoleId":0,"attack":52,"magicPower":51,"defense":50,"dexterity":49,"fleeRate":48},{"roleId":5,"level":99,"hp":1,"maxHp":65535,"mp":0,"maxMp":65535,"primaryExperience":{"value":39,"level":4,"count":4},"hiddenExperience":{"attack":{"value":87,"level":3,"count":1},"flee":{"value":0,"level":3,"count":0}},"equipment":[1,0,0,0,0,0],"magics":[65,0],"coveredByRoleId":4,"attack":210,"magicPower":205,"defense":200,"dexterity":195,"fleeRate":190}],"partyRoleIds":[2,0,5],"inventory":[{"itemId":7,"amount":3,"amountInUse":2},{"itemId":65535,"amount":0,"amountInUse":65535}],"cash":123456789,"world":{"sceneId":12,"leader":{"x":-3192,"y":1032,"direction":3,"layer":1,"stepPhase":2},"objects":[{"objectId":60,"x":1450,"y":898,"direction":2,"currentFrame":1,"state":1,"vanishTime":0,"triggerMode":1,"triggerEntry":392,"autoEntry":0,"autoIdleCount":5,"triggerIdleCount":11},{"objectId":3,"x":1920,"y":1000,"direction":3,"currentFrame":0,"state":-2,"vanishTime":800,"triggerMode":0,"triggerEntry":0,"autoEntry":77,"autoIdleCount":0,"triggerIdleCount":0}],"sceneScripts":[{"sceneId":12,"onEnterEntry":4,"onTeleportEntry":9},{"sceneId":13,"onEnterEntry":40,"onTeleportEntry":0}],"objectScripts":[{"objectId":1,"slotIndex":0,"entry":328},{"objectId":60,"slotIndex":1,"entry":0}],"battlefield":7,"music":{"musicId":3,"mode":1}}}""";

        public static PlayerSaveV1 Save(bool withMusic = true)
        {
            var roles = new PlayerSaveRole[]
            {
                new(0, 1, 90, 90, 12, 30, new PlayerSaveExperience(0, 1, 0), Tracks(),
                    new[] { 0, 0, 0, 0, 0, 0 }, Array.Empty<int>(), 0, 12, 11, 10, 9, 8),
                new(1, 7, 100, 100, 20, 20, new PlayerSaveExperience(10, 2, 3), Tracks(),
                    new[] { 0, 0, 0, 0, 0, 0 }, Array.Empty<int>(), 0, 22, 21, 20, 19, 18),
                new(2, 33, 110, 120, 5, 5, new PlayerSaveExperience(20, 3, 4), Tracks(),
                    new[] { 0, 0, 0, 0, 0, 0 }, new[] { 65 }, 0, 32, 31, 30, 29, 28),
                new(3, 40, 0, 480, 0, 10, new PlayerSaveExperience(30, 4, 5), Tracks(),
                    new[] { 0, 0, 0, 0, 0, 0 }, Array.Empty<int>(), 0, 42, 41, 40, 39, 38),
                new(4, 50, 55, 66, 7, 8, new PlayerSaveExperience(40, 5, 6),
                    Tracks(("health", new PlayerSaveExperience(41, 2, 3))),
                    new[] { 0, 0, 1, 0, 0, 0 }, Array.Empty<int>(), 0, 52, 51, 50, 49, 48),
                new(5, 99, 1, 65535, 0, 65535, new PlayerSaveExperience(39, 4, 4),
                    Tracks(("flee", new PlayerSaveExperience(0, 3, 0)), ("attack", new PlayerSaveExperience(87, 3, 1))),
                    new[] { 1, 0, 0, 0, 0, 0 }, new[] { 65, 0 }, 4, 210, 205, 200, 195, 190),
            };
            var world = new PlayerSaveWorld(12, new PlayerSaveLeader(-3192, 1032, 3, 1, 2),
                new[]
                {
                    new PlayerSaveObject(60, 1450, 898, 2, 1, 1, 0, 1, 392, 0, 5, 11),
                    new PlayerSaveObject(3, 1920, 1000, 3, 0, -2, 800, 0, 0, 77, 0, 0),
                },
                new[] { new PlayerSaveSceneScript(12, 4, 9), new PlayerSaveSceneScript(13, 40, 0) },
                new[] { new PlayerSaveObjectScript(1, 0, 328), new PlayerSaveObjectScript(60, 1, 0) },
                7, withMusic ? new PlayerSaveMusic(3, 1) : null);
            return new PlayerSaveV1(1, 2, 9, Identity, roles, new[] { 2, 0, 5 },
                new[] { new PlayerSaveInventorySlot(7, 3, 2), new PlayerSaveInventorySlot(65535, 0, 65535) },
                123456789, world);
        }

        private static IReadOnlyDictionary<string, PlayerSaveExperience> Tracks(
            params (string Name, PlayerSaveExperience Experience)[] entries)
        {
            var tracks = new Dictionary<string, PlayerSaveExperience>(StringComparer.Ordinal);
            foreach ((string name, PlayerSaveExperience experience) in entries) tracks.Add(name, experience);
            return tracks;
        }
    }
}
