using System.Text.Json;
using System.Text.Unicode;

namespace Pal.Core.Save;

/// <summary>M6-C leaf #223 (parent #148 Decision 18–20): the versioned, fail-closed v1
/// player-save JSON codec over the frozen #221 snapshot contract (docs/milestones/m6/
/// m6c-save-matrix.md storage domains). <see cref="EncodeToUtf8"/> writes the snapshot as
/// deterministic compact UTF-8 JSON — frozen field order, Ordinal-sorted hidden experience
/// tracks, no BOM, an absent setMusic word omitted. <see cref="Decode"/> is fail-closed and
/// applies nothing partially: it enforces the 1 MiB pre-decode byte cap and strict UTF-8
/// before parsing, rejects nesting beyond <see cref="MaxJsonDepth"/>, duplicate keys,
/// unknown/missing fields, wrong types, wrong dimensions and over-cap collections at the
/// decode stage, requires <c>$.formatVersion</c> to be exactly 1 (unknown versions never
/// migrate), binds the save to the prepared gameplay content identity with an Ordinal exact
/// comparison, then constructs <see cref="PlayerSaveV1"/> whose frozen constructor re-validates
/// every value domain. The codec stays pure bytes/text: no FileAccess, no file system, no
/// menu or restore wiring — the c4 platform adapter owns <c>user://</c> I/O. The content
/// identity references gameplay content only (matrix D1, Decision 15): language packages are
/// never part of the identity, so a different display language never misjudges gameplay
/// identity.</summary>
public static class PlayerSaveJson
{
    /// <summary>The frozen maximum JSON nesting depth of a legal v1 save. The deepest legal
    /// path is <c>$.roles[i].hiddenExperience.&lt;track&gt;</c> = five nested containers;
    /// deeper files reject during parse (#148 Decision 20) with no unbounded allocation.</summary>
    public const int MaxJsonDepth = 5;

    private static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = MaxJsonDepth };

    private static readonly string[] RootFields =
        { "formatVersion", "saveSlot", "savedTimes", "contentIdentity", "roles", "partyRoleIds", "inventory", "cash", "world" };
    private static readonly string[] RoleFields =
    {
        "roleId", "level", "hp", "maxHp", "mp", "maxMp", "primaryExperience", "hiddenExperience",
        "equipment", "magics", "coveredByRoleId", "attack", "magicPower", "defense", "dexterity", "fleeRate",
    };
    private static readonly string[] ExperienceFields = { "value", "level", "count" };
    private static readonly string[] InventorySlotFields = { "itemId", "amount", "amountInUse" };
    private static readonly string[] LeaderFields = { "x", "y", "direction", "layer", "stepPhase" };
    private static readonly string[] ObjectFields =
    {
        "objectId", "x", "y", "direction", "currentFrame", "state", "vanishTime",
        "triggerMode", "triggerEntry", "autoEntry", "autoIdleCount", "triggerIdleCount",
    };
    private static readonly string[] SceneScriptFields = { "sceneId", "onEnterEntry", "onTeleportEntry" };
    private static readonly string[] ObjectScriptFields = { "objectId", "slotIndex", "entry" };
    private static readonly string[] MusicFields = { "musicId", "mode" };
    private static readonly string[] WorldFields = { "sceneId", "leader", "objects", "sceneScripts", "objectScripts", "battlefield" };
    private static readonly string[] WorldOptionalFields = { "music" };

    /// <summary>Encodes the frozen snapshot as deterministic compact UTF-8 JSON (no BOM) in the
    /// frozen field order; hidden experience tracks are written in Ordinal key order so two
    /// equal snapshots always produce identical bytes. The snapshot is already validated at
    /// construction, so encoding never fails on domain values.</summary>
    public static byte[] EncodeToUtf8(PlayerSaveV1 save)
    {
        ArgumentNullException.ThrowIfNull(save);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("formatVersion", save.FormatVersion);
            writer.WriteNumber("saveSlot", save.SaveSlot);
            writer.WriteNumber("savedTimes", save.SavedTimes);
            writer.WriteString("contentIdentity", save.ContentIdentity);
            writer.WriteStartArray("roles");
            foreach (PlayerSaveRole role in save.Roles)
            {
                writer.WriteStartObject();
                writer.WriteNumber("roleId", role.RoleId);
                writer.WriteNumber("level", role.Level);
                writer.WriteNumber("hp", role.Hp);
                writer.WriteNumber("maxHp", role.MaxHp);
                writer.WriteNumber("mp", role.Mp);
                writer.WriteNumber("maxMp", role.MaxMp);
                WriteExperience(writer, "primaryExperience", role.PrimaryExperience);
                writer.WriteStartObject("hiddenExperience");
                foreach (string track in role.HiddenExperience.Keys.OrderBy(name => name, StringComparer.Ordinal))
                    WriteExperience(writer, track, role.HiddenExperience[track]);
                writer.WriteEndObject();
                writer.WriteStartArray("equipment");
                foreach (int word in role.Equipment) writer.WriteNumberValue(word);
                writer.WriteEndArray();
                writer.WriteStartArray("magics");
                foreach (int word in role.Magics) writer.WriteNumberValue(word);
                writer.WriteEndArray();
                writer.WriteNumber("coveredByRoleId", role.CoveredByRoleId);
                writer.WriteNumber("attack", role.Attack);
                writer.WriteNumber("magicPower", role.MagicPower);
                writer.WriteNumber("defense", role.Defense);
                writer.WriteNumber("dexterity", role.Dexterity);
                writer.WriteNumber("fleeRate", role.FleeRate);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("partyRoleIds");
            foreach (int roleId in save.PartyRoleIds) writer.WriteNumberValue(roleId);
            writer.WriteEndArray();
            writer.WriteStartArray("inventory");
            foreach (PlayerSaveInventorySlot slot in save.Inventory)
            {
                writer.WriteStartObject();
                writer.WriteNumber("itemId", slot.ItemId);
                writer.WriteNumber("amount", slot.Amount);
                writer.WriteNumber("amountInUse", slot.AmountInUse);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteNumber("cash", save.Cash);
            writer.WriteStartObject("world");
            writer.WriteNumber("sceneId", save.World.SceneId);
            writer.WriteStartObject("leader");
            writer.WriteNumber("x", save.World.Leader.X);
            writer.WriteNumber("y", save.World.Leader.Y);
            writer.WriteNumber("direction", save.World.Leader.Direction);
            writer.WriteNumber("layer", save.World.Leader.Layer);
            writer.WriteNumber("stepPhase", save.World.Leader.StepPhase);
            writer.WriteEndObject();
            writer.WriteStartArray("objects");
            foreach (PlayerSaveObject obj in save.World.Objects)
            {
                writer.WriteStartObject();
                writer.WriteNumber("objectId", obj.ObjectId);
                writer.WriteNumber("x", obj.X);
                writer.WriteNumber("y", obj.Y);
                writer.WriteNumber("direction", obj.Direction);
                writer.WriteNumber("currentFrame", obj.CurrentFrame);
                writer.WriteNumber("state", obj.State);
                writer.WriteNumber("vanishTime", obj.VanishTime);
                writer.WriteNumber("triggerMode", obj.TriggerMode);
                writer.WriteNumber("triggerEntry", obj.TriggerEntry);
                writer.WriteNumber("autoEntry", obj.AutoEntry);
                writer.WriteNumber("autoIdleCount", obj.AutoIdleCount);
                writer.WriteNumber("triggerIdleCount", obj.TriggerIdleCount);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("sceneScripts");
            foreach (PlayerSaveSceneScript sceneScript in save.World.SceneScripts)
            {
                writer.WriteStartObject();
                writer.WriteNumber("sceneId", sceneScript.SceneId);
                writer.WriteNumber("onEnterEntry", sceneScript.OnEnterEntry);
                writer.WriteNumber("onTeleportEntry", sceneScript.OnTeleportEntry);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("objectScripts");
            foreach (PlayerSaveObjectScript objectScript in save.World.ObjectScripts)
            {
                writer.WriteStartObject();
                writer.WriteNumber("objectId", objectScript.ObjectId);
                writer.WriteNumber("slotIndex", objectScript.SlotIndex);
                writer.WriteNumber("entry", objectScript.Entry);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteNumber("battlefield", save.World.Battlefield);
            if (save.World.Music is { } music)
            {
                writer.WriteStartObject("music");
                writer.WriteNumber("musicId", music.MusicId);
                writer.WriteNumber("mode", music.Mode);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }

    /// <summary>Strictly decodes and validates a v1 save from its UTF-8 bytes against the
    /// prepared gameplay <paramref name="expectedContentIdentity"/>. Every violation rejects
    /// whole-file with a <c>$.</c>-locatable <see cref="InvalidDataException"/>: nothing is
    /// partially applied, no version is migrated or defaulted and no value is clamped.</summary>
    public static PlayerSaveV1 Decode(ReadOnlyMemory<byte> utf8Bytes, string expectedContentIdentity)
    {
        if (utf8Bytes.Length > PlayerSaveContract.MaxFileBytes)
            throw new InvalidDataException(
                $"$ : the save is {utf8Bytes.Length} bytes, over the frozen {PlayerSaveContract.MaxFileBytes}-byte pre-decode cap; rejected before decoding.");
        if (!Utf8.IsValid(utf8Bytes.Span))
            throw new InvalidDataException("$ : the save bytes are not valid UTF-8; rejected before decoding.");
        if (string.IsNullOrWhiteSpace(expectedContentIdentity)
            || expectedContentIdentity.Length > PlayerSaveContract.MaxContentIdentityChars)
            throw new InvalidDataException(
                "$.contentIdentity: the expected gameplay content identity is missing or malformed; a save is never validated against an empty context.");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8Bytes, DocumentOptions);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                $"$ : the save is not well-formed JSON within depth {MaxJsonDepth}: {error.Message}", error);
        }
        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("$ : the save root must be a JSON object.");

            // Header first (#148 Decision 18; matrix restore step 1): a missing or unknown
            // version rejects before any field validation and never migrates.
            if (!root.TryGetProperty("formatVersion", out JsonElement versionElement))
                throw new InvalidDataException("$.formatVersion: the field is required; a missing version never migrates.");
            int formatVersion = Integer(versionElement, "$.formatVersion");
            if (formatVersion != PlayerSaveContract.CurrentVersion)
                throw new InvalidDataException(
                    $"$.formatVersion: expected exactly {PlayerSaveContract.CurrentVersion}; version {formatVersion} is unknown and never migrates.");
            RejectDuplicateKeys(root, "$");
            Keys(root, "$", RootFields);

            int saveSlot = Integer(root.GetProperty("saveSlot"), "$.saveSlot");
            int savedTimes = Integer(root.GetProperty("savedTimes"), "$.savedTimes");
            string contentIdentity = Text(root.GetProperty("contentIdentity"), "$.contentIdentity");
            if (!string.Equals(contentIdentity, expectedContentIdentity, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"$.contentIdentity: the save is bound to gameplay content '{contentIdentity}', which is not the prepared context '{expectedContentIdentity}'.");

            JsonElement rolesElement = ArrayField(root, "roles", "$", PlayerSaveContract.RoleCount);
            var roles = new List<PlayerSaveRole>(rolesElement.GetArrayLength());
            int roleIndex = 0;
            foreach (JsonElement roleElement in rolesElement.EnumerateArray())
                roles.Add(Role(roleElement, $"$.roles[{roleIndex++}]"));
            if (roles.Count != PlayerSaveContract.RoleCount)
                throw new InvalidDataException(
                    $"$.roles: expected exactly {PlayerSaveContract.RoleCount} role records, found {roles.Count}.");

            int[] partyRoleIds = WordArrayField(root, "partyRoleIds", "$", 1, PlayerSaveContract.MaxPartyMembers);

            JsonElement inventoryElement = ArrayField(root, "inventory", "$", PlayerSaveContract.InventoryCapacity);
            var inventory = new List<PlayerSaveInventorySlot>(inventoryElement.GetArrayLength());
            int inventoryIndex = 0;
            foreach (JsonElement slotElement in inventoryElement.EnumerateArray())
            {
                string path = $"$.inventory[{inventoryIndex++}]";
                Keys(slotElement, path, InventorySlotFields);
                inventory.Add(new PlayerSaveInventorySlot(
                    Integer(slotElement.GetProperty("itemId"), path + ".itemId"),
                    Integer(slotElement.GetProperty("amount"), path + ".amount"),
                    Integer(slotElement.GetProperty("amountInUse"), path + ".amountInUse")));
            }

            JsonElement cashElement = root.GetProperty("cash");
            if (cashElement.ValueKind != JsonValueKind.Number || !cashElement.TryGetInt64(out long cash)
                || cash is < 0 or > uint.MaxValue)
                throw new InvalidDataException("$.cash: expected an integer in 0..4294967295 (DWORD).");

            return new PlayerSaveV1(formatVersion, saveSlot, savedTimes, contentIdentity, roles, partyRoleIds,
                inventory, (uint)cash, World(root.GetProperty("world")));
        }
    }

    private static void WriteExperience(Utf8JsonWriter writer, string name, PlayerSaveExperience experience)
    {
        writer.WriteStartObject(name);
        writer.WriteNumber("value", experience.Value);
        writer.WriteNumber("level", experience.Level);
        writer.WriteNumber("count", experience.Count);
        writer.WriteEndObject();
    }

    private static void RejectDuplicateKeys(JsonElement element, string context)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name))
                    throw new InvalidDataException($"{context}: duplicate JSON key '{property.Name}'.");
                RejectDuplicateKeys(property.Value, context + "." + property.Name);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement item in element.EnumerateArray())
                RejectDuplicateKeys(item, $"{context}[{index++}]");
        }
    }

    /// <summary>Rejects unknown fields and missing required fields; for this self-produced
    /// format an unknown key is corruption or a foreign version, never a comment.</summary>
    private static void Keys(JsonElement element, string context, string[] required, string[]? optional = null)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{context}: expected a JSON object.");
        foreach (JsonProperty property in element.EnumerateObject())
            if (!required.Contains(property.Name, StringComparer.Ordinal)
                && (optional is null || !optional.Contains(property.Name, StringComparer.Ordinal)))
                throw new InvalidDataException($"{context}: unknown field '{property.Name}'.");
        foreach (string key in required)
            if (!element.TryGetProperty(key, out _))
                throw new InvalidDataException($"{context}: the field '{key}' is required.");
    }

    private static int Integer(JsonElement element, string path) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int value)
            ? value
            : throw new InvalidDataException($"{path}: expected an integer.");

    private static string Text(JsonElement element, string path) =>
        element.ValueKind == JsonValueKind.String
            ? element.GetString()!
            : throw new InvalidDataException($"{path}: expected a string.");

    private static short Short(JsonElement element, string path) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int value)
            && value is >= short.MinValue and <= short.MaxValue
            ? (short)value
            : throw new InvalidDataException($"{path}: expected a signed SHORT (-32768..32767).");

    private static JsonElement ObjectField(JsonElement parent, string name, string parentPath)
    {
        JsonElement element = parent.GetProperty(name);
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{parentPath}.{name}: expected a JSON object.");
        return element;
    }

    /// <summary>Bounded array field: over-cap collections reject here, at the decode stage,
    /// so an oversized file never turns into an unbounded build (#148 Decision 20).</summary>
    private static JsonElement ArrayField(JsonElement parent, string name, string parentPath, int maxCount)
    {
        string path = parentPath + "." + name;
        JsonElement element = parent.GetProperty(name);
        if (element.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{path}: expected a JSON array.");
        if (element.GetArrayLength() > maxCount)
            throw new InvalidDataException($"{path}: expected at most {maxCount} entries, found {element.GetArrayLength()}.");
        return element;
    }

    private static int[] WordArrayField(JsonElement parent, string name, string parentPath, int minCount, int maxCount)
    {
        JsonElement element = ArrayField(parent, name, parentPath, maxCount);
        int length = element.GetArrayLength();
        if (length < minCount)
            throw new InvalidDataException(
                $"{parentPath}.{name}: expected {(minCount == maxCount ? $"exactly {minCount}" : $"at least {minCount}")} entries, found {length}.");
        var words = new int[length];
        int index = 0;
        foreach (JsonElement item in element.EnumerateArray())
        {
            words[index] = Integer(item, $"{parentPath}.{name}[{index}]");
            index++;
        }
        return words;
    }

    private static PlayerSaveExperience Experience(JsonElement element, string path)
    {
        Keys(element, path, ExperienceFields);
        return new PlayerSaveExperience(
            Integer(element.GetProperty("value"), path + ".value"),
            Integer(element.GetProperty("level"), path + ".level"),
            Integer(element.GetProperty("count"), path + ".count"));
    }

    private static PlayerSaveRole Role(JsonElement element, string path)
    {
        Keys(element, path, RoleFields);
        JsonElement hiddenElement = ObjectField(element, "hiddenExperience", path);
        var hidden = new Dictionary<string, PlayerSaveExperience>(StringComparer.Ordinal);
        foreach (JsonProperty property in hiddenElement.EnumerateObject())
        {
            if (hidden.Count >= PlayerSaveContract.MaxHiddenExperienceTracks)
                throw new InvalidDataException(
                    $"{path}.hiddenExperience: expected at most {PlayerSaveContract.MaxHiddenExperienceTracks} named tracks.");
            hidden.Add(property.Name, Experience(property.Value, $"{path}.hiddenExperience.{property.Name}"));
        }
        return new PlayerSaveRole(
            Integer(element.GetProperty("roleId"), path + ".roleId"),
            Integer(element.GetProperty("level"), path + ".level"),
            Integer(element.GetProperty("hp"), path + ".hp"),
            Integer(element.GetProperty("maxHp"), path + ".maxHp"),
            Integer(element.GetProperty("mp"), path + ".mp"),
            Integer(element.GetProperty("maxMp"), path + ".maxMp"),
            Experience(element.GetProperty("primaryExperience"), path + ".primaryExperience"),
            hidden,
            WordArrayField(element, "equipment", path, PlayerSaveContract.EquipmentSlotCount, PlayerSaveContract.EquipmentSlotCount),
            WordArrayField(element, "magics", path, 0, PlayerSaveContract.MaxLearnedMagics),
            Integer(element.GetProperty("coveredByRoleId"), path + ".coveredByRoleId"),
            Integer(element.GetProperty("attack"), path + ".attack"),
            Integer(element.GetProperty("magicPower"), path + ".magicPower"),
            Integer(element.GetProperty("defense"), path + ".defense"),
            Integer(element.GetProperty("dexterity"), path + ".dexterity"),
            Integer(element.GetProperty("fleeRate"), path + ".fleeRate"));
    }

    private static PlayerSaveWorld World(JsonElement element)
    {
        Keys(element, "$.world", WorldFields, WorldOptionalFields);
        JsonElement leaderElement = ObjectField(element, "leader", "$.world");
        Keys(leaderElement, "$.world.leader", LeaderFields);

        JsonElement objectsElement = ArrayField(element, "objects", "$.world", PlayerSaveContract.MaxEventObjects);
        var objects = new List<PlayerSaveObject>(objectsElement.GetArrayLength());
        int objectIndex = 0;
        foreach (JsonElement objectElement in objectsElement.EnumerateArray())
        {
            string path = $"$.world.objects[{objectIndex++}]";
            Keys(objectElement, path, ObjectFields);
            objects.Add(new PlayerSaveObject(
                Integer(objectElement.GetProperty("objectId"), path + ".objectId"),
                Integer(objectElement.GetProperty("x"), path + ".x"),
                Integer(objectElement.GetProperty("y"), path + ".y"),
                Integer(objectElement.GetProperty("direction"), path + ".direction"),
                Integer(objectElement.GetProperty("currentFrame"), path + ".currentFrame"),
                Integer(objectElement.GetProperty("state"), path + ".state"),
                Integer(objectElement.GetProperty("vanishTime"), path + ".vanishTime"),
                Integer(objectElement.GetProperty("triggerMode"), path + ".triggerMode"),
                Integer(objectElement.GetProperty("triggerEntry"), path + ".triggerEntry"),
                Integer(objectElement.GetProperty("autoEntry"), path + ".autoEntry"),
                Integer(objectElement.GetProperty("autoIdleCount"), path + ".autoIdleCount"),
                Integer(objectElement.GetProperty("triggerIdleCount"), path + ".triggerIdleCount")));
        }

        JsonElement sceneScriptsElement = ArrayField(element, "sceneScripts", "$.world", PlayerSaveContract.MaxSceneScriptEntries);
        var sceneScripts = new List<PlayerSaveSceneScript>(sceneScriptsElement.GetArrayLength());
        int sceneScriptIndex = 0;
        foreach (JsonElement sceneScriptElement in sceneScriptsElement.EnumerateArray())
        {
            string path = $"$.world.sceneScripts[{sceneScriptIndex++}]";
            Keys(sceneScriptElement, path, SceneScriptFields);
            sceneScripts.Add(new PlayerSaveSceneScript(
                Integer(sceneScriptElement.GetProperty("sceneId"), path + ".sceneId"),
                Integer(sceneScriptElement.GetProperty("onEnterEntry"), path + ".onEnterEntry"),
                Integer(sceneScriptElement.GetProperty("onTeleportEntry"), path + ".onTeleportEntry")));
        }

        JsonElement objectScriptsElement = ArrayField(element, "objectScripts", "$.world", PlayerSaveContract.MaxObjectScriptEntries);
        var objectScripts = new List<PlayerSaveObjectScript>(objectScriptsElement.GetArrayLength());
        int objectScriptIndex = 0;
        foreach (JsonElement objectScriptElement in objectScriptsElement.EnumerateArray())
        {
            string path = $"$.world.objectScripts[{objectScriptIndex++}]";
            Keys(objectScriptElement, path, ObjectScriptFields);
            objectScripts.Add(new PlayerSaveObjectScript(
                Integer(objectScriptElement.GetProperty("objectId"), path + ".objectId"),
                Integer(objectScriptElement.GetProperty("slotIndex"), path + ".slotIndex"),
                Integer(objectScriptElement.GetProperty("entry"), path + ".entry")));
        }

        PlayerSaveMusic? music = null;
        if (element.TryGetProperty("music", out JsonElement musicElement))
        {
            if (musicElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("$.world.music: expected a JSON object; an absent word is the only null form.");
            Keys(musicElement, "$.world.music", MusicFields);
            music = new PlayerSaveMusic(
                Integer(musicElement.GetProperty("musicId"), "$.world.music.musicId"),
                Integer(musicElement.GetProperty("mode"), "$.world.music.mode"));
        }

        return new PlayerSaveWorld(
            Integer(element.GetProperty("sceneId"), "$.world.sceneId"),
            new PlayerSaveLeader(
                Short(leaderElement.GetProperty("x"), "$.world.leader.x"),
                Short(leaderElement.GetProperty("y"), "$.world.leader.y"),
                Integer(leaderElement.GetProperty("direction"), "$.world.leader.direction"),
                Integer(leaderElement.GetProperty("layer"), "$.world.leader.layer"),
                Integer(leaderElement.GetProperty("stepPhase"), "$.world.leader.stepPhase")),
            objects, sceneScripts, objectScripts,
            Integer(element.GetProperty("battlefield"), "$.world.battlefield"), music);
    }
}
