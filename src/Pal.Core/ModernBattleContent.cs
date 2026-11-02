using System.Text.Json;

namespace Pal.Core;

/// <summary>Strict, pure numeric modern battle data. Parsing does not grant selected-entry combat capability.</summary>
public static partial class ModernBattleContent
{
    private const string Root = "battle-data.json.$";

    public static TriggerBattleContent Parse(string json)
    {
        using var document = EventJson.Parse(json, Root);
        var root = document.RootElement;
        EventJson.Keys(root, Root, "formatVersion", "roles", "players", "growth", "teams");
        if (Number(root, "formatVersion", Root) != 1)
            throw Error(Root, "unsupported formatVersion");
        var role = Single(root, "roles", Root);
        var player = Single(root, "players", Root);
        var growth = Single(root, "growth", Root);
        string rp = Root + ".roles[0]", pp = Root + ".players[0]", gp = Root + ".growth[0]";
        ValidateRole(role, rp);
        var initialRole = BattleModels.Role(role);
        var combatPlayer = Player(player, pp);
        EventJson.Keys(growth, gp, "roleId", "levelUpExperience", "levelUpMagics");
        int growthRole = Number(growth, "roleId", gp, 0, 5);
        if (initialRole.RoleId != combatPlayer.RoleId || initialRole.Hp != combatPlayer.Hp || initialRole.MaxHp != combatPlayer.MaxHp
            || initialRole.RoleId != growthRole)
            throw Error(Root, "roles[0], players[0] and growth[0] must describe the same role, HP and maxHp");
        var thresholds = Words(growth, "levelUpExperience", gp, 100);
        var rows = new List<BattleLevelUpMagic>();
        int rowIndex = 0;
        foreach (var row in Array(growth, "levelUpMagics", gp))
        {
            string path = gp + $".levelUpMagics[{rowIndex++}]";
            EventJson.Keys(row, path, "level", "magicId");
            rows.Add(new(Number(row, "level", path), Number(row, "magicId", path)));
        }
        var teams = new Dictionary<int, ClassicBattleEnemy>();
        int teamIndex = 0;
        foreach (var team in Array(root, "teams", Root))
        {
            string path = Root + $".teams[{teamIndex++}]";
            EventJson.Keys(team, path, "teamId", "enemies");
            int id = Number(team, "teamId", path);
            var enemy = Enemy(Single(team, "enemies", path), path + ".enemies[0]");
            if (!teams.TryAdd(id, enemy)) throw Error(path + ".teamId", $"duplicate team ID {id}");
        }
        if (teams.Count == 0) throw Error(Root + ".teams", "expected at least one team");
        var content = new TriggerBattleContent(initialRole, combatPlayer, new(thresholds, rows), teams);
        Validate(content);
        return content;
    }

    private static void ValidateRole(JsonElement role, string path)
    {
        EventJson.Keys(role, path, "roleId", "level", "hp", "maxHp", "mp", "maxMp", "primaryExperience",
            "hiddenExperience", "equipment", "magics", "statuses", "coveredByRoleId");
        Number(role, "roleId", path, 0, 5);
        Number(role, "level", path);
        int hp = Number(role, "hp", path), maxHp = Number(role, "maxHp", path, 1);
        int mp = Number(role, "mp", path), maxMp = Number(role, "maxMp", path);
        if (hp > maxHp) throw Error(path + ".hp", "must not exceed maxHp");
        if (mp > maxMp) throw Error(path + ".mp", "must not exceed maxMp");
        Experience(Property(role, "primaryExperience", path), path + ".primaryExperience");
        var hidden = Property(role, "hiddenExperience", path);
        string[] names = ["health", "magic", "attack", "magicPower", "defense", "dexterity", "flee"];
        EventJson.Keys(hidden, path + ".hiddenExperience", names);
        foreach (string name in names)
            Experience(Property(hidden, name, path + ".hiddenExperience"), path + ".hiddenExperience." + name);
        Words(role, "equipment", path, 6);
        Words(role, "magics", path, 32);
        Words(role, "statuses", path, 9);
        Number(role, "coveredByRoleId", path, 0, 5);
    }

    private static void Experience(JsonElement value, string path)
    {
        EventJson.Keys(value, path, "value", "level", "count");
        Number(value, "value", path);
        Number(value, "level", path);
        Number(value, "count", path);
    }

    private static ClassicBattlePlayer Player(JsonElement value, string path)
    {
        EventJson.Keys(value, path, "roleId", "hp", "maxHp", "attack", "defense", "dexterity", "equipment",
            "statuses", "poisons", "dyingScript", "friendDeathScript", "coveredByRoleId", "attackAll");
        int hp = Number(value, "hp", path), maxHp = Number(value, "maxHp", path, 1);
        if (hp > maxHp) throw Error(path + ".hp", "must not exceed maxHp");
        var cover = Property(value, "coveredByRoleId", path);
        return new(Number(value, "roleId", path, 0, 5), hp, maxHp,
            Number(value, "attack", path), Number(value, "defense", path), Number(value, "dexterity", path))
        {
            Equipment = Words(value, "equipment", path, 6), Statuses = Words(value, "statuses", path, 9),
            Poisons = Words(value, "poisons", path), DyingScript = Number(value, "dyingScript", path),
            FriendDeathScript = Number(value, "friendDeathScript", path),
            CoveredByRoleId = cover.ValueKind == JsonValueKind.Null ? null : Integer(cover, path + ".coveredByRoleId", 0, 5),
            AttackAll = Boolean(value, "attackAll", path)
        };
    }

    private static ClassicBattleEnemy Enemy(JsonElement value, string path)
    {
        EventJson.Keys(value, path, "id", "level", "hp", "attack", "defense", "dexterity", "physicalResistance",
            "statuses", "poisons", "turnStartScript", "readyScript", "battleEndScript", "magicId", "dualMove",
            "attackEquivItem", "attackEquivItemRate", "experience", "cash");
        return new(Number(value, "id", path, 1), Number(value, "level", path), Number(value, "hp", path, 1),
            Number(value, "attack", path, -32768, 32767), Number(value, "defense", path, -32768, 32767),
            Number(value, "dexterity", path, -32768, 32767), Number(value, "physicalResistance", path))
        {
            Statuses = Words(value, "statuses", path, 9), Poisons = Words(value, "poisons", path),
            TurnStartScript = Number(value, "turnStartScript", path), ReadyScript = Number(value, "readyScript", path),
            BattleEndScript = Number(value, "battleEndScript", path), MagicId = Number(value, "magicId", path),
            DualMove = Number(value, "dualMove", path), AttackEquivItem = Number(value, "attackEquivItem", path),
            AttackEquivItemRate = Number(value, "attackEquivItemRate", path),
            Experience = Number(value, "experience", path), Cash = Number(value, "cash", path)
        };
    }

    private static JsonElement Property(JsonElement value, string key, string path) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var result) ? result
            : throw Error(path + "." + key, "missing field or expected parent object");

    private static int Number(JsonElement value, string key, string path, int min = 0, int max = 65535) =>
        Integer(Property(value, key, path), path + "." + key, min, max);

    private static int Integer(JsonElement value, string path, int min, int max) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) && number >= min && number <= max
            ? number : throw Error(path, $"expected integer in {min}..{max}");

    private static bool Boolean(JsonElement value, string key, string path) => Property(value, key, path).ValueKind switch
    {
        JsonValueKind.True => true, JsonValueKind.False => false,
        _ => throw Error(path + "." + key, "expected boolean")
    };

    private static JsonElement.ArrayEnumerator Array(JsonElement value, string key, string path)
    {
        var array = Property(value, key, path);
        return array.ValueKind == JsonValueKind.Array ? array.EnumerateArray() : throw Error(path + "." + key, "expected array");
    }

    private static JsonElement Single(JsonElement value, string key, string path)
    {
        var items = Array(value, key, path).ToArray();
        if (items.Length != 1) throw Error(path + "." + key, "current content binding requires exactly one item");
        return items[0];
    }

    private static IReadOnlyList<int> Words(JsonElement value, string key, string path, int? count = null)
    {
        var items = Array(value, key, path).ToArray();
        if (count is not null && items.Length != count) throw Error(path + "." + key, $"expected exactly {count} items");
        return System.Array.AsReadOnly(items.Select((item, i) => Integer(item, path + $".{key}[{i}]", 0, 65535)).ToArray());
    }

    private static InvalidDataException Error(string path, string message) => new($"{path}: {message}.");
}
