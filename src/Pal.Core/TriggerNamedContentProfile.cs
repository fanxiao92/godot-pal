using System.Text.Json;

namespace Pal.Core;

/// <summary>Content shapes only. Registration here never grants trigger or auto execution.</summary>
internal static class TriggerNamedContentProfile
{
    private enum FieldKind { Word, SignedWord, Reference, NullableReference }
    private sealed record Field(string Name, FieldKind Kind);
    private sealed record Shape(int Opcode, string Op, Field[] Fields);

    private static readonly Shape[] Shapes =
    [
        new(0x0004, "call", [new("to", FieldKind.Reference), new("eventObjectId", FieldKind.Word)]),
        new(0x0008, "advanceEntry", []),
        new(0x000A, "confirmNoJump", [new("to", FieldKind.Reference)]),
        new(0x0011, "walkToSlow", [new("x", FieldKind.Word), new("y", FieldKind.Word), new("h", FieldKind.Word)]),
        new(0x0012, "setObjectPositionRelativeToParty", [new("eventObjectId", FieldKind.Word), new("xOffset", FieldKind.Word), new("yOffset", FieldKind.Word)]),
        new(0x0016, "setObjectFacing", [new("eventObjectId", FieldKind.Word), new("direction", FieldKind.Word), new("frame", FieldKind.Word)]),
        new(0x0017, "setExtraAttr", [new("equipPartWord", FieldKind.Word), new("field", FieldKind.Word), new("value", FieldKind.SignedWord)]),
        new(0x0018, "equipItem", [new("equipPartWord", FieldKind.Word), new("itemId", FieldKind.Word)]),
        new(0x0019, "incPlayerAttr", [new("field", FieldKind.Word), new("delta", FieldKind.SignedWord), new("roleOperand", FieldKind.Word)]),
        new(0x001A, "setPlayerStat", [new("field", FieldKind.Word), new("value", FieldKind.SignedWord), new("roleOperand", FieldKind.Word)]),
        new(0x001E, "changeCash", [new("amount", FieldKind.SignedWord), new("to", FieldKind.Reference)]),
        new(0x0020, "removeItem", [new("itemId", FieldKind.Word), new("count", FieldKind.Word), new("shortageTarget", FieldKind.NullableReference)]),
        new(0x0021, "inflictDamageToEnemy", [new("scope", FieldKind.Word), new("damage", FieldKind.Word)]),
        new(0x0023, "removeEquipment", [new("playerRole", FieldKind.Word), new("partWord", FieldKind.Word)]),
        new(0x0026, "showBuyMenu", [new("storeId", FieldKind.Word)]),
        new(0x0027, "showSellMenu", []),
        new(0x0028, "applyPoisonToEnemy", [new("scope", FieldKind.Word), new("objectId", FieldKind.Word)]),
        new(0x0029, "applyPoisonToPlayer", [new("scope", FieldKind.Word), new("objectId", FieldKind.Word)]),
        new(0x002A, "curePoisonByObjectIdForEnemy", [new("scope", FieldKind.Word), new("objectId", FieldKind.Word)]),
        new(0x002B, "curePoisonByObjectIdForPlayer", [new("scope", FieldKind.Word), new("objectId", FieldKind.Word)]),
        new(0x002C, "curePoisonsByLevel", [new("scope", FieldKind.Word), new("maxLevel", FieldKind.Word)]),
        new(0x002D, "setStatusPlayer", [new("status", FieldKind.Word), new("rounds", FieldKind.Word)]),
        new(0x002E, "setStatusEnemy", [new("status", FieldKind.Word), new("rounds", FieldKind.Word), new("to", FieldKind.Reference)]),
        new(0x002F, "clearPlayerStatus", [new("status", FieldKind.Word)]),
        new(0x0031, "changeBattleSprite", [new("battleSprite", FieldKind.Word)]),
        new(0x0033, "collectEnemyForItems", [new("to", FieldKind.Reference)]),
        new(0x0034, "transformCollectedEnemies", [new("to", FieldKind.Reference)]),
        new(0x0035, "shakeScreen", [new("times", FieldKind.Word), new("level", FieldKind.Word)]),
        new(0x0036, "setPlayingRng", [new("rngId", FieldKind.Word)]),
        new(0x0037, "playRng", [new("startFrame", FieldKind.Word), new("endFrame", FieldKind.Word), new("speed", FieldKind.Word)]),
        new(0x0038, "teleportParty", [new("to", FieldKind.Reference)]),
        new(0x0039, "drainHpFromEnemy", [new("amount", FieldKind.Word)]),
        new(0x003A, "playerFlee", [new("to", FieldKind.Reference)]),
        new(0x003F, "rideEventObjectSlow", [new("x", FieldKind.Word), new("y", FieldKind.Word), new("h", FieldKind.Word)]),
        new(0x0042, "simulateMagic", [new("magicId", FieldKind.Word), new("baseDamage", FieldKind.Word), new("enemyOperand", FieldKind.SignedWord)]),
        new(0x0044, "rideEventObject", [new("x", FieldKind.Word), new("y", FieldKind.Word), new("h", FieldKind.Word)]),
        new(0x0045, "setBattleMusic", [new("battleMusicId", FieldKind.Word)]),
        new(0x0047, "playSound", [new("soundId", FieldKind.Word)]),
        new(0x004B, "nullifyObject", []),
        new(0x004C, "chasePlayer", [new("chaseRange", FieldKind.Word), new("speed", FieldKind.Word), new("floating", FieldKind.Word)]),
        new(0x004D, "waitForAnyKey", []),
        new(0x0051, "fadeIn", [new("delay", FieldKind.Word)]),
        new(0x0053, "useDayPalette", []),
        new(0x0054, "useNightPalette", []),
        new(0x0055, "addMagic", [new("magicId", FieldKind.Word), new("roleOperand", FieldKind.Word)]),
        new(0x0056, "removeMagic", [new("magicId", FieldKind.Word), new("roleOperand", FieldKind.Word)]),
        new(0x0057, "setMagicBaseDamageByMp", [new("magicId", FieldKind.Word), new("mpMultiplier", FieldKind.Word)]),
        new(0x0058, "itemShortageJump", [new("itemId", FieldKind.Word), new("count", FieldKind.SignedWord), new("to", FieldKind.Reference)]),
        new(0x0059, "changeScene", [new("sceneId", FieldKind.Word)]),
        new(0x005A, "halveHp", []),
        new(0x005B, "halveEnemyHp", [new("maxDamage", FieldKind.Word)]),
        new(0x005C, "setBattleHidingTime", [new("units", FieldKind.Word)]),
        new(0x005D, "playerLacksPoisonJump", [new("objectId", FieldKind.Word), new("to", FieldKind.Reference)]),
        new(0x005E, "enemyLacksPoisonJump", [new("objectId", FieldKind.Word), new("to", FieldKind.Reference)]),
        new(0x005F, "killPlayer", []),
        new(0x0060, "killEnemy", []),
        new(0x0061, "notPoisonedJump", [new("to", FieldKind.Reference)]),
        new(0x0062, "pauseEnemyChasing", [new("cycles", FieldKind.Word)]),
        new(0x0063, "speedUpEnemyChasing", [new("cycles", FieldKind.Word)]),
        new(0x0064, "enemyHpAboveJump", [new("percent", FieldKind.Word), new("to", FieldKind.Reference)]),
        new(0x0065, "setPlayerSprite", [new("playerRole", FieldKind.Word), new("spriteNum", FieldKind.Word), new("reload", FieldKind.Word)]),
        new(0x0066, "throwWeapon", [new("magicId", FieldKind.Word), new("damageUnits", FieldKind.Word)]),
        new(0x0067, "enemyUseMagic", [new("magicId", FieldKind.Word), new("magicRate", FieldKind.Word)]),
        new(0x0068, "enemyTurnJump", [new("to", FieldKind.Reference)]),
    ];

    private static Shape Find(string op, int opcode)
    {
        if (!Shapes.Any(shape => shape.Op == op)) throw new InvalidDataException($"Unknown op '{op}'.");
        return Shapes.SingleOrDefault(shape => shape.Op == op && shape.Opcode == opcode)
            ?? throw new InvalidDataException($"Invalid {op} mapping.");
    }

    internal static string[] Fields(string op, int opcode) => Find(op, opcode).Fields.Select(field => field.Name).ToArray();

    internal static IReadOnlyList<TriggerContentReference> Validate(JsonElement node, string op, int opcode)
    {
        var references = new List<TriggerContentReference>();
        foreach (var field in Find(op, opcode).Fields)
        {
            if (field.Kind == FieldKind.Word) _ = EventJson.Word(node, field.Name);
            else if (field.Kind == FieldKind.SignedWord)
            {
                if (EventJson.Number(node, field.Name) is < short.MinValue or > short.MaxValue)
                    throw new InvalidDataException($"{op}.{field.Name} must be a signed WORD.");
            }
            else
            {
                string? reference = EventJson.Reference(node, field.Name);
                bool nullable = field.Kind == FieldKind.NullableReference;
                if (reference is null && !nullable)
                    throw new InvalidDataException($"{op}.{field.Name} must be a reference, including address zero.");
                if (reference is not null) references.Add(new(field.Name, reference, nullable));
            }
        }
        return references.AsReadOnly();
    }
}

internal sealed record TriggerContentReference(string Field, string Reference, bool RequiresNonzeroAddress);
