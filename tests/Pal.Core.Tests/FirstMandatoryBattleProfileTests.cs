using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Pal.TestFixtures;
using Pal.Core;
using Xunit;

namespace Pal.Core.Tests;

public sealed class FirstMandatoryBattleProfileTests
{
    [Fact]
    public void Load_exposes_the_frozen_typed_profile()
    {
        var fixture = BattleContextTestFixture.Valid();

        FirstMandatoryBattleProfile profile = FirstMandatoryBattleProfile.Load(fixture.IndexBytes, fixture.Files);

        Assert.Equal("first-mandatory-battle-pre-trigger-v1", profile.ProfileId);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(fixture.ContextBytes)), profile.ContextSha256);
        Assert.Equal(2, profile.Identity.SceneId);
        Assert.Equal(12, profile.Identity.MapId);
        Assert.Equal(28, profile.Identity.OwnerId);
        Assert.Equal(0x09c3, profile.Identity.TriggerAddress);
        Assert.False(profile.Provenance.OptionalGrinding);
        Assert.False(profile.Provenance.PlayerEquipmentChanges);
        Assert.Equal((6, 6, 5), (profile.Layout.EquipmentRoleCount, profile.Layout.EquipmentSlotCount, profile.Layout.LevelUpMagicRoleCount));
        Assert.Equal(new[] { 0 }, profile.PartyRoleIds);
        Assert.Equal(new[] { 196, 225, 208, 166, 235, 249 }, profile.Roles[0].Equipment);
        Assert.Equal(new[] { 296, 345 }, profile.Roles[0].Magics);
        Assert.Equal((5, 28, 240, 10, 240, 0), (profile.Roles[1].Level, profile.Roles[1].Hp, profile.Roles[1].MaxHp, profile.Roles[1].Mp, profile.Roles[1].MaxMp, profile.Roles[1].CoveredByRoleId));
        Assert.Equal(7, profile.Roles[0].HiddenExperience.Count);
        Assert.Equal(9, profile.Roles[0].Statuses.Count);
        Assert.Equal((1248, 256, "east", 0), (profile.World.Leader.X, profile.World.Leader.Y, profile.World.Leader.Direction, profile.World.Leader.Phase));
        Assert.Equal((28, 3, true), (profile.World.SearchProof.OwnerId, profile.World.SearchProof.SampleIndex, profile.World.SearchProof.CollisionPass));
        Assert.Equal(0x09c3, profile.World.EventObjects.Single(x => x.OwnerId == 28).TriggerEntry);
        Assert.Equal("@09C3", profile.Closure.Entry);
        Assert.Equal(9, profile.Closure.Entries.Count);
        Assert.All(profile.Closure.Entries, entry => Assert.Equal("contract-only", entry.Status));
        BattleClosureEntry battle = profile.Closure.Entries.Single(x => x.Id == "battle-team-18");
        BattleCommandIdentity battleIdentity = Assert.IsType<BattleCommandIdentity>(battle.Identity);
        Assert.Equal(0x09fe, battleIdentity.CommandAddress);
        Assert.Equal(new[] { 0x0012, 0xa073, 0x0000 }, battleIdentity.Operands);
        Assert.Equal(new[] { 46, 47, 51, 53 }, battle.OwnerTickets);
        Assert.Equal(new[] { "events", "battle-data" }, battle.ResourceIds);
        Assert.Equal(6, profile.Resources.Count);
        Assert.Equal("battle/data.json", profile.Resources.Single(x => x.Id == "battle-data").Reference);
    }

    [Fact]
    public void Load_hashes_the_exact_context_bytes_and_requires_the_exact_file_set()
    {
        var fixture = BattleContextTestFixture.Valid();
        byte[] changed = [.. fixture.ContextBytes, (byte)' '];
        Assert.Throws<InvalidDataException>(() => FirstMandatoryBattleProfile.Load(fixture.IndexBytes,
            new Dictionary<string, byte[]> { ["battle-context.json"] = changed }));
        Assert.Throws<InvalidDataException>(() => FirstMandatoryBattleProfile.Load(fixture.IndexBytes, new Dictionary<string, byte[]>()));
        Assert.Throws<InvalidDataException>(() => FirstMandatoryBattleProfile.Load(fixture.IndexBytes,
            new Dictionary<string, byte[]> { ["battle-context.json"] = fixture.ContextBytes, ["extra.json"] = [] }));
        Assert.Throws<InvalidDataException>(() => FirstMandatoryBattleProfile.Load(fixture.IndexBytes,
            new Dictionary<string, byte[]> { ["../battle-context.json"] = fixture.ContextBytes }));
        Assert.Throws<InvalidDataException>(() => FirstMandatoryBattleProfile.Load(fixture.IndexBytes,
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { ["BATTLE-CONTEXT.JSON"] = fixture.ContextBytes }));
    }

    [Fact]
    public void Public_surface_exposes_exact_source_and_strict_typed_factories_only()
    {
        MethodInfo[] factories = typeof(FirstMandatoryBattleProfile).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.ReturnType == typeof(FirstMandatoryBattleProfile)).OrderBy(method => method.Name).ToArray();
        Assert.Equal(new[] { nameof(FirstMandatoryBattleProfile.FromTypedEnvelope), nameof(FirstMandatoryBattleProfile.Load) }, factories.Select(x => x.Name));
        Assert.Equal(new[] { typeof(FirstMandatoryBattleEnvelope) }, factories[0].GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(new[] { typeof(byte[]), typeof(IReadOnlyDictionary<string, byte[]>) }, factories[1].GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void Typed_envelope_roundtrip_preserves_all_layers_and_has_deterministic_non_json_hash()
    {
        FirstMandatoryBattleProfile source = LoadContextForTest(BattleContextTestFixture.Valid().ContextBytes);
        FirstMandatoryBattleEnvelope envelope = source.ToTypedEnvelope();
        FirstMandatoryBattleProfile roundtrip = FirstMandatoryBattleProfile.FromTypedEnvelope(envelope);
        Assert.Equal(source.ContextSha256, roundtrip.ContextSha256);
        Assert.Equal(source.Provenance.FieldSources.Count, roundtrip.Provenance.FieldSources.Count);
        Assert.Equal(source.Roles.Count, roundtrip.Roles.Count);
        Assert.Equal(source.World.EventObjects.Count, roundtrip.World.EventObjects.Count);
        Assert.Equal(source.Closure.Entries.Count, roundtrip.Closure.Entries.Count);
        Assert.Equal(source.Resources.Count, roundtrip.Resources.Count);
        byte[] canonical = FirstMandatoryBattleEnvelopeCodec.Encode(envelope);
        Assert.DoesNotContain((byte)'{', canonical.Take(16));
        Assert.Equal(FirstMandatoryBattleEnvelopeCodec.Sha256(envelope), Convert.ToHexStringLower(SHA256.HashData(canonical)));
    }

    [Fact]
    public void Load_exposes_all_nine_closed_typed_closure_identities()
    {
        FirstMandatoryBattleProfile profile = LoadContextForTest(BattleContextTestFixture.Valid().ContextBytes);
        Assert.Collection(profile.Closure.Entries,
            entry => { var x = Typed<BattleEventRangeIdentity>(entry); Assert.Equal((0x09c3, 0x09fe, 0x09ff, 0x0a32), (x.StartAddress, x.BattleAddress, x.WonStartAddress, x.EndAddress)); },
            entry => { var x = Typed<BattleCommandIdentity>(entry); Assert.Equal((0x09fe, 18, 495, 97, 2, 24), (x.CommandAddress, x.EnemyTeamId, x.EnemyObjectId, x.EnemyId, x.EnemyCount, x.BattlefieldId)); Assert.Equal(new[] { 0x0012, 0xa073, 0 }, x.Operands); Assert.Equal(("third-operand-zero-is-boss-no-flee", "encode-operands-from-semantic-fields"), (x.SpecialValue, x.ReverseEncoding)); },
            entry => { var x = Typed<BattleRoleHooksIdentity>(entry); Assert.Equal((1, 0xa96e, 0xa987, 0, 0xa9b5, 0xa9d1, "all-random-branches"), (x.DyingRoleId, x.DyingStartAddress, x.DyingEndAddress, x.FriendDeathRoleId, x.FriendDeathStartAddress, x.FriendDeathEndAddress, x.BranchCoverage)); },
            entry => { var x = Typed<BattleEquipmentLayoutIdentity>(entry); Assert.Equal((6, 6, 170, true), (x.RoleCount, x.SlotCount, x.DualAttackObjectId, x.PersistentEquipmentStatus)); },
            entry => { var x = Typed<BattleGrowthLayoutIdentity>(entry); Assert.Equal((5, true, true, true), (x.RoleCount, x.IncludesPrimaryExperience, x.IncludesHiddenExperience, x.IncludesRecoveryAndCleanup)); },
            entry => { var x = Typed<BattleDynamicAutoIdentity>(entry); Assert.Equal((0x0a36, 0x0a3b, "update-driven"), (x.StartAddress, x.EndAddress, x.Advance)); Assert.Equal(new[] { "movement", "direction", "hidden" }, x.Effects); },
            entry => { var x = Typed<BattleFailureBoundaryIdentity>(entry); Assert.Equal((0xa073, 0x004e, "diagnose-and-stop-without-reload-retry-or-exploration"), (x.LostEntryAddress, x.ReloadCommand, x.Behavior)); },
            entry => { var x = Typed<BattleImageSetIdentity>(entry); Assert.Equal((97, "ABC97", 24, "FBP24", true), (x.EnemyId, x.EnemyArchiveEntry, x.BattlefieldId, x.BattlefieldArchiveEntry, x.IncludesLayoutAndEffectFrames)); Assert.Equal(new[] { "F0", "F1" }, x.RoleSpriteIds); },
            entry => { var x = Typed<BattleLocalizedDialogueIdentity>(entry); Assert.Equal(new[] { "zh-CN", "en" }, x.Languages); Assert.Equal(new[] { "messages", "names", "portraits", "glyphs", "ui" }, x.Contents); });
    }

    [Fact]
    public void Closure_identity_hierarchy_is_closed_to_external_assemblies_and_has_exactly_nine_variants()
    {
        Type baseType = typeof(BattleClosureIdentity);
        ConstructorInfo[] externallyUsableConstructors = baseType
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(constructor => constructor.IsPublic || constructor.IsFamily || constructor.IsFamilyOrAssembly)
            .ToArray();
        Assert.Empty(externallyUsableConstructors);

        Type[] variants = baseType.Assembly.GetTypes()
            .Where(type => type.BaseType == baseType && !type.IsAbstract)
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToArray();
        Assert.All(variants, type => Assert.True(type.IsPublic && type.IsSealed, $"{type.Name} must be public sealed."));
        Assert.Equal(new[]
        {
            typeof(BattleCommandIdentity),
            typeof(BattleDynamicAutoIdentity),
            typeof(BattleEquipmentLayoutIdentity),
            typeof(BattleEventRangeIdentity),
            typeof(BattleFailureBoundaryIdentity),
            typeof(BattleGrowthLayoutIdentity),
            typeof(BattleImageSetIdentity),
            typeof(BattleLocalizedDialogueIdentity),
            typeof(BattleRoleHooksIdentity)
        }.OrderBy(type => type.Name, StringComparer.Ordinal), variants);
    }

    [Fact]
    public void Collection_bearing_closure_identities_defensively_copy_all_caller_lists()
    {
        int[] operands = [0x0012, 0xa073, 0];
        var effects = new List<string> { "movement", "direction", "hidden" };
        string[] roleSpriteIds = ["F0", "F1"];
        var languages = new List<string> { "zh-CN", "en" };
        string[] contents = ["messages", "names", "portraits", "glyphs", "ui"];
        var command = new BattleCommandIdentity(0x09fe, operands, 18, 495, 97, 2, 24, "special", "reverse");
        var dynamicAuto = new BattleDynamicAutoIdentity(0x0a36, 0x0a3b, effects, "update-driven");
        var images = new BattleImageSetIdentity(97, "ABC97", roleSpriteIds, 24, "FBP24", true);
        var dialogue = new BattleLocalizedDialogueIdentity(languages, contents);

        operands[0] = 999;
        effects[0] = "changed";
        roleSpriteIds[0] = "changed";
        languages[0] = "changed";
        contents[0] = "changed";

        Assert.Equal(new[] { 0x0012, 0xa073, 0 }, command.Operands);
        Assert.Equal(new[] { "movement", "direction", "hidden" }, dynamicAuto.Effects);
        Assert.Equal(new[] { "F0", "F1" }, images.RoleSpriteIds);
        Assert.Equal(new[] { "zh-CN", "en" }, dialogue.Languages);
        Assert.Equal(new[] { "messages", "names", "portraits", "glyphs", "ui" }, dialogue.Contents);
    }

    [Fact]
    public void Collection_bearing_closure_identities_fail_fast_for_null_lists()
    {
        Assert.Throws<ArgumentNullException>(() => new BattleCommandIdentity(0, null!, 0, 0, 0, 0, 0, "", ""));
        Assert.Throws<ArgumentNullException>(() => new BattleDynamicAutoIdentity(0, 0, null!, ""));
        Assert.Throws<ArgumentNullException>(() => new BattleImageSetIdentity(0, "", null!, 0, "", false));
        Assert.Throws<ArgumentNullException>(() => new BattleLocalizedDialogueIdentity(null!, []));
        Assert.Throws<ArgumentNullException>(() => new BattleLocalizedDialogueIdentity([], null!));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("type")]
    public void Load_rejects_non_strict_index_json(string mutation)
    {
        var fixture = BattleContextTestFixture.Valid();
        string index = Encoding.UTF8.GetString(fixture.IndexBytes);
        if (mutation == "duplicate") index = index.Replace("\"formatVersion\":1", "\"formatVersion\":1,\"formatVersion\":1", StringComparison.Ordinal);
        else
        {
            JsonObject node = JsonNode.Parse(index)!.AsObject();
            if (mutation == "unknown") node["unexpected"] = true;
            else node["formatVersion"] = "1";
            index = node.ToJsonString();
        }
        Assert.Throws<InvalidDataException>(() => FirstMandatoryBattleProfile.Load(Encoding.UTF8.GetBytes(index), fixture.Files));
    }

    [Theory]
    [InlineData("formatVersion", "2")]
    [InlineData("profile", "\"other\"")]
    [InlineData("contextFile", "\"other.json\"")]
    [InlineData("contextSha256", "\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"")]
    public void Load_rejects_index_version_profile_context_identity_and_hash_format(string field, string replacement)
    {
        var fixture = BattleContextTestFixture.Valid();
        string index = Encoding.UTF8.GetString(fixture.IndexBytes);
        string current = JsonNode.Parse(index)![field]!.ToJsonString();
        byte[] mutated = Encoding.UTF8.GetBytes(index.Replace($"\"{field}\":{current}", $"\"{field}\":{replacement}", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => FirstMandatoryBattleProfile.Load(mutated, fixture.Files));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("missing")]
    [InlineData("type")]
    [InlineData("range")]
    public void Load_rejects_duplicate_unknown_missing_type_and_range_errors(string mutation)
    {
        var fixture = BattleContextTestFixture.Valid();
        byte[] context;
        if (mutation == "duplicate") context = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(fixture.ContextBytes).Replace("\"formatVersion\":1", "\"formatVersion\":1,\"formatVersion\":1", StringComparison.Ordinal));
        else
        {
            JsonObject document = fixture.Document.DeepClone().AsObject();
            if (mutation == "unknown") document["unexpected"] = true;
            else if (mutation == "missing") document.Remove("closure");
            else if (mutation == "type") document["snapshot"]!["roles"]![0]!["hp"] = "150";
            else document["snapshot"]!["world"]!["eventObjects"]![0]!["vanishTime"] = 32768;
            context = Encoding.UTF8.GetBytes(document.ToJsonString());
        }
        Assert.Throws<InvalidDataException>(() => LoadContextForTest(context));
    }

    [Theory]
    [InlineData("role")]
    [InlineData("provenance")]
    [InlineData("owner28")]
    [InlineData("search")]
    [InlineData("closure")]
    [InlineData("resource")]
    [InlineData("unsafe-reference")]
    [InlineData("unsafe-rooted")]
    [InlineData("unsafe-backslash")]
    [InlineData("unsafe-nul")]
    [InlineData("unsafe-newline")]
    [InlineData("unsafe-double-slash")]
    [InlineData("unsafe-dot-segment")]
    [InlineData("unsafe-trailing-slash")]
    [InlineData("unsafe-windows-character")]
    [InlineData("unsafe-colon")]
    [InlineData("unsafe-trailing-dot")]
    [InlineData("unsafe-device-name")]
    public void Load_rejects_changes_to_frozen_contract_groups(string mutation)
    {
        var fixture = BattleContextTestFixture.Valid();
        JsonObject document = fixture.Document.DeepClone().AsObject();
        if (mutation == "role") document["snapshot"]!["roles"]![1]!["hp"] = 29;
        else if (mutation == "provenance") document["provenance"]!["fieldSources"]![0]!["sourceAddress"] = 0x0904;
        else if (mutation == "owner28") document["snapshot"]!["world"]!["eventObjects"]![0]!["spriteId"] = 211;
        else if (mutation == "search") document["snapshot"]!["world"]!["searchProof"]!["sampleIndex"] = 13;
        else if (mutation == "closure") document["closure"]!["entries"]!.AsArray().RemoveAt(0);
        else if (mutation == "resource") document["resources"]![0]!["kind"] = "other";
        else if (mutation == "unsafe-rooted") document["resources"]![0]!["reference"] = "/events/index.json";
        else if (mutation == "unsafe-backslash") document["resources"]![0]!["reference"] = "events\\index.json";
        else if (mutation == "unsafe-nul") document["resources"]![0]!["reference"] = "events/\0index.json";
        else if (mutation == "unsafe-newline") document["resources"]![0]!["reference"] = "events/\nindex.json";
        else if (mutation == "unsafe-double-slash") document["resources"]![0]!["reference"] = "events//index.json";
        else if (mutation == "unsafe-dot-segment") document["resources"]![0]!["reference"] = "events/./index.json";
        else if (mutation == "unsafe-trailing-slash") document["resources"]![0]!["reference"] = "events/";
        else if (mutation == "unsafe-windows-character") document["resources"]![0]!["reference"] = "events/index?.json";
        else if (mutation == "unsafe-colon") document["resources"]![0]!["reference"] = "https://example.invalid/index.json";
        else if (mutation == "unsafe-trailing-dot") document["resources"]![0]!["reference"] = "events./index.json";
        else if (mutation == "unsafe-device-name") document["resources"]![0]!["reference"] = "events/NUL.json";
        else document["resources"]![0]!["reference"] = "../events/index.json";
        Assert.Throws<InvalidDataException>(() => LoadContextForTest(Encoding.UTF8.GetBytes(document.ToJsonString())));
    }

    [Theory]
    [InlineData("COM¹")]
    [InlineData("events/COM².json")]
    [InlineData("nested/path/COM³.dat")]
    [InlineData("LPT¹.txt")]
    [InlineData("events/LPT²")]
    [InlineData("nested/LPT³.json")]
    public void Load_rejects_windows_superscript_device_names(string reference)
    {
        JsonObject document = BattleContextTestFixture.Valid().Document.DeepClone().AsObject();
        document["resources"]![0]!["reference"] = reference;
        Assert.Throws<InvalidDataException>(() => LoadContextForTest(Encoding.UTF8.GetBytes(document.ToJsonString())));
    }

    [Fact]
    public void Load_allows_ordinary_unicode_resource_filenames()
    {
        JsonObject document = BattleContextTestFixture.Valid().Document.DeepClone().AsObject();
        document["resources"]![0]!["reference"] = "events/剧情-¹.json";
        LoadContextForTest(Encoding.UTF8.GetBytes(document.ToJsonString()));
    }

    [Fact]
    public void Load_rejects_invalid_utf8_in_index_and_context()
    {
        var fixture = BattleContextTestFixture.Valid();
        Assert.Throws<InvalidDataException>(() => FirstMandatoryBattleProfile.Load([0xff], fixture.Files));
        Assert.Throws<InvalidDataException>(() => LoadContextForTest([0xff]));
    }

    [Fact]
    public void Load_allows_underscore_comments_but_rejects_other_unknown_fields()
    {
        var fixture = BattleContextTestFixture.Valid();
        JsonObject document = fixture.Document.DeepClone().AsObject();
        document["_note"] = "root comment";
        document["snapshot"]!["world"]!["leader"]!["_note"] = "nested comment";
        document["closure"]!["entries"]![0]!["identity"]!["_note"] = "structured identity comment";
        LoadContextForTest(Encoding.UTF8.GetBytes(document.ToJsonString()));
        document["snapshot"]!["world"]!["leader"]!["note"] = "not a comment";
        Assert.Throws<InvalidDataException>(() => LoadContextForTest(Encoding.UTF8.GetBytes(document.ToJsonString())));
    }

    [Theory]
    [InlineData("hp")]
    [InlineData("maxHp")]
    [InlineData("mp")]
    [InlineData("maxMp")]
    public void Load_rejects_original_base_for_unestablished_role0_vitals(string property)
    {
        JsonObject document = BattleContextTestFixture.Valid().Document.DeepClone().AsObject();
        JsonObject source = document["provenance"]!["fieldSources"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(item => item["path"]!.GetValue<string>() == $"snapshot.roles[0].{property}");
        Assert.Equal("author-policy", source["sourceKind"]!.GetValue<string>());
        Assert.Null(source["sourceAddress"]);
        source["sourceKind"] = "original-base";
        Assert.Throws<InvalidDataException>(() => LoadContextForTest(Encoding.UTF8.GetBytes(document.ToJsonString())));
    }

    [Fact]
    public void CreateSessionState_deep_copies_all_nested_mutable_state_between_sessions_and_profile()
    {
        var fixture = BattleContextTestFixture.Valid();
        FirstMandatoryBattleProfile profile = FirstMandatoryBattleProfile.Load(fixture.IndexBytes, fixture.Files);

        FirstMandatoryBattleSessionState first = profile.CreateSessionState();
        FirstMandatoryBattleSessionState second = profile.CreateSessionState();
        first.PartyRoleIds[0] = 5;
        first.Roles[0].Equipment[0] = 999;
        first.Roles[0].Magics[0] = 999;
        first.Roles[0].Statuses[0] = 999;
        first.Roles[0].PrimaryExperience.Value = 999;
        first.Roles[0].HiddenExperience["health"].Count = 999;
        first.World.Leader.X = 999;
        first.World.SceneEntries[0].EnterEntry = 999;
        first.World.EventObjects[0].State = -1;

        Assert.Equal(0, second.PartyRoleIds[0]);
        Assert.Equal(196, second.Roles[0].Equipment[0]);
        Assert.Equal(296, second.Roles[0].Magics[0]);
        Assert.Equal(0, second.Roles[0].Statuses[0]);
        Assert.Equal(0, second.Roles[0].PrimaryExperience.Value);
        Assert.Equal(0, second.Roles[0].HiddenExperience["health"].Count);
        Assert.Equal(1248, second.World.Leader.X);
        Assert.Equal(0x0e97, second.World.SceneEntries[0].EnterEntry);
        Assert.Equal(2, second.World.EventObjects[0].State);
        Assert.Equal(196, profile.Roles[0].Equipment[0]);
        Assert.NotSame(first.Roles, second.Roles);
        Assert.NotSame(first.Roles[0].HiddenExperience, second.Roles[0].HiddenExperience);
        Assert.NotSame(first.World.EventObjects, second.World.EventObjects);
    }

    private static FirstMandatoryBattleProfile LoadContextForTest(byte[] contextBytes)
    {
        string hash = Convert.ToHexStringLower(SHA256.HashData(contextBytes));
        byte[] indexBytes = Encoding.UTF8.GetBytes(new JsonObject
        {
            ["formatVersion"] = 1,
            ["profile"] = FirstMandatoryBattleProfile.ProfileIdValue,
            ["contextFile"] = "battle-context.json",
            ["contextSha256"] = hash
        }.ToJsonString());
        return FirstMandatoryBattleProfile.Load(indexBytes,
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["battle-context.json"] = contextBytes });
    }

    private static T Typed<T>(BattleClosureEntry entry) where T : BattleClosureIdentity => Assert.IsType<T>(entry.Identity);
}
