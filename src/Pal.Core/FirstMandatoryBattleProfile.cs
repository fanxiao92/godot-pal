using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Pal.Core;

public sealed class FirstMandatoryBattleProfile
{
    public string ProfileId => ProfileIdValue;
    public BattleContextIdentity Identity { get; }
    public BattleContextProvenance Provenance { get; }
    public BattleContextLayout Layout { get; }
    public IReadOnlyList<int> PartyRoleIds { get; }
    public IReadOnlyList<BattleRoleSnapshot> Roles { get; }
    public BattleWorldSnapshot World { get; }
    public BattleClosure Closure { get; }
    public IReadOnlyList<BattleResourceReference> Resources { get; }

    private FirstMandatoryBattleProfile(JsonElement root)
    {
        Identity = BattleModels.Identity(root.GetProperty("identity"));
        Provenance = BattleModels.Provenance(root.GetProperty("provenance"));
        Layout = BattleModels.Layout(root.GetProperty("layout"));
        JsonElement snapshot = root.GetProperty("snapshot");
        PartyRoleIds = BattleModels.ReadOnlyInts(snapshot.GetProperty("partyRoleIds"));
        Roles = System.Array.AsReadOnly(snapshot.GetProperty("roles").EnumerateArray().Select(BattleModels.Role).ToArray());
        World = BattleModels.World(snapshot.GetProperty("world"));
        Closure = BattleModels.Closure(root.GetProperty("closure"));
        Resources = System.Array.AsReadOnly(root.GetProperty("resources").EnumerateArray().Select(BattleModels.Resource).ToArray());
    }

    public static FirstMandatoryBattleProfile Load(byte[] indexBytes, IReadOnlyDictionary<string, byte[]> suppliedFiles)
    {
        ArgumentNullException.ThrowIfNull(indexBytes);
        ArgumentNullException.ThrowIfNull(suppliedFiles);
        using JsonDocument index = ParseStrict(indexBytes, "index.json");
        JsonElement root = index.RootElement;
        Keys(root, "index.json", "formatVersion", "profile", "contextFile", "contextSha256");
        if (Integer(root, "formatVersion") != 1 || Text(root, "profile") != ProfileIdValue)
            throw new InvalidDataException("index.json has an unsupported formatVersion or profile.");
        if (Text(root, "contextFile") != "battle-context.json")
            throw new InvalidDataException("index.json contextFile must be battle-context.json.");
        string expectedHash = Text(root, "contextSha256");
        if (!Sha256Pattern.IsMatch(expectedHash)) throw new InvalidDataException("index.json contextSha256 must be lowercase hexadecimal.");
        if (suppliedFiles.Count != 1 || !suppliedFiles.TryGetValue("battle-context.json", out byte[]? contextBytes) || contextBytes is null)
            throw new InvalidDataException("Supplied files must contain exactly battle-context.json.");
        string actualHash = Convert.ToHexStringLower(SHA256.HashData(contextBytes));
        if (!StringComparer.Ordinal.Equals(expectedHash, actualHash)) throw new InvalidDataException("battle-context.json exact-byte SHA-256 does not match index.json.");
        return LoadContext(contextBytes);
    }

    public static FirstMandatoryBattleProfile LoadContext(byte[] contextBytes)
    {
        ArgumentNullException.ThrowIfNull(contextBytes);
        using JsonDocument document = ParseStrict(contextBytes, "battle-context.json");
        ValidateSchema(document.RootElement);
        return new FirstMandatoryBattleProfile(document.RootElement);
    }

    public static byte[] ValidateAndCanonicalizeContext(byte[] contextBytes)
    {
        ArgumentNullException.ThrowIfNull(contextBytes);
        using JsonDocument document = ParseStrict(contextBytes, "battle-context.json");
        ValidateSchema(document.RootElement);
        JsonNode node = JsonNode.Parse(document.RootElement.GetRawText())!;
        return Encoding.UTF8.GetBytes(node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    public FirstMandatoryBattleSessionState CreateSessionState() => new(this);

    private static JsonDocument ParseStrict(byte[] bytes, string identity)
    {
        ReadOnlySpan<byte> payload = bytes;
        if (payload.StartsWith(Encoding.UTF8.Preamble)) payload = payload[Encoding.UTF8.Preamble.Length..];
        try
        {
            string text = StrictUtf8.GetString(payload);
            var document = JsonDocument.Parse(text);
            try { RejectDuplicateKeys(document.RootElement, identity); return document; }
            catch { document.Dispose(); throw; }
        }
        catch (DecoderFallbackException exception) { throw new InvalidDataException($"{identity} is not valid UTF-8.", exception); }
        catch (JsonException exception) { throw new InvalidDataException($"{identity} is not valid JSON.", exception); }
    }
    public const string ProfileIdValue = "first-mandatory-battle-pre-trigger-v1";
    private const string SnapshotKind = "authored-precondition-not-natural-save";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex Sha256Pattern = new("^[0-9a-f]{64}\\z", RegexOptions.CultureInvariant);
    private static readonly string[] HiddenExperienceNames = ["health", "magic", "attack", "magicPower", "defense", "dexterity", "flee"];
    private sealed record ClosureSpec(string Category, JsonObject Identity, int[] OwnerTickets, string[] ResourceIds);
    private sealed record ResourceSpec(string Kind, int[] OwnerTickets);
    private sealed record SourceSpec(string Kind, int? Address);

    private static readonly IReadOnlyDictionary<string, SourceSpec> FrozenSourceSpecs = CreateFrozenSourceSpecs();

    private static readonly IReadOnlyDictionary<string, ClosureSpec> ClosureSpecs = new Dictionary<string, ClosureSpec>(StringComparer.Ordinal)
    {
        ["trigger-09c3-0a32"] = new("command", new JsonObject { ["kind"] = "event-range", ["startAddress"] = 0x09c3, ["battleAddress"] = 0x09fe, ["wonStartAddress"] = 0x09ff, ["endAddress"] = 0x0a32 }, [52, 53], ["events"]),
        ["battle-team-18"] = new("command", new JsonObject { ["kind"] = "battle-command", ["commandAddress"] = 0x09fe, ["operands"] = new JsonArray(0x0012, 0xa073, 0x0000), ["enemyTeamId"] = 18, ["enemyObjectId"] = 495, ["enemyId"] = 97, ["enemyCount"] = 2, ["battlefieldId"] = 24, ["specialValue"] = "third-operand-zero-is-boss-no-flee", ["reverseEncoding"] = "encode-operands-from-semantic-fields" }, [46, 47, 51, 53], ["events", "battle-data"]),
        ["role-hooks-random"] = new("command", new JsonObject { ["kind"] = "role-script-ranges", ["dyingRoleId"] = 1, ["dyingStartAddress"] = 0xa96e, ["dyingEndAddress"] = 0xa987, ["friendDeathRoleId"] = 0, ["friendDeathStartAddress"] = 0xa9b5, ["friendDeathEndAddress"] = 0xa9d1, ["branchCoverage"] = "all-random-branches" }, [49], ["events", "battle-data", "dialogue-zh-cn", "dialogue-en", "glyphs"]),
        ["equipment-six-role"] = new("validation", new JsonObject { ["kind"] = "equipment-layout", ["roleCount"] = 6, ["slotCount"] = 6, ["dualAttackObjectId"] = 170, ["persistentEquipmentStatus"] = true }, [48], ["battle-data"]),
        ["growth-five-role"] = new("validation", new JsonObject { ["kind"] = "growth-layout", ["roleCount"] = 5, ["includesPrimaryExperience"] = true, ["includesHiddenExperience"] = true, ["includesRecoveryAndCleanup"] = true }, [50], ["battle-data"]),
        ["dynamic-auto-0a36-0a3b"] = new("command", new JsonObject { ["kind"] = "dynamic-auto-range", ["startAddress"] = 0x0a36, ["endAddress"] = 0x0a3b, ["effects"] = new JsonArray("movement", "direction", "hidden"), ["advance"] = "update-driven" }, [52], ["events"]),
        ["failure-reload-todo"] = new("command", new JsonObject { ["kind"] = "failure-boundary", ["lostEntryAddress"] = 0xa073, ["reloadCommand"] = 0x004e, ["behavior"] = "diagnose-and-stop-without-reload-retry-or-exploration" }, [51], ["events", "dialogue-zh-cn", "dialogue-en", "glyphs"]),
        ["battle-images"] = new("resource", new JsonObject { ["kind"] = "battle-image-set", ["enemyId"] = 97, ["enemyArchiveEntry"] = "ABC97", ["roleSpriteIds"] = new JsonArray("F0", "F1"), ["battlefieldId"] = 24, ["battlefieldArchiveEntry"] = "FBP24", ["includesLayoutAndEffectFrames"] = true }, [53], ["battle-images"]),
        ["dialogue-languages"] = new("resource", new JsonObject { ["kind"] = "localized-dialogue-set", ["languages"] = new JsonArray("zh-CN", "en"), ["contents"] = new JsonArray("messages", "names", "portraits", "glyphs", "ui") }, [49, 53], ["dialogue-zh-cn", "dialogue-en", "glyphs"])
    };

    private static readonly IReadOnlyDictionary<string, ResourceSpec> ResourceSpecs = new Dictionary<string, ResourceSpec>(StringComparer.Ordinal)
    {
        ["events"] = new("faithful-events", [52, 53]),
        ["battle-data"] = new("battle-data", [46, 47, 48, 49, 50, 51, 53]),
        ["battle-images"] = new("battle-images", [53]),
        ["dialogue-zh-cn"] = new("dialogue-language", [49, 53]),
        ["dialogue-en"] = new("dialogue-language", [49, 53]),
        ["glyphs"] = new("glyph-atlas", [49, 53])
    };

    private static void ValidateSchema(JsonElement root)
    {
        RequireObject(root, "battle context");
        Keys(root, "battle context", "formatVersion", "profile", "identity", "provenance", "layout", "snapshot", "closure", "resources");
        if (Integer(root, "formatVersion") != 1 || Text(root, "profile") != ProfileIdValue)
            throw new InvalidDataException("Battle context has an unsupported formatVersion or profile.");

        JsonElement identity = Object(root, "identity");
        Keys(identity, "identity", "snapshotKind", "sceneId", "mapId", "ownerId", "triggerAddress");
        if (Text(identity, "snapshotKind") != SnapshotKind) throw new InvalidDataException("Battle context is an authored precondition, not a natural save.");
        Expect(Integer(identity, "sceneId") == 2 && Integer(identity, "mapId") == 12 && Integer(identity, "ownerId") == 28 && Integer(identity, "triggerAddress") == 0x09c3,
            "Battle context identity must be scene 2 / map 12 / owner 28 / trigger @09C3.");

        JsonElement provenance = Object(root, "provenance");
        Keys(provenance, "provenance", "optionalGrinding", "playerEquipmentChanges", "fieldSources");
        Expect(!Boolean(provenance, "optionalGrinding") && !Boolean(provenance, "playerEquipmentChanges"),
            "Battle context must exclude optional grinding and player equipment changes.");
        Dictionary<string, JsonElement> fieldSources = ValidateFieldSources(Array(provenance, "fieldSources"));

        JsonElement layout = Object(root, "layout");
        Keys(layout, "layout", "equipmentRoleCount", "equipmentSlotCount", "levelUpMagicRoleCount");
        Expect(Integer(layout, "equipmentRoleCount") == 6 && Integer(layout, "equipmentSlotCount") == 6,
            "Battle context requires the Windows six-role equipment layout with six slots.");
        Expect(Integer(layout, "levelUpMagicRoleCount") == 5, "Battle context requires the Windows five-role level-up magic layout.");

        JsonElement snapshot = Object(root, "snapshot");
        Keys(snapshot, "snapshot", "partyRoleIds", "roles", "world");
        int[] party = Words(Array(snapshot, "partyRoleIds"), "partyRoleIds");
        Expect(party.SequenceEqual([0]), "The @09C3 pre-trigger party must contain role0 only; @09FC forms role0/role1 later.");
        JsonElement roles = Array(snapshot, "roles");
        Expect(roles.GetArrayLength() == 6, "Battle context must contain exactly six roles for equipment initialization.");
        int ordinal = 0;
        foreach (JsonElement role in roles.EnumerateArray()) ValidateRole(role, ordinal++);
        ValidateFrozenRoles(roles);
        ValidateWorld(Object(snapshot, "world"));
        var snapshotLeafPaths = new HashSet<string>(StringComparer.Ordinal);
        CollectLeafPaths(snapshot, "snapshot", snapshotLeafPaths);
        string[] uncovered = snapshotLeafPaths.Where(path => !fieldSources.ContainsKey(path)).Order(StringComparer.Ordinal).ToArray();
        Expect(uncovered.Length == 0, $"provenance.fieldSources must identify every snapshot field or empty container; first missing path '{uncovered.FirstOrDefault()}'.");
        string[] nonexistent = fieldSources.Keys.Where(path => !snapshotLeafPaths.Contains(path)).Order(StringComparer.Ordinal).ToArray();
        Expect(nonexistent.Length == 0, $"provenance.fieldSources contains nonexistent snapshot path '{nonexistent.FirstOrDefault()}'.");
        ValidateSourceSemantics(snapshot, fieldSources);
        HashSet<string> resourceIds = ValidateResources(Array(root, "resources"));
        ValidateClosure(Object(root, "closure"), resourceIds);
    }

    private static Dictionary<string, JsonElement> ValidateFieldSources(JsonElement sources)
    {
        Expect(sources.GetArrayLength() > 0, "provenance.fieldSources must not be empty.");
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonElement source in sources.EnumerateArray())
        {
            RequireObject(source, "field source");
            Keys(source, "field source", "path", "sourceKind", "sourceAddress", "appliesTo");
            string path = Text(source, "path");
            Expect(path.Length > 0 && result.TryAdd(path, source), "provenance field paths must be nonempty and unique.");
            string kind = Text(source, "sourceKind");
            Expect(kind is "original-base" or "required-precondition" or "author-policy", "field sourceKind is invalid.");
            JsonElement address = source.GetProperty("sourceAddress");
            if (kind == "required-precondition") Expect(address.ValueKind == JsonValueKind.Number, "required-precondition sourceAddress must be a WORD address.");
            else Expect(address.ValueKind == JsonValueKind.Null, $"{kind} sourceAddress must be null.");
            if (address.ValueKind != JsonValueKind.Null) Word(address, "sourceAddress");
            Expect(Text(source, "appliesTo").Length > 0, "field source appliesTo must not be empty.");
        }
        return result;
    }

    private static void CollectLeafPaths(JsonElement value, string path, HashSet<string> paths)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in value.EnumerateObject())
                if (!property.Name.StartsWith('_')) CollectLeafPaths(property.Value, path + "." + property.Name, paths);
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            if (value.GetArrayLength() == 0) paths.Add(path);
            else
            {
                int index = 0;
                foreach (JsonElement item in value.EnumerateArray()) CollectLeafPaths(item, $"{path}[{index++}]", paths);
            }
        }
        else paths.Add(path);
    }

    private static void ValidateSourceSemantics(JsonElement snapshot, IReadOnlyDictionary<string, JsonElement> sources)
    {
        var frozenSources = new Dictionary<string, SourceSpec>(FrozenSourceSpecs, StringComparer.Ordinal);
        int eventObjectIndex = 0;
        foreach (JsonElement item in snapshot.GetProperty("world").GetProperty("eventObjects").EnumerateArray())
        {
            if (Integer(item, "ownerId") == 28)
                frozenSources.Add($"snapshot.world.eventObjects[{eventObjectIndex}].state", new("required-precondition", 0x0936));
            eventObjectIndex++;
        }
        foreach ((string path, JsonElement source) in sources)
        {
            Expect(Text(source, "appliesTo") == ExpectedAppliesTo(snapshot, path), $"field source appliesTo does not match path '{path}'.");
            string kind = Text(source, "sourceKind");
            if (kind == "required-precondition")
                Expect(frozenSources.TryGetValue(path, out SourceSpec? required) && required.Kind == kind
                    && Word(source.GetProperty("sourceAddress"), "sourceAddress") == required.Address,
                    $"required-precondition source for '{path}' is not an approved path/address tuple.");
        }
        foreach ((string path, SourceSpec expected) in frozenSources)
        {
            JsonElement source = sources[path];
            JsonElement address = source.GetProperty("sourceAddress");
            Expect(Text(source, "sourceKind") == expected.Kind
                && (expected.Address is null ? address.ValueKind == JsonValueKind.Null : Word(address, "sourceAddress") == expected.Address),
                $"'{path}' must use its frozen {expected.Kind} provenance tuple.");
        }
    }

    private static IReadOnlyDictionary<string, SourceSpec> CreateFrozenSourceSpecs()
    {
        var result = new Dictionary<string, SourceSpec>(StringComparer.Ordinal);
        void Base(string path) => result.Add(path, new("original-base", null));
        foreach (string property in new[] { "level", "coveredByRoleId" })
        {
            Base($"snapshot.roles[0].{property}");
            Base($"snapshot.roles[1].{property}");
        }
        foreach (string property in new[] { "hp", "maxHp", "mp", "maxMp" }) Base($"snapshot.roles[1].{property}");
        for (int slot = 0; slot < 6; slot++)
        {
            Base($"snapshot.roles[0].equipment[{slot}]");
            Base($"snapshot.roles[1].equipment[{slot}]");
        }
        Base("snapshot.roles[0].magics[0]");
        for (int index = 0; index < 10; index++) Base($"snapshot.roles[1].magics[{index}]");
        result.Add("snapshot.roles[0].magics[1]", new("required-precondition", 0x0905));
        return result;
    }

    private static string ExpectedAppliesTo(JsonElement snapshot, string path)
    {
        Match role = Regex.Match(path, @"^snapshot\.roles\[(\d+)\]");
        if (role.Success) return "role" + role.Groups[1].Value;
        if (path.StartsWith("snapshot.partyRoleIds", StringComparison.Ordinal)) return "team-before-09C3";
        Match eventObject = Regex.Match(path, @"^snapshot\.world\.eventObjects\[(\d+)\]");
        if (eventObject.Success) return "owner" + snapshot.GetProperty("world").GetProperty("eventObjects")[int.Parse(eventObject.Groups[1].Value)].GetProperty("ownerId").GetInt32();
        Match scene = Regex.Match(path, @"^snapshot\.world\.sceneEntries\[(\d+)\]");
        if (scene.Success) return "scene" + snapshot.GetProperty("world").GetProperty("sceneEntries")[int.Parse(scene.Groups[1].Value)].GetProperty("sceneId").GetInt32();
        return "world";
    }

    private static void ValidateFrozenRoles(JsonElement roles)
    {
        JsonElement role0 = roles[0], role1 = roles[1];
        Expect(Integer(role0, "level") == 1 && Integer(role0, "hp") == 150 && Integer(role0, "maxHp") == 150
            && Integer(role0, "mp") == 100 && Integer(role0, "maxMp") == 100, "role0 must retain frozen level 1, HP 150/150, and MP 100/100.");
        Expect(Words(Array(role0, "equipment"), "role0 equipment").SequenceEqual([196, 225, 208, 166, 235, 249]), "role0 equipment must match the frozen base equipment.");
        Expect(Words(Array(role0, "magics"), "role0 magics").SequenceEqual([296, 345]), "role0 magics must be base 296 plus required-precondition 345.");
        Expect(Integer(role0, "coveredByRoleId") == 2, "role0 must remain covered by role2.");
        Expect(Integer(role1, "level") == 5 && Integer(role1, "hp") == 28 && Integer(role1, "maxHp") == 240
            && Integer(role1, "mp") == 10 && Integer(role1, "maxMp") == 240, "role1 must retain base level 5, HP 28/240, and MP 10/240; the precondition must not fill role1.");
        Expect(Words(Array(role1, "equipment"), "role1 equipment").SequenceEqual([197, 0, 210, 170, 237, 248]), "role1 equipment must match the frozen base equipment including hand object 170.");
        Expect(Words(Array(role1, "magics"), "role1 magics").SequenceEqual([312, 316, 321, 327, 332, 306, 297, 309, 303, 307]), "role1 magics must match the frozen base list.");
        Expect(Integer(role1, "coveredByRoleId") == 0, "role1 must remain covered by role0.");
    }

    private static void ValidateRole(JsonElement role, int expectedId)
    {
        RequireObject(role, $"role {expectedId}");
        Keys(role, $"role {expectedId}", "roleId", "level", "hp", "maxHp", "mp", "maxMp", "primaryExperience", "hiddenExperience", "equipment", "magics", "statuses", "coveredByRoleId");
        Expect(Integer(role, "roleId") == expectedId, "roles must be ordered and identified 0 through 5.");
        int level = PositiveWord(role, "level"), maxHp = PositiveWord(role, "maxHp"), maxMp = Word(role.GetProperty("maxMp"), "maxMp");
        int hp = Word(role.GetProperty("hp"), "hp"), mp = Word(role.GetProperty("mp"), "mp");
        Expect(hp <= maxHp && mp <= maxMp, $"role {expectedId} current HP/MP cannot exceed maxima.");
        ValidateExperience(Object(role, "primaryExperience"), level, $"role {expectedId} primaryExperience");
        JsonElement hidden = Object(role, "hiddenExperience");
        Keys(hidden, $"role {expectedId} hiddenExperience", HiddenExperienceNames);
        foreach (string name in HiddenExperienceNames) ValidateExperience(Object(hidden, name), level, $"role {expectedId} hiddenExperience.{name}");
        Expect(Words(Array(role, "equipment"), "equipment").Length == 6, $"role {expectedId} equipment must contain six slots.");
        int[] magics = Words(Array(role, "magics"), "magics");
        Expect(magics.Length <= 32 && magics.Distinct().Count() == magics.Length && magics.All(value => value != 0), $"role {expectedId} magics must be unique nonzero WORD ids (maximum 32).");
        Expect(Words(Array(role, "statuses"), "statuses").Length == 9, $"role {expectedId} statuses must contain all nine status slots.");
        int coveredBy = Word(role.GetProperty("coveredByRoleId"), "coveredByRoleId");
        Expect(coveredBy < 6, $"role {expectedId} coveredByRoleId must identify one of six roles.");
    }

    private static void ValidateExperience(JsonElement experience, int roleLevel, string context)
    {
        Keys(experience, context, "value", "level", "count");
        Word(experience.GetProperty("value"), context + ".value");
        Expect(PositiveWord(experience, "level") == roleLevel, context + ".level must match the role level in this authored zero-action baseline.");
        Word(experience.GetProperty("count"), context + ".count");
    }

    private static void ValidateWorld(JsonElement world)
    {
        Keys(world, "world", "sceneId", "mapId", "paletteId", "nightPalette", "musicId", "battleMusicId", "leader", "searchProof", "sceneEntries", "eventObjects");
        Expect(Integer(world, "sceneId") == 2 && Integer(world, "mapId") == 12, "snapshot world must identify scene 2 / map 12.");
        Word(world.GetProperty("paletteId"), "paletteId"); Boolean(world, "nightPalette"); Word(world.GetProperty("musicId"), "musicId"); Word(world.GetProperty("battleMusicId"), "battleMusicId");
        JsonElement leader = Object(world, "leader");
        Keys(leader, "leader", "x", "y", "direction", "layer", "phase");
        SignedWord(leader.GetProperty("x"), "leader.x"); SignedWord(leader.GetProperty("y"), "leader.y");
        Expect(Text(leader, "direction") is "north" or "east" or "south" or "west", "leader direction is invalid.");
        Word(leader.GetProperty("layer"), "leader.layer");
        Expect(Integer(leader, "phase") is >= 0 and <= 3, "leader phase must be 0..3.");
        JsonElement proof = Object(world, "searchProof");
        Keys(proof, "searchProof", "ownerId", "sampleIndex", "collisionPass");
        Expect(Integer(proof, "ownerId") == 28 && Integer(proof, "sampleIndex") is >= 0 and < 13 && Boolean(proof, "collisionPass"),
            "searchProof must record a legal 13-sample owner28 hit and collision pass.");
        JsonElement sceneEntries = Array(world, "sceneEntries");
        Expect(sceneEntries.GetArrayLength() > 0, "sceneEntries must not be empty.");
        var sceneIds = new HashSet<int>();
        foreach (JsonElement entry in sceneEntries.EnumerateArray())
        {
            Keys(entry, "scene entry", "sceneId", "enterEntry", "teleportEntry");
            int sceneId = PositiveWord(entry, "sceneId");
            Expect(sceneIds.Add(sceneId), "sceneEntries sceneId values must be unique.");
            Word(entry.GetProperty("enterEntry"), "enterEntry"); Word(entry.GetProperty("teleportEntry"), "teleportEntry");
        }
        Expect(sceneIds.Contains(2), "sceneEntries must include the selected scene 2.");
        JsonElement objects = Array(world, "eventObjects");
        Expect(objects.GetArrayLength() > 0, "eventObjects must not be empty.");
        bool foundOwner = false;
        var owners = new HashSet<int>();
        foreach (JsonElement item in objects.EnumerateArray())
        {
            Keys(item, "event object", "ownerId", "x", "y", "direction", "state", "vanishTime", "triggerMode", "triggerEntry", "autoEntry", "autoIdleFrameCount", "spriteId", "spriteFrames", "currentFrame", "scriptIdleFrameCount", "spritePtrOffset", "autoSpriteFrames", "layer");
            int id = PositiveWord(item, "ownerId");
            Expect(owners.Add(id), "eventObjects ownerId values must be unique.");
            Word(item.GetProperty("x"), "x"); Word(item.GetProperty("y"), "y"); Word(item.GetProperty("direction"), "direction"); SignedWord(item.GetProperty("state"), "state"); SignedWord(item.GetProperty("vanishTime"), "vanishTime");
            Word(item.GetProperty("triggerMode"), "triggerMode"); Word(item.GetProperty("triggerEntry"), "triggerEntry"); Word(item.GetProperty("autoEntry"), "autoEntry");
            Word(item.GetProperty("autoIdleFrameCount"), "autoIdleFrameCount"); Word(item.GetProperty("spriteId"), "spriteId"); Word(item.GetProperty("spriteFrames"), "spriteFrames");
            Word(item.GetProperty("currentFrame"), "currentFrame"); Word(item.GetProperty("scriptIdleFrameCount"), "scriptIdleFrameCount"); Word(item.GetProperty("spritePtrOffset"), "spritePtrOffset"); Word(item.GetProperty("autoSpriteFrames"), "autoSpriteFrames"); SignedWord(item.GetProperty("layer"), "layer");
            if (id == 28)
            {
                foundOwner = true;
                Expect(Integer(item, "x") == 1280 && Integer(item, "y") == 256 && Integer(item, "state") == 2 && Integer(item, "triggerMode") == 1
                    && Integer(item, "triggerEntry") == 0x09c3 && Integer(item, "autoEntry") == 0 && Integer(item, "spriteId") == 210,
                    "owner28 must retain the enabled required-precondition identity at (1280,256), trigger @09C3, no auto, mode 1, sprite 210.");
            }
        }
        Expect(foundOwner, "eventObjects must include owner28.");
    }

    private static void ValidateClosure(JsonElement closure, IReadOnlySet<string> resourceIds)
    {
        Keys(closure, "closure", "entry", "entries");
        Expect(Text(closure, "entry") == "@09C3", "closure entry must be @09C3, not the internal @09FE battle instruction.");
        JsonElement entries = Array(closure, "entries");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement entry in entries.EnumerateArray())
        {
            Keys(entry, "closure entry", "id", "category", "identity", "ownerTickets", "status", "resourceIds");
            string id = Text(entry, "id");
            Expect(id.Length > 0 && ids.Add(id), $"closure id '{id}' is empty or duplicated.");
            if (!ClosureSpecs.TryGetValue(id, out ClosureSpec? spec)) throw new InvalidDataException($"closure id '{id}' is unknown.");
            Expect(Text(entry, "category") == spec.Category, $"closure entry '{id}' category is not the frozen category.");
            JsonElement identity = Object(entry, "identity");
            using JsonDocument expectedIdentity = JsonDocument.Parse(spec.Identity.ToJsonString());
            Expect(DeepEqualsIgnoringComments(identity, expectedIdentity.RootElement), $"closure entry '{id}' identity is not the frozen structured identity.");
            Expect(ExactWordSet(Array(entry, "ownerTickets"), spec.OwnerTickets, $"closure entry '{id}' ownerTickets"), $"closure entry '{id}' ownerTickets do not match the exact owner set.");
            Expect(Text(entry, "status") == "contract-only", $"closure entry '{id}' must remain contract-only until downstream evidence is delivered.");
            string[] links = Array(entry, "resourceIds").EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new InvalidDataException($"closure entry '{id}' resourceIds must be strings.")).ToArray();
            Expect(links.SequenceEqual(spec.ResourceIds), $"closure entry '{id}' resource links do not match the frozen matrix.");
            Expect(links.All(resourceIds.Contains), $"closure entry '{id}' links a missing resource.");
        }
        string? missing = ClosureSpecs.Keys.FirstOrDefault(id => !ids.Contains(id));
        Expect(missing is null && ids.Count == ClosureSpecs.Count, $"closure is missing required entry '{missing}'.");
    }

    private static HashSet<string> ValidateResources(JsonElement resources)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement resource in resources.EnumerateArray())
        {
            Keys(resource, "resource", "id", "kind", "reference", "sha256", "ownerTickets");
            string id = Text(resource, "id"), reference = Text(resource, "reference"), hash = Text(resource, "sha256");
            Expect(id.Length > 0 && ids.Add(id), $"resource id '{id}' is empty or duplicated.");
            if (!ResourceSpecs.TryGetValue(id, out ResourceSpec? spec)) throw new InvalidDataException($"resource id '{id}' is unknown.");
            Expect(Text(resource, "kind") == spec.Kind, $"resource '{id}' kind does not match the frozen kind.");
            Expect(reference.Length > 0 && reference[0] != '/' && !reference.Contains("..", StringComparison.Ordinal)
                && reference.All(ch => ch is not '\\' and not ':'),
                $"resource '{id}' reference must be a safe forward-slash relative path.");
            Expect(Sha256Pattern.IsMatch(hash), $"resource '{id}' sha256 must be lowercase hexadecimal.");
            Expect(ExactWordSet(Array(resource, "ownerTickets"), spec.OwnerTickets, $"resource '{id}' ownerTickets"), $"resource '{id}' ownerTickets do not match the exact owner set.");
        }
        Expect(ids.SetEquals(ResourceSpecs.Keys), "resources must contain exactly the frozen resource set.");
        return ids;
    }

    private static bool DeepEqualsIgnoringComments(JsonElement actual, JsonElement expected)
    {
        if (actual.ValueKind != expected.ValueKind) return false;
        if (actual.ValueKind == JsonValueKind.Object)
        {
            JsonProperty[] actualProperties = actual.EnumerateObject().Where(property => !property.Name.StartsWith('_')).ToArray();
            JsonProperty[] expectedProperties = expected.EnumerateObject().ToArray();
            return actualProperties.Length == expectedProperties.Length
                && expectedProperties.All(property => actual.TryGetProperty(property.Name, out JsonElement value)
                    && DeepEqualsIgnoringComments(value, property.Value));
        }
        if (actual.ValueKind == JsonValueKind.Array)
        {
            JsonElement[] actualItems = actual.EnumerateArray().ToArray(), expectedItems = expected.EnumerateArray().ToArray();
            return actualItems.Length == expectedItems.Length
                && actualItems.Zip(expectedItems).All(pair => DeepEqualsIgnoringComments(pair.First, pair.Second));
        }
        return JsonElement.DeepEquals(actual, expected);
    }

    private static void RejectDuplicateKeys(JsonElement element, string context)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new InvalidDataException($"{context}: duplicate JSON key '{property.Name}'.");
                RejectDuplicateKeys(property.Value, context + "." + property.Name);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (JsonElement item in element.EnumerateArray()) RejectDuplicateKeys(item, context);
    }

    private static void Keys(JsonElement element, string context, params string[] names)
    {
        RequireObject(element, context);
        var allowed = names.ToHashSet(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
            if (!allowed.Contains(property.Name) && !property.Name.StartsWith('_')) throw new InvalidDataException($"{context}: unknown field '{property.Name}'.");
        foreach (string name in names) if (!element.TryGetProperty(name, out _)) throw new InvalidDataException($"{context}: missing field '{name}'.");
    }

    private static void RequireObject(JsonElement element, string context) { if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"{context} must be an object."); }
    private static JsonElement Object(JsonElement value, string name) { JsonElement result = value.GetProperty(name); RequireObject(result, name); return result; }
    private static JsonElement Array(JsonElement value, string name) { JsonElement result = value.GetProperty(name); return result.ValueKind == JsonValueKind.Array ? result : throw new InvalidDataException($"{name} must be an array."); }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).ValueKind == JsonValueKind.String ? value.GetProperty(name).GetString()! : throw new InvalidDataException($"{name} must be a string.");
    private static int Integer(JsonElement value, string name) => value.GetProperty(name).ValueKind == JsonValueKind.Number && value.GetProperty(name).TryGetInt32(out int result) ? result : throw new InvalidDataException($"{name} must be an integer.");
    private static bool Boolean(JsonElement value, string name) => value.GetProperty(name).ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetProperty(name).GetBoolean() : throw new InvalidDataException($"{name} must be a boolean.");
    private static int PositiveWord(JsonElement value, string name) { int result = Word(value.GetProperty(name), name); return result > 0 ? result : throw new InvalidDataException($"{name} must be greater than zero."); }
    private static int Word(JsonElement value, string context) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result) && result is >= 0 and <= 65535 ? result : throw new InvalidDataException($"{context} must be a WORD (0..65535).");
    private static int SignedWord(JsonElement value, string context) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result) && result is >= -32768 and <= 32767 ? result : throw new InvalidDataException($"{context} must be a signed WORD (-32768..32767).");
    private static int[] Words(JsonElement value, string context) => value.EnumerateArray().Select(item => Word(item, context)).ToArray();
    private static bool ExactWordSet(JsonElement value, int[] expected, string context)
    {
        int[] actual = Words(value, context);
        return actual.Length == actual.Distinct().Count() && actual.ToHashSet().SetEquals(expected);
    }
    private static void Expect(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
