using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pal.Core;
using Pal.Core.Battle;

namespace Pal.Extract;

/// <summary>Explicit projection of caller-verified M5 Windows base tables. Provenance
/// declarations and modern byte identities are recorded, not independently authenticated.</summary>
public static class BattleDataProjector
{
    private const int MaximumFileBytes = 64 * 1024 * 1024;
    private static readonly DataTableDescriptor[] Tables = [DataTables.EventObject, DataTables.Scene,
        DataTables.Store, DataTables.Enemy, DataTables.EnemyTeam, DataTables.PlayerRoles, DataTables.Magic,
        DataTables.BattleField, DataTables.LevelUpMagic, DataTables.BattleEffectIndex, DataTables.EnemyPos, DataTables.LevelUpExp];
    private static readonly string[] FileNames = ["index.json", "object.json", .. Tables.Select(t => t.FileId + ".json")];
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public sealed record Projection(string NumericJson, string ReceiptJson);

    /// <summary>Pure in-memory seam; complete exact modern file bytes, ordered unique selections.
    /// No raw archives, script host, session, randomness or filesystem access.</summary>
    public static Projection Project(IReadOnlyDictionary<string, byte[]> files, IReadOnlyList<int> roles, IReadOnlyList<int> teams)
    {
        ArgumentNullException.ThrowIfNull(files);
        ValidateSelection(roles, "roles", 5, 3);
        ValidateSelection(teams, "teams", 65535, 65536);
        if (!files.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(FileNames))
            throw new InvalidDataException("M5 input requires exactly index.json and all 13 registered table files.");
        var docs = FileNames.ToDictionary(name => name, name => Parse(files[name], name), StringComparer.Ordinal);
        JsonObject index = docs["index.json"];
        StrictJson.ValidateKeys(index, "index.json", "formatVersion", "profile", "sources", "tables");
        if (StrictJson.Int(index, "formatVersion") != 1 || StrictJson.String(index, "profile") != "faithful")
            throw new InvalidDataException("index.json: unsupported formatVersion or profile.");
        var archives = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonNode? node in StrictJson.RequiredArray(index, "sources"))
        {
            var source = Object(node, "index.json.sources");
            StrictJson.ValidateKeys(source, "index.json.sources", "archive", "sha256");
            string archive = StrictJson.String(source, "archive");
            if (archive is not "SSS.MKF" and not "DATA.MKF" || !archives.Add(archive))
                throw new InvalidDataException("index.json.sources: expected unique SSS.MKF and DATA.MKF declarations.");
            Hash(source, "index.json.sources");
        }
        if (archives.Count != 2) throw new InvalidDataException("index.json.sources: both source archives are required.");
        var entries = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (JsonNode? node in StrictJson.RequiredArray(index, "tables"))
        {
            var entry = Object(node, "index.json.tables");
            StrictJson.ValidateKeys(entry, "index.json.tables", "file", "archive", "chunk", "recordCount", "byteLength", "sha256");
            string file = StrictJson.String(entry, "file");
            if (!FileNames.Contains(file, StringComparer.Ordinal) || file == "index.json" || !entries.TryAdd(file, entry))
                throw new InvalidDataException($"index.json.tables: unknown or duplicate file '{file}'.");
            Hash(entry, file);
        }
        if (entries.Count != 13) throw new InvalidDataException("index.json.tables: all 13 tables are required.");
        foreach (DataTableDescriptor table in Tables)
        {
            string file = table.FileId + ".json";
            try { DataTableSchema.ValidateDocument(table, docs[file]); }
            catch (Exception error) when (error is InvalidOperationException or FormatException or OverflowException)
            { throw new InvalidDataException($"{file}: invalid field type or shape: {error.Message}", error); }
            CheckEntry(entries[file], file, table.Archive, table.Chunk, table.RecordByteSize, docs[file]);
            if (table == DataTables.PlayerRoles || table == DataTables.LevelUpExp || table == DataTables.EnemyPos || table == DataTables.BattleEffectIndex)
                if (Records(docs, table.FileId).Count != 1) throw new InvalidDataException($"{file}: requires exactly one record.");
        }
        // This command targets Windows, never infer variant from an ambiguous multiple of 12/14.
        JsonObject objectEntry = entries["object.json"];
        CheckEntry(objectEntry, "object.json", "SSS.MKF", 2, 14, docs["object.json"]);
        int objectLength = StrictJson.Int(objectEntry, "byteLength");
        if (ObjectUnionTable.DetectVariant(objectLength) != ObjectUnionVariant.Win95)
            throw new InvalidDataException("object.json: Windows 14-byte records required.");
        ObjectUnionTable.ValidateDocument(objectLength, docs["object.json"]);

        var sources = new JsonArray();
        var outputRoles = new JsonArray();
        var players = new JsonArray();
        var growth = new JsonArray();
        var outputTeams = new JsonArray();
        var slots = new JsonArray();
        JsonObject baseline = Row(docs, "player-roles", 0);
        for (int party = 0; party < roles.Count; party++)
        {
            int id = roles[party];
            if (id == 5) throw new InvalidDataException("player-roles.json role 5: no sixth level-up-magic role column; complete growth source unsupported.");
            string rp = $"roles[{party}]", pp = $"players[{party}]", gp = $"growth[{party}]";
            var role = new JsonObject();
            var player = new JsonObject();
            var g = new JsonObject();
            Add(role, "roleId", JsonValue.Create(id), rp, $"selection.roles[{party}]");
            Add(player, "roleId", JsonValue.Create(id), pp, $"selection.roles[{party}]");
            Add(g, "roleId", JsonValue.Create(id), gp, $"selection.roles[{party}]");
            int Read(string field) => StrictJson.Word(baseline[field]![id]);
            string From(string field) => $"player-roles.json.records[0].{field}[{id}]";
            foreach (string field in new[] { "level", "hp", "maxHp", "mp", "maxMp" })
                Add(role, field, JsonValue.Create(Read(field)), rp, From(field));
            foreach (string field in new[] { "hp", "maxHp" }) Add(player, field, JsonValue.Create(Read(field)), pp, From(field));
            foreach (var (from, to) in new[] { ("attackStrength", "attack"), ("magicStrength", "magicPower"),
                ("defense", "defense"), ("dexterity", "dexterity"), ("fleeRate", "fleeRate") })
            {
                Add(role, to, JsonValue.Create(Read(from)), rp, From(from));
                Add(player, to, JsonValue.Create(Read(from)), pp, From(from));
            }
            var equipment = new JsonArray();
            for (int slot = 0; slot < 6; slot++)
            {
                int item = StrictJson.Word(baseline["equipment"]![slot]![id]);
                if (item != 0) throw new InvalidDataException($"player-roles.json.records[0].equipment[{slot}][{id}]: nonzero equipment requires an equip-script closure unavailable to this command.");
                equipment.Add(item);
            }
            Add(role, "equipment", equipment, rp, $"player-roles.json.records[0].equipment[slot][{id}]");
            Add(player, "equipment", equipment.DeepClone(), pp, $"player-roles.json.records[0].equipment[slot][{id}]");
            var magics = new JsonArray();
            for (int slot = 0; slot < 32; slot++)
            {
                int magic = StrictJson.Word(baseline["magic"]![slot]![id]);
                MagicReference(magic, $"player-roles.json.records[0].magic[{slot}][{id}]");
                magics.Add(magic);
            }
            if (Read("cooperativeMagic") != 0) throw new InvalidDataException($"{From("cooperativeMagic")}: cooperative magic is unsupported by this physical projection.");
            Add(role, "magics", magics, rp, $"player-roles.json.records[0].magic[slot][{id}] (stored IDs, not casting capability)");
            int level = Read("level");
            var xp = new JsonObject { ["value"] = 0, ["level"] = level, ["count"] = 0 };
            string defaults = $"global.c PAL_LoadDefaultGame: EXP value/count=0, level from {From("level")}";
            Add(role, "primaryExperience", xp.DeepClone(), rp, defaults);
            var hidden = new JsonObject();
            foreach (string name in new[] { "health", "magic", "attack", "magicPower", "defense", "dexterity", "flee" }) hidden[name] = xp.DeepClone();
            Add(role, "hiddenExperience", hidden, rp, defaults);
            Add(role, "statuses", Zeros(9), rp, "global.c PAL_InitGameData: cleared role statuses; no equipment effects");
            Add(player, "statuses", Zeros(9), pp, "global.c PAL_InitGameData: cleared role statuses; no equipment effects");
            Add(player, "poisons", new JsonArray(), pp, "global.c PAL_LoadDefaultGame: cleared poison records");
            int cover = Read("coveredBy");
            if (cover > 5) throw new InvalidDataException($"{From("coveredBy")}: cover must be a role identity 0..5.");
            Add(role, "coveredByRoleId", JsonValue.Create(cover), rp, From("coveredBy"));
            // Preserve role0 as role0, including self/out-of-party IDs. Combat resolves by
            // identity and checks the coverer's actual party membership/status/HP (fight.c).
            Add(player, "coveredByRoleId", JsonValue.Create(cover), pp, From("coveredBy"));
            int nameId = Read("name");
            var nameObject = View(nameId, "player", From("name"));
            Add(player, "nameObjectId", JsonValue.Create(nameId), pp, From("name"));
            foreach (var (from, to) in new[] { ("scriptOnDying", "dyingScript"), ("scriptOnFriendDeath", "friendDeathScript") })
            {
                int hook = StrictJson.Int(nameObject, from);
                if (hook != 0) throw new InvalidDataException($"object.json.records[{nameId}].{from}: event host required for role {id}.");
                Add(player, to, JsonValue.Create(hook), pp, $"object.json.records[{nameId}].{from}");
            }
            int attackAll = Read("attackAll");
            if (attackAll != 0) throw new InvalidDataException($"{From("attackAll")}: attack-all is unsupported.");
            Add(player, "attackAll", JsonValue.Create(false), pp, From("attackAll"));
            Add(g, "levelUpExperience", Row(docs, "level-up-exp", 0)["levelUpExp"]!.DeepClone(), gp, "level-up-exp.json.records[0].levelUpExp[level]");
            var learn = new JsonArray();
            int rowIndex = 0;
            foreach (JsonNode? node in Records(docs, "level-up-magic"))
            {
                var pair = node!["roles"]![id]!.AsObject();
                int magic = StrictJson.Int(pair, "magicId");
                MagicReference(magic, $"level-up-magic.json.records[{rowIndex}].roles[{id}].magicId");
                var row = new JsonObject();
                foreach (string field in new[] { "level", "magicId" })
                    Add(row, field, pair[field]!.DeepClone(), $"{gp}.levelUpMagics[{rowIndex}]", $"level-up-magic.json.records[{rowIndex}].roles[{id}].{field}");
                learn.Add(row);
                rowIndex++;
            }
            g["levelUpMagics"] = learn;
            outputRoles.Add(role); players.Add(player); growth.Add(g);
        }
        for (int teamIndex = 0; teamIndex < teams.Count; teamIndex++)
        {
            int id = teams[teamIndex];
            JsonObject sourceTeam = Row(docs, "enemy-team", id);
            var enemies = new JsonArray();
            int loaded = 0;
            for (int j = 0; j < 5; j++)
            {
                int objectId = StrictJson.Word(sourceTeam["enemies"]![j]);
                string from = $"enemy-team.json.records[{id}].enemies[{j}]";
                if (objectId == 65535)
                {
                    slots.Add(new JsonObject { ["teamId"] = id, ["sourceSlot"] = j, ["objectId"] = objectId, ["disposition"] = "skip-FFFF" });
                    continue;
                }
                // #140 (battle.c:1617-1717): a zero source row keeps a real runtime slot with
                // wObjectID 0 — it never reads the enemy table — while FFFF never reaches i.
                if (objectId == 0)
                {
                    int runtimeSlot = enemies.Count;
                    string xp = $"teams[{teamIndex}].enemies[{runtimeSlot}]";
                    string source = $"{from}: zero row keeps runtime slot {runtimeSlot} (PAL_StartBattle wObjectID 0)";
                    var empty = new JsonObject();
                    Add(empty, "isEmpty", JsonValue.Create(true), xp, source);
                    foreach (var (field, note) in new[] { ("id", "canonical empty id 0"), ("level", "empty slot never reads the enemy table"),
                        ("hp", "canonical empty hp 0"), ("attack", "empty slot never reads the enemy table"),
                        ("defense", "empty slot never reads the enemy table"), ("dexterity", "empty slot never reads the enemy table"),
                        ("physicalResistance", "empty slot never reads the enemy table") })
                        Add(empty, field, JsonValue.Create(0), xp, $"{from}: {note}");
                    Add(empty, "statuses", Zeros(9), xp, $"{from}: empty slot carries no enemy statuses");
                    Add(empty, "poisons", new JsonArray(), xp, $"{from}: empty slot carries no poisons");
                    foreach (string field in new[] { "turnStartScript", "readyScript", "battleEndScript", "magicId", "dualMove", "attackEquivItem", "attackEquivItemRate" })
                        Add(empty, field, JsonValue.Create(0), xp, $"{from}: empty slot runs no hooks or enemy actions");
                    Add(empty, "experience", JsonValue.Create(0), xp, $"{from}: empty slot contributes no rewards");
                    Add(empty, "cash", JsonValue.Create(0), xp, $"{from}: empty slot contributes no rewards");
                    slots.Add(new JsonObject { ["teamId"] = id, ["sourceSlot"] = j, ["objectId"] = 0, ["disposition"] = "empty-0" });
                    enemies.Add(empty);
                    continue;
                }
                // #210: dense teams migrate with Core to the 1..5 loaded-slot domain
                // (palcommon.h MAX_ENEMIES_IN_TEAM); the five source rows bound the count.
                JsonObject obj = View(objectId, "enemy", from);
                int enemyId = StrictJson.Int(obj, "enemyId");
                JsonObject definition = Row(docs, "enemy", enemyId);
                // Runtime slot i: FFFF never reaches it, a zero row occupies one, duplicates retained.
                int slot = enemies.Count;
                string ep = $"teams[{teamIndex}].enemies[{slot}]";
                var enemy = new JsonObject();
                Add(enemy, "id", JsonValue.Create(slot + 1), ep, $"{from}: runtime slot+1 battleId (FFFF skipped, 0 keeps a slot, duplicates retained)");
                foreach (var (field, to) in new[] { ("level", "level"), ("health", "hp"), ("physicalResistance", "physicalResistance"),
                    ("magicId", "magicId"), ("dualMove", "dualMove"), ("attackEquivItemId", "attackEquivItem"),
                    ("attackEquivItemRate", "attackEquivItemRate"), ("exp", "experience"), ("cash", "cash") })
                {
                    int value = StrictJson.Int(definition, field);
                    if (field is "magicId" or "dualMove" or "attackEquivItemId" or "attackEquivItemRate" && value != 0)
                        throw new InvalidDataException($"enemy.json.records[{enemyId}].{field} ({from}): unsupported physical capability; values including magic FFFF are never normalized.");
                    Add(enemy, to, JsonValue.Create(value), ep, $"enemy.json.records[{enemyId}].{field}");
                }
                foreach (var (field, to) in new[] { ("attackStrength", "attack"), ("defense", "defense"), ("dexterity", "dexterity") })
                    Add(enemy, to, JsonValue.Create((int)unchecked((short)StrictJson.Int(definition, field))), ep, $"enemy.json.records[{enemyId}].{field}: SHORT interpretation at physical consumption");
                foreach (var (field, to) in new[] { ("scriptOnTurnStart", "turnStartScript"), ("scriptOnReady", "readyScript"), ("scriptOnBattleEnd", "battleEndScript") })
                {
                    int hook = StrictJson.Int(obj, field);
                    if (hook != 0) throw new InvalidDataException($"object.json.records[{objectId}].{field} ({from}): event host required.");
                    Add(enemy, to, JsonValue.Create(hook), ep, $"object.json.records[{objectId}].{field}");
                }
                Add(enemy, "statuses", Zeros(9), ep, "battle.c PAL_StartBattle: cleared BATTLEENEMY");
                Add(enemy, "poisons", new JsonArray(), ep, "battle.c PAL_StartBattle: cleared BATTLEENEMY");
                slots.Add(new JsonObject { ["teamId"] = id, ["sourceSlot"] = j, ["objectId"] = objectId,
                    ["enemyId"] = enemyId, ["compactSlot"] = slot, ["battleId"] = slot + 1, ["disposition"] = "loaded" });
                enemies.Add(enemy);
                loaded++;
            }
            if (loaded == 0) throw new InvalidDataException($"enemy-team.json.records[{id}]: no loaded enemies.");
            var team = new JsonObject { ["enemies"] = enemies };
            Add(team, "teamId", JsonValue.Create(id), $"teams[{teamIndex}]", $"selection.teams[{teamIndex}]");
            outputTeams.Add(team);
        }
        var numeric = new JsonObject { ["roles"] = outputRoles, ["players"] = players, ["growth"] = growth, ["teams"] = outputTeams };
        Add(numeric, "formatVersion", JsonValue.Create(1), "", "modern-battle-data v1");
        string numericJson = numeric.ToJsonString(JsonOptions) + "\n";
        if (Utf8.GetByteCount(numericJson) > 16 * 1024 * 1024)
            throw new InvalidDataException("numeric/battle-data.json: output exceeds the existing PrepareBattleData 16MiB input limit.");
        TriggerBattleContent content = ModernBattleContent.Parse(numericJson);
        foreach (int team in teams)
        {
            try { ModernBattleContent.ValidatePhysicalBattle(content, team); }
            catch (InvalidDataException error) { throw new InvalidDataException($"Selected roles [{string.Join(',', roles)}], enemy-team.json.records[{team}]: {error.Message}", error); }
        }
        var identities = new JsonArray();
        foreach (string name in FileNames.Order(StringComparer.Ordinal))
            identities.Add(new JsonObject { ["file"] = name, ["byteLength"] = files[name].Length, ["sha256"] = Digest(files[name]) });
        var receipt = new JsonObject
        {
            ["formatVersion"] = 1, ["kind"] = "m5-base-table-physical-projection",
            ["snapshotKind"] = "independent-base-table-context-not-natural-save",
            ["inputTrust"] = "caller-verified-upstream; declarations-and-byte-identities-not-independent-authentication",
            ["declaredSources"] = index["sources"]!.DeepClone(), ["declaredTables"] = index["tables"]!.DeepClone(),
            ["modernInputs"] = identities, ["roles"] = new JsonArray(roles.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
            ["teams"] = new JsonArray(teams.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
            ["enemySlots"] = slots, ["fieldSources"] = sources,
            ["output"] = new JsonObject { ["file"] = "numeric/battle-data.json", ["byteLength"] = Utf8.GetByteCount(numericJson), ["sha256"] = Digest(Utf8.GetBytes(numericJson)) },
            ["limits"] = "Host-free numeric physical capability only; no script/equipment provenance, casting, images, language, scene or story closure. Unconsumed M5 metadata remains identified by input bytes, not projected as gameplay."
        };
        return new(numericJson, receipt.ToJsonString(JsonOptions) + "\n");

        JsonObject View(int id, string expected, string path)
        {
            JsonObject obj = Row(docs, "object", id);
            if (StrictJson.String(obj, "view") != expected)
                throw new InvalidDataException($"{path}: object.json.records[{id}] requires declared {expected} view.");
            return obj;
        }
        void MagicReference(int id, string path)
        {
            if (id == 0) return;
            JsonObject magic = View(id, "magic", path);
            _ = Row(docs, "magic", StrictJson.Int(magic, "magicId"));
        }
        void Add(JsonObject obj, string key, JsonNode? value, string parent, string source)
        {
            obj[key] = value;
            Record(value, string.IsNullOrEmpty(parent) ? key : parent + "." + key, source);
        }
        void Record(JsonNode? node, string path, string source)
        {
            if (node is JsonObject o) { foreach (var pair in o) Record(pair.Value, path + "." + pair.Key, source); }
            else if (node is JsonArray a && a.Count > 0)
            {
                for (int i = 0; i < a.Count; i++)
                    Record(a[i], path + $"[{i}]", source.Replace("[slot]", $"[{i}]", StringComparison.Ordinal).Replace("[level]", $"[{i}]", StringComparison.Ordinal));
            }
            else sources.Add(new JsonObject { ["path"] = path, ["source"] = source });
        }
    }

    /// <summary>Read exact modern files under shared-read handles through publication;
    /// write new files only in a disjoint new/empty directory. No cleanup of unknown files.</summary>
    public static void Export(string input, string output, IReadOnlyList<int> roles, IReadOnlyList<int> teams)
    {
        string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input));
        string destination = ExportPaths.Validate(source, output, []);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (source.StartsWith(Path.EndsInDirectorySeparator(destination) ? destination : destination + Path.DirectorySeparatorChar, comparison))
            throw new InvalidDataException("Input must not be inside the output directory.");
        if (!Directory.Exists(source)) throw new InvalidDataException("M5 input directory does not exist.");
        var actual = Directory.EnumerateFileSystemEntries(source).Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(FileNames)) throw new InvalidDataException("M5 input requires exactly index.json and all 13 registered table files.");
        var handles = new List<FileStream>();
        try
        {
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (string name in FileNames)
            {
                string path = Path.Combine(source, name);
                PathLinkGuard.RejectReparsePoints(path, $"{name}: input links/reparse points are not supported.");
                var handle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                handles.Add(handle);
                if (handle.Length is <= 0 or > MaximumFileBytes) throw new InvalidDataException($"{name}: file size must be 1..{MaximumFileBytes} bytes.");
                byte[] bytes = new byte[(int)handle.Length];
                handle.ReadExactly(bytes);
                files.Add(name, bytes);
            }
            Projection projection = Project(files, roles, teams);
            // Recheck immediately before touching output, without truncating any existing file.
            _ = ExportPaths.Validate(source, destination, []);
            Directory.CreateDirectory(destination);
            PathLinkGuard.RejectReparsePoints(Path.Combine(destination, "numeric"), "Output: links/reparse points are not supported.");
            Directory.CreateDirectory(Path.Combine(destination, "numeric"));
            WriteNew(Path.Combine(destination, "numeric", "battle-data.json"), projection.NumericJson);
            WriteNew(Path.Combine(destination, "receipt.json"), projection.ReceiptJson);
        }
        finally { foreach (FileStream handle in handles) handle.Dispose(); }
    }

    /// <summary>Strict decimal comma-list for the CLI; malformed selections are argv errors.</summary>
    public static bool TrySelection(string text, out int[] selection)
    {
        selection = [];
        string[] parts = text.Split(',');
        var values = new List<int>();
        foreach (string part in parts)
        {
            if (part.Length == 0 || part.Any(c => c is < '0' or > '9')
                || !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
                || value > 65535 || values.Contains(value)) return false;
            values.Add(value);
        }
        selection = values.ToArray();
        return true;
    }

    private static void ValidateSelection(IReadOnlyList<int>? selection, string name, int maximum, int countLimit)
    {
        if (selection is null || selection.Count == 0 || selection.Count > countLimit
            || selection.Any(x => x < 0 || x > maximum) || selection.Distinct().Count() != selection.Count)
            throw new InvalidDataException($"{name}: expected ordered unique IDs 0..{maximum}, at most {countLimit} selections.");
    }

    private static JsonObject Parse(byte[] bytes, string name)
    {
        if (bytes is null || bytes.Length is <= 0 or > MaximumFileBytes) throw new InvalidDataException($"{name}: invalid file size.");
        try
        {
            ReadOnlySpan<byte> input = bytes;
            if (input.StartsWith(Encoding.UTF8.Preamble)) input = input[Encoding.UTF8.Preamble.Length..];
            using var parsed = JsonDocument.Parse(Utf8.GetString(input));
            StrictJson.RejectDuplicateKeys(parsed.RootElement, name);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"{name}: root must be object.");
            return JsonNode.Parse(parsed.RootElement.GetRawText())!.AsObject();
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException)
        { throw new InvalidDataException($"{name}: invalid UTF-8 or JSON: {error.Message}", error); }
    }

    private static void CheckEntry(JsonObject entry, string file, string archive, int chunk, int width, JsonObject doc)
    {
        int count = StrictJson.Int(entry, "recordCount"), length = StrictJson.Int(entry, "byteLength");
        if (StrictJson.String(entry, "archive") != archive || StrictJson.Int(entry, "chunk") != chunk
            || count <= 0 || count > 65536 || length <= 0 || length != (long)count * width
            || StrictJson.RequiredArray(doc, "records").Count != count)
            throw new InvalidDataException($"index.json {file}: archive/chunk/recordCount/byteLength must match its fixed layout and actual records.");
    }

    private static void Hash(JsonObject obj, string path)
    {
        string hash = StrictJson.String(obj, "sha256");
        if (hash.Length != 64 || hash.Any(c => !char.IsAsciiHexDigit(c))) throw new InvalidDataException($"{path}.sha256: expected 64 hexadecimal digits (original source declaration, not JSON digest).");
    }
    private static JsonObject Object(JsonNode? node, string path) => node as JsonObject ?? throw new InvalidDataException($"{path}: expected object.");
    private static JsonArray Records(IReadOnlyDictionary<string, JsonObject> docs, string table) => StrictJson.RequiredArray(docs[table + ".json"], "records");
    private static JsonObject Row(IReadOnlyDictionary<string, JsonObject> docs, string table, int index)
    {
        JsonArray records = Records(docs, table);
        if (index < 0 || index >= records.Count) throw new InvalidDataException($"{table}.json.records[{index}]: missing referenced record.");
        return Object(records[index], $"{table}.json.records[{index}]");
    }
    private static JsonArray Zeros(int count) => new(Enumerable.Range(0, count).Select(_ => (JsonNode?)JsonValue.Create(0)).ToArray());
    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void WriteNew(string path, string text)
    {
        PathLinkGuard.RejectReparsePoints(path, "Output: links/reparse points are not supported.");
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(Utf8.GetBytes(text));
    }
}
