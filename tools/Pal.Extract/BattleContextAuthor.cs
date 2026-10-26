using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Pal.Extract;

internal static class BattleContextAuthor
{
    internal const string Profile = "first-mandatory-battle-pre-trigger-v1";
    private const string SnapshotKind = "authored-precondition-not-natural-save";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex Sha256Pattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly string[] HiddenExperienceNames = ["health", "magic", "attack", "magicPower", "defense", "dexterity", "flee"];
    private static readonly string[] RequiredClosureIds =
    [
        "trigger-09c3-0a32", "battle-team-18", "role-hooks-random", "equipment-six-role", "growth-five-role",
        "dynamic-auto-0a36-0a3b", "failure-reload-todo", "battle-images", "dialogue-languages"
    ];

    internal static void Author(string input, string output)
    {
        string source = Path.GetFullPath(input);
        string destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
        ValidatePaths(source, destination);
        byte[] inputBytes = File.ReadAllBytes(source);
        ReadOnlySpan<byte> jsonBytes = inputBytes;
        if (jsonBytes.StartsWith(Encoding.UTF8.Preamble)) jsonBytes = jsonBytes[Encoding.UTF8.Preamble.Length..];
        string json;
        try { json = StrictUtf8.GetString(jsonBytes); }
        catch (DecoderFallbackException exception) { throw new InvalidDataException("Battle context input is not valid UTF-8.", exception); }
        using var document = JsonDocument.Parse(json);
        RejectDuplicateKeys(document.RootElement, "battle context");
        Validate(document.RootElement);

        var node = JsonNode.Parse(document.RootElement.GetRawText())!.AsObject();
        string context = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
        byte[] contextBytes = Encoding.UTF8.GetBytes(context);
        var index = new JsonObject
        {
            ["formatVersion"] = 1,
            ["profile"] = Profile,
            ["contextFile"] = "battle-context.json",
            ["contextSha256"] = Convert.ToHexStringLower(SHA256.HashData(contextBytes))
        };
        Directory.CreateDirectory(destination);
        File.WriteAllBytes(Path.Combine(destination, "battle-context.json"), contextBytes);
        File.WriteAllText(Path.Combine(destination, "index.json"), index.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
    }

    private static void Validate(JsonElement root)
    {
        RequireObject(root, "battle context");
        Keys(root, "battle context", "formatVersion", "profile", "identity", "provenance", "layout", "snapshot", "closure", "resources");
        if (Integer(root, "formatVersion") != 1 || Text(root, "profile") != Profile)
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
        HashSet<string> fieldSourcePaths = ValidateFieldSources(Array(provenance, "fieldSources"));

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
        JsonElement role1 = roles[1];
        Expect(Integer(role1, "level") == 5 && Integer(role1, "hp") == 28 && Integer(role1, "maxHp") == 240
            && Integer(role1, "mp") == 10 && Integer(role1, "maxMp") == 240,
            "role1 must retain base level 5, HP 28/240, and MP 10/240; the precondition must not fill role1.");
        ValidateWorld(Object(snapshot, "world"));
        var snapshotLeafPaths = new HashSet<string>(StringComparer.Ordinal);
        CollectLeafPaths(snapshot, "snapshot", snapshotLeafPaths);
        string[] uncovered = snapshotLeafPaths.Where(path => !fieldSourcePaths.Contains(path)).Order(StringComparer.Ordinal).ToArray();
        Expect(uncovered.Length == 0, $"provenance.fieldSources must identify every snapshot field; first missing path '{uncovered.FirstOrDefault()}'.");
        ValidateClosure(Object(root, "closure"));
        ValidateResources(Array(root, "resources"));
    }

    private static HashSet<string> ValidateFieldSources(JsonElement sources)
    {
        Expect(sources.GetArrayLength() > 0, "provenance.fieldSources must not be empty.");
        var paths = new HashSet<string>(StringComparer.Ordinal);
        bool hasRequiredMagicPrecondition = false;
        foreach (JsonElement source in sources.EnumerateArray())
        {
            RequireObject(source, "field source");
            Keys(source, "field source", "path", "sourceKind", "sourceAddress", "appliesTo");
            string path = Text(source, "path");
            Expect(path.Length > 0 && paths.Add(path), "provenance field paths must be nonempty and unique.");
            Expect(Text(source, "sourceKind") is "original-base" or "required-precondition" or "author-policy", "field sourceKind is invalid.");
            JsonElement sourceAddress = source.GetProperty("sourceAddress");
            if (sourceAddress.ValueKind != JsonValueKind.Null) Word(sourceAddress, "sourceAddress");
            string appliesTo = Text(source, "appliesTo");
            Expect(appliesTo.Length > 0, "field source appliesTo must not be empty.");
            if (path.StartsWith("snapshot.roles[0].magics[", StringComparison.Ordinal)
                && Text(source, "sourceKind") == "required-precondition" && sourceAddress.ValueKind == JsonValueKind.Number
                && Word(sourceAddress, "sourceAddress") == 0x0905 && appliesTo == "role0") hasRequiredMagicPrecondition = true;
        }
        foreach (string required in new[] { "snapshot.roles[1].hp", "snapshot.roles[1].mp" })
            Expect(paths.Contains(required), $"provenance.fieldSources is missing required path '{required}'.");
        Expect(hasRequiredMagicPrecondition,
            "provenance.fieldSources must identify the role0 magic learned at required precondition @0905.");
        return paths;
    }

    private static void CollectLeafPaths(JsonElement value, string path, HashSet<string> paths)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in value.EnumerateObject()) CollectLeafPaths(property.Value, path + "." + property.Name, paths);
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement item in value.EnumerateArray()) CollectLeafPaths(item, $"{path}[{index++}]", paths);
        }
        else paths.Add(path);
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
        Expect(Integer(leader, "x") >= 0 && Integer(leader, "y") >= 0 && Text(leader, "direction") is "north" or "east" or "south" or "west"
            && Integer(leader, "layer") >= 0 && Integer(leader, "phase") >= 0, "leader position, direction, layer, or phase is invalid.");
        JsonElement proof = Object(world, "searchProof");
        Keys(proof, "searchProof", "ownerId", "sampleIndex", "collisionPass");
        Expect(Integer(proof, "ownerId") == 28 && Integer(proof, "sampleIndex") is >= 0 and < 13 && Boolean(proof, "collisionPass"),
            "searchProof must record a legal 13-sample owner28 hit and collision pass.");
        JsonElement sceneEntries = Array(world, "sceneEntries");
        Expect(sceneEntries.GetArrayLength() > 0, "sceneEntries must not be empty.");
        foreach (JsonElement entry in sceneEntries.EnumerateArray())
        {
            Keys(entry, "scene entry", "sceneId", "enterEntry", "teleportEntry");
            PositiveWord(entry, "sceneId"); Word(entry.GetProperty("enterEntry"), "enterEntry"); Word(entry.GetProperty("teleportEntry"), "teleportEntry");
        }
        JsonElement objects = Array(world, "eventObjects");
        Expect(objects.GetArrayLength() > 0, "eventObjects must not be empty.");
        bool foundOwner = false;
        var owners = new HashSet<int>();
        foreach (JsonElement item in objects.EnumerateArray())
        {
            Keys(item, "event object", "ownerId", "x", "y", "direction", "state", "vanishTime", "triggerMode", "triggerEntry", "autoEntry", "autoIdleFrameCount", "spriteId", "spriteFrames", "layer");
            int id = PositiveWord(item, "ownerId");
            Expect(owners.Add(id), "eventObjects ownerId values must be unique.");
            Integer(item, "x"); Integer(item, "y"); Integer(item, "direction"); Integer(item, "state"); SignedWord(item.GetProperty("vanishTime"), "vanishTime");
            Word(item.GetProperty("triggerMode"), "triggerMode"); Word(item.GetProperty("triggerEntry"), "triggerEntry"); Word(item.GetProperty("autoEntry"), "autoEntry");
            Word(item.GetProperty("autoIdleFrameCount"), "autoIdleFrameCount"); Word(item.GetProperty("spriteId"), "spriteId"); Word(item.GetProperty("spriteFrames"), "spriteFrames"); Integer(item, "layer");
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

    private static void ValidateClosure(JsonElement closure)
    {
        Keys(closure, "closure", "entry", "entries");
        Expect(Text(closure, "entry") == "@09C3", "closure entry must be @09C3, not the internal @09FE battle instruction.");
        JsonElement entries = Array(closure, "entries");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement entry in entries.EnumerateArray())
        {
            Keys(entry, "closure entry", "id", "category", "identity", "ownerTicket", "status");
            string id = Text(entry, "id");
            Expect(id.Length > 0 && ids.Add(id), "closure ids must be nonempty and unique.");
            Expect(Text(entry, "category") is "command" or "resource" or "validation", $"closure entry '{id}' has an invalid category.");
            Expect(Text(entry, "identity").Length > 0, $"closure entry '{id}' has an empty identity.");
            Expect(Integer(entry, "ownerTicket") is >= 45 and <= 54, $"closure entry '{id}' has an invalid owner ticket.");
            Expect(Text(entry, "status") is "prepared" or "contract-only", $"closure entry '{id}' has an invalid status.");
        }
        foreach (string required in RequiredClosureIds) Expect(ids.Contains(required), $"closure is missing required entry '{required}'.");
    }

    private static void ValidateResources(JsonElement resources)
    {
        Expect(resources.GetArrayLength() > 0, "resources must not be empty.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement resource in resources.EnumerateArray())
        {
            Keys(resource, "resource", "id", "kind", "reference", "sha256", "ownerTicket");
            string id = Text(resource, "id"), reference = Text(resource, "reference"), hash = Text(resource, "sha256");
            Expect(id.Length > 0 && ids.Add(id), "resource ids must be nonempty and unique.");
            Expect(Text(resource, "kind").Length > 0, $"resource '{id}' kind must not be empty.");
            Expect(reference.Length > 0 && !Path.IsPathRooted(reference) && !reference.Contains("..", StringComparison.Ordinal) && reference.All(ch => ch != '\\'),
                $"resource '{id}' reference must be a safe forward-slash relative path.");
            Expect(Sha256Pattern.IsMatch(hash), $"resource '{id}' sha256 must be lowercase hexadecimal.");
            Expect(Integer(resource, "ownerTicket") is >= 45 and <= 54, $"resource '{id}' owner ticket is invalid.");
        }
    }

    private static void ValidatePaths(string source, string destination)
    {
        if (!File.Exists(source)) throw new InvalidDataException("Battle context input file does not exist.");
        RejectLinks(source, "Battle context input"); RejectLinks(destination, "Battle context output");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (source.Equals(destination, comparison)) throw new InvalidDataException("Battle context output must differ from input.");
        if (File.Exists(destination)) throw new InvalidDataException("Battle context output must be a new or empty directory, not a file.");
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new InvalidDataException("Battle context output directory is not empty; existing data will not be overwritten.");
    }

    private static void RejectLinks(string path, string context)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException($"{context}: links/reparse points are not supported."); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
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
    private static int Integer(JsonElement value, string name) => value.GetProperty(name).TryGetInt32(out int result) && value.GetProperty(name).ValueKind == JsonValueKind.Number ? result : throw new InvalidDataException($"{name} must be an integer.");
    private static bool Boolean(JsonElement value, string name) => value.GetProperty(name).ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetProperty(name).GetBoolean() : throw new InvalidDataException($"{name} must be a boolean.");
    private static int PositiveWord(JsonElement value, string name) { int result = Word(value.GetProperty(name), name); return result > 0 ? result : throw new InvalidDataException($"{name} must be greater than zero."); }
    private static int Word(JsonElement value, string context) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result) && result is >= 0 and <= 65535 ? result : throw new InvalidDataException($"{context} must be a WORD (0..65535).");
    private static int SignedWord(JsonElement value, string context) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result) && result is >= -32768 and <= 32767 ? result : throw new InvalidDataException($"{context} must be a signed WORD (-32768..32767).");
    private static int[] Words(JsonElement value, string context) => value.EnumerateArray().Select(item => Word(item, context)).ToArray();
    private static void Expect(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
