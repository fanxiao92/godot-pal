using System.Buffers.Binary;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Pal.Extract;

/// Binding kind of one semantic field declared by an opcode descriptor
/// (events-schema v1, opcode registry chapter).
public enum OpcodeFieldKind
{
    /// Unsigned operand re-encoded from an integer 0..65535.
    Word,
    /// Signed operand (-32768..32767) re-encoded as a two's-complement WORD.
    SignedWord,
    /// 0..255 field bound to the operand's low byte; the source WORD's unmapped high byte is preserved.
    LowByte,
    /// Required label reference resolving to a chunk-4 address.
    Reference,
    /// Nullable label reference: null encodes WORD 0, and a reference to address 0 is rejected.
    BattleTarget,
    /// Boolean coupled to a BattleTarget field on the same operand (true <=> that reference is null).
    BossFlag,
    /// Value derived from the opcode itself: value = opcode - Base.
    OpcodeOffset,
    /// Fixed JSON string that must match the descriptor (e.g. setDialog.box, end.mode).
    ConstantString,
}

/// One declared field binding of a named opcode.
public sealed record OpcodeField(
    string Name, OpcodeFieldKind Kind, int Operand = -1, int Base = 0,
    string? CoupledTarget = null, string? ConstantString = null);

/// Declared executor handling of an opcode, mirroring the script.c switch topology:
/// PAL_RunTriggerScript and PAL_RunAutoScript each own a small control-flow switch and
/// delegate everything else to the shared PAL_InterpretInstruction.
public enum OpcodeExecutor
{
    /// Handled by PAL_InterpretInstruction for both trigger and auto.
    Shared,
    /// Handled directly by the PAL_RunTriggerScript switch; auto would terminate.
    TriggerSwitchOnly,
    /// Handled by both switches with the same semantics.
    BothSwitches,
    /// Handled by both switches with different behavior (e.g. idle counters, description text).
    BothSwitchesDiffer,
}

/// Declared control-flow successor kind of a named command.
public enum OpcodeSuccessor
{
    /// Advances to the WORD-order next entry.
    Sequential,
    /// Stops the script (end keep).
    Stop,
    /// Conditional jump: either the declared reference target or next.
    ConditionalJump,
    /// Battle-result dependent: next / lossTarget / fleeTarget.
    BattleResult,
    /// Sequential with the message-span advancement of dialog text.
    MessageSpan,
    /// Random multi-way jump: one of the next `count` entries (entry+1 .. entry+count).
    RandomBranch,
}

/// One named opcode: verb, operand field bindings, executor and successor declarations.
public sealed record OpcodeDescriptor(
    int Opcode, string Op, OpcodeField[] Fields,
    OpcodeExecutor Executor, OpcodeSuccessor Successor,
    bool AllowLegacyRaw = false, bool DecodeValidatesReferenceRange = false);

/// Table-driven registry for the events.json v1 named opcodes (M5 opcode registry).
/// Decode, validate, and encode all run through the generic paths below; opcode batches
/// only add descriptors and tests. Opcodes without a descriptor stay "raw".
public static class OpcodeRegistry
{
    private static readonly Regex Identifier = new(@"^[A-Za-z_][A-Za-z0-9_-]*\z", RegexOptions.CultureInvariant);

    private static readonly OpcodeField[] NoFields = [];

    private static readonly OpcodeDescriptor[] Registry =
    [
        new(0x0000, "end", [new("mode", OpcodeFieldKind.ConstantString, ConstantString: "keep")], OpcodeExecutor.BothSwitches, OpcodeSuccessor.Stop),
        new(0x0001, "end", [new("mode", OpcodeFieldKind.ConstantString, ConstantString: "advance")], OpcodeExecutor.BothSwitches, OpcodeSuccessor.Stop),
        new(0x0002, "end", [new("mode", OpcodeFieldKind.ConstantString, ConstantString: "redirect"), new("to", OpcodeFieldKind.Reference, 0), new("limit", OpcodeFieldKind.Word, 1)], OpcodeExecutor.BothSwitches, OpcodeSuccessor.ConditionalJump),
        new(0x0003, "goto", [new("to", OpcodeFieldKind.Reference, 0), new("limit", OpcodeFieldKind.Word, 1)], OpcodeExecutor.BothSwitches, OpcodeSuccessor.ConditionalJump),
        new(0x0004, "call", [new("to", OpcodeFieldKind.Reference, 0), new("eventObjectId", OpcodeFieldKind.Word, 1)], OpcodeExecutor.BothSwitches, OpcodeSuccessor.Sequential),
        new(0x0006, "randomJump", [new("rate", OpcodeFieldKind.Word, 0), new("to", OpcodeFieldKind.Reference, 1)], OpcodeExecutor.BothSwitches, OpcodeSuccessor.ConditionalJump),
        new(0x0007, "startBattle",
            [new("enemyTeamId", OpcodeFieldKind.Word, 0), new("lossTarget", OpcodeFieldKind.BattleTarget, 1),
             new("isBoss", OpcodeFieldKind.BossFlag, 2, CoupledTarget: "fleeTarget"), new("fleeTarget", OpcodeFieldKind.BattleTarget, 2)],
            OpcodeExecutor.TriggerSwitchOnly, OpcodeSuccessor.BattleResult, AllowLegacyRaw: true, DecodeValidatesReferenceRange: true),
        new(0x0008, "advanceEntry", NoFields, OpcodeExecutor.TriggerSwitchOnly, OpcodeSuccessor.Sequential),
        new(0x0009, "waitUpdate", [new("frames", OpcodeFieldKind.Word, 0), new("runTriggers", OpcodeFieldKind.Word, 1), new("updateGestures", OpcodeFieldKind.Word, 2)], OpcodeExecutor.BothSwitchesDiffer, OpcodeSuccessor.Sequential),
        new(0x000A, "confirmNoJump", [new("to", OpcodeFieldKind.Reference, 0)], OpcodeExecutor.TriggerSwitchOnly, OpcodeSuccessor.ConditionalJump),
        new(0x000B, "walkStep", [new("direction", OpcodeFieldKind.OpcodeOffset, Base: 0x000B)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x000C, "walkStep", [new("direction", OpcodeFieldKind.OpcodeOffset, Base: 0x000B)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x000D, "walkStep", [new("direction", OpcodeFieldKind.OpcodeOffset, Base: 0x000B)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x000E, "walkStep", [new("direction", OpcodeFieldKind.OpcodeOffset, Base: 0x000B)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x000F, "face", [new("direction", OpcodeFieldKind.Word, 0), new("frame", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0010, "walkTo", [new("x", OpcodeFieldKind.Word, 0), new("y", OpcodeFieldKind.Word, 1), new("h", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0013, "setObjectPosition", [new("eventObjectId", OpcodeFieldKind.Word, 0), new("x", OpcodeFieldKind.Word, 1), new("y", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0014, "setObjectGesture", [new("frame", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0015, "setPartyFacing", [new("direction", OpcodeFieldKind.Word, 0), new("gesture", OpcodeFieldKind.Word, 1), new("memberIndex", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x001B, "changeHp", [new("scope", OpcodeFieldKind.Word, 0), new("amount", OpcodeFieldKind.SignedWord, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x001C, "changeMp", [new("scope", OpcodeFieldKind.Word, 0), new("amount", OpcodeFieldKind.SignedWord, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x001D, "changeHpMp", [new("scope", OpcodeFieldKind.Word, 0), new("amount", OpcodeFieldKind.SignedWord, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0022, "revivePlayer", [new("scope", OpcodeFieldKind.Word, 0), new("healthTenths", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0024, "setAutoEntry", [new("eventObjectId", OpcodeFieldKind.Word, 0), new("entry", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0025, "setTriggerEntry", [new("eventObjectId", OpcodeFieldKind.Word, 0), new("entry", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0030, "boostStat", [new("field", OpcodeFieldKind.Word, 0), new("percent", OpcodeFieldKind.SignedWord, 1), new("roleOperand", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x003B, "setDialog", [new("box", OpcodeFieldKind.ConstantString, ConstantString: "center"), new("color", OpcodeFieldKind.LowByte, 0), new("rngWord", OpcodeFieldKind.Word, 2)], OpcodeExecutor.TriggerSwitchOnly, OpcodeSuccessor.Sequential),
        new(0x003C, "setDialog", [new("box", OpcodeFieldKind.ConstantString, ConstantString: "upper"), new("portraitId", OpcodeFieldKind.Word, 0), new("color", OpcodeFieldKind.LowByte, 1), new("rngWord", OpcodeFieldKind.Word, 2)], OpcodeExecutor.TriggerSwitchOnly, OpcodeSuccessor.Sequential),
        new(0x003D, "setDialog", [new("box", OpcodeFieldKind.ConstantString, ConstantString: "lower"), new("portraitId", OpcodeFieldKind.Word, 0), new("color", OpcodeFieldKind.LowByte, 1), new("rngWord", OpcodeFieldKind.Word, 2)], OpcodeExecutor.TriggerSwitchOnly, OpcodeSuccessor.Sequential),
        new(0x003E, "setDialog", [new("box", OpcodeFieldKind.ConstantString, ConstantString: "centerWindow"), new("color", OpcodeFieldKind.LowByte, 0)], OpcodeExecutor.TriggerSwitchOnly, OpcodeSuccessor.Sequential),
        new(0x0040, "setTriggerMode", [new("eventObjectId", OpcodeFieldKind.Word, 0), new("mode", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0041, "markFailed", NoFields, OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0043, "setMusic", [new("musicId", OpcodeFieldKind.Word, 0), new("mode", OpcodeFieldKind.Word, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0046, "setPartyPos", [new("x", OpcodeFieldKind.Word, 0), new("y", OpcodeFieldKind.Word, 1), new("h", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0049, "setState", [new("eventObjectId", OpcodeFieldKind.Word, 0), new("state", OpcodeFieldKind.SignedWord, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x004A, "setBattlefield", [new("battlefieldId", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x004D, "waitForAnyKey", NoFields, OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x004E, "requestReload", NoFields, OpcodeExecutor.Shared, OpcodeSuccessor.Stop),
        new(0x004F, "fadeToRed", NoFields, OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0050, "fadeOut", [new("frames", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0052, "hide", [new("frames", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0058, "itemShortageJump", [new("itemId", OpcodeFieldKind.Word, 0), new("count", OpcodeFieldKind.SignedWord, 1), new("to", OpcodeFieldKind.Reference, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        new(0x005D, "playerLacksPoisonJump", [new("objectId", OpcodeFieldKind.Word, 0), new("to", OpcodeFieldKind.Reference, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        new(0x005E, "enemyLacksPoisonJump", [new("objectId", OpcodeFieldKind.Word, 0), new("to", OpcodeFieldKind.Reference, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        new(0x0061, "notPoisonedJump", [new("to", OpcodeFieldKind.Reference, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        new(0x0064, "enemyHpAboveJump", [new("percent", OpcodeFieldKind.Word, 0), new("to", OpcodeFieldKind.Reference, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        new(0x0068, "enemyTurnJump", [new("to", OpcodeFieldKind.Reference, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        // #99 review correction: script.c:3295 reads the delay from operand 1 ((0 -> 60) ms
        // normalization at runtime) and the gesture flag from operand 2; operand 0 is unused.
        new(0x0005, "redraw", [new("delayUnits", OpcodeFieldKind.Word, 1), new("updateGestures", OpcodeFieldKind.Word, 2)], OpcodeExecutor.TriggerSwitchOnly, OpcodeSuccessor.Sequential),
        new(0x006D, "setSceneScripts", [new("sceneId", OpcodeFieldKind.Word, 0), new("onEnter", OpcodeFieldKind.Word, 1), new("onTeleport", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0073, "fadeScene", [new("mode", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0074, "partyHpNotFullJump", [new("to", OpcodeFieldKind.Reference, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        new(0x0075, "setParty", [new("roleId0", OpcodeFieldKind.Word, 0), new("roleId1", OpcodeFieldKind.Word, 1), new("roleId2", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0078, "noop", NoFields, OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0079, "roleInPartyJump", [new("roleNameWord", OpcodeFieldKind.Word, 0), new("to", OpcodeFieldKind.Reference, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        new(0x0081, "notFacingObjectJump", [new("eventObjectId", OpcodeFieldKind.Word, 0), new("zoneUnits", OpcodeFieldKind.Word, 1), new("to", OpcodeFieldKind.Reference, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        new(0x0083, "objectOutOfZoneJump", [new("eventObjectId", OpcodeFieldKind.Word, 0), new("zoneUnits", OpcodeFieldKind.Word, 1), new("to", OpcodeFieldKind.Reference, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        new(0x0085, "delay", [new("delayUnits", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0086, "itemNotEquippedJump", [new("itemId", OpcodeFieldKind.Word, 0), new("count", OpcodeFieldKind.Word, 1), new("to", OpcodeFieldKind.Reference, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        new(0x008E, "restoreScreen", NoFields, OpcodeExecutor.TriggerSwitchOnly, OpcodeSuccessor.Sequential),
        new(0x0091, "notFirstOfKindJump", [new("to", OpcodeFieldKind.Reference, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        new(0x0094, "stateMatchJump", [new("eventObjectId", OpcodeFieldKind.Word, 0), new("state", OpcodeFieldKind.SignedWord, 1), new("to", OpcodeFieldKind.Reference, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        new(0x0095, "sceneMatchJump", [new("sceneId", OpcodeFieldKind.Word, 0), new("to", OpcodeFieldKind.Reference, 1)], OpcodeExecutor.Shared, OpcodeSuccessor.ConditionalJump),
        new(0x00A0, "quitGame", NoFields, OpcodeExecutor.Shared, OpcodeSuccessor.Stop),
        new(0x00A2, "randomBranch", [new("count", OpcodeFieldKind.Word, 0)], OpcodeExecutor.Shared, OpcodeSuccessor.RandomBranch),
        new(0x00A6, "backupScreen", NoFields, OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x0090, "setObjectScript", [new("objectId", OpcodeFieldKind.Word, 0), new("value", OpcodeFieldKind.Word, 1), new("slotIndex", OpcodeFieldKind.Word, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0x009A, "setStateRange", [new("fromEventObjectId", OpcodeFieldKind.Word, 0), new("toEventObjectId", OpcodeFieldKind.Word, 1), new("state", OpcodeFieldKind.SignedWord, 2)], OpcodeExecutor.Shared, OpcodeSuccessor.Sequential),
        new(0xFFFF, "showDialog", [new("messageId", OpcodeFieldKind.Word, 0), new("descriptionLineWord", OpcodeFieldKind.Word, 1)], OpcodeExecutor.BothSwitchesDiffer, OpcodeSuccessor.MessageSpan),
    ];

    /// All registered descriptors keyed by opcode.
    public static IReadOnlyDictionary<int, OpcodeDescriptor> ByOpcode { get; } =
        Registry.ToDictionary(descriptor => descriptor.Opcode);

    /// All registered verb names, including "raw".
    public static IReadOnlyCollection<string> OpNames { get; } =
        Registry.Select(descriptor => descriptor.Op).Append("raw").ToHashSet();

    public static bool IsRegistered(int opcode) => ByOpcode.ContainsKey(opcode);

    public static bool IsIdentifier(string value) => Identifier.IsMatch(value);

    public static string LabelAt(int address) => "L" + address.ToString("X4");

    /// Decodes one instruction into a command object: label, named fields (or "raw"),
    /// WORD-order next, and the source triple.
    public static JsonObject DecodeCommand(int index, int count, ushort opcode, ushort a, ushort b, ushort c)
    {
        ushort[] operands = [a, b, c];
        var result = new JsonObject { ["label"] = LabelAt(index) };
        if (ByOpcode.TryGetValue(opcode, out OpcodeDescriptor? descriptor))
        {
            result["op"] = descriptor.Op;
            foreach (OpcodeField field in descriptor.Fields)
            {
                switch (field.Kind)
                {
                    case OpcodeFieldKind.Word:
                        result[field.Name] = (int)operands[field.Operand];
                        break;
                    case OpcodeFieldKind.SignedWord:
                        result[field.Name] = (int)(short)operands[field.Operand];
                        break;
                    case OpcodeFieldKind.LowByte:
                        result[field.Name] = (int)(operands[field.Operand] & 255);
                        break;
                    case OpcodeFieldKind.Reference:
                        result[field.Name] = LabelAt(operands[field.Operand]);
                        break;
                    case OpcodeFieldKind.BattleTarget:
                        if (descriptor.DecodeValidatesReferenceRange && operands[field.Operand] != 0 && operands[field.Operand] >= count)
                            throw new InvalidDataException($"SSS.MKF: {descriptor.Op} @{index:X4} result target is outside chunk 4.");
                        result[field.Name] = operands[field.Operand] == 0 ? null : LabelAt(operands[field.Operand]);
                        break;
                    case OpcodeFieldKind.BossFlag:
                        result[field.Name] = operands[field.Operand] == 0;
                        break;
                    case OpcodeFieldKind.OpcodeOffset:
                        result[field.Name] = opcode - field.Base;
                        break;
                    case OpcodeFieldKind.ConstantString:
                        result[field.Name] = field.ConstantString;
                        break;
                }
            }
        }
        else result["op"] = "raw";
        result["next"] = (index + 1 < count || count == 65536) ? LabelAt((index + 1) & 0xffff) : null;
        result["source"] = new JsonObject
        {
            ["index"] = index, ["opcode"] = (int)opcode,
            ["operands"] = new JsonArray((int)a, (int)b, (int)c),
        };
        return result;
    }

    /// Strictly validates one command against the registry: identifiers, op vocabulary,
    /// source triple, descriptor mapping, allowed keys, and per-field declarations.
    public static void ValidateCommand(JsonObject command, string fileId, int count)
    {
        string label = StrictJson.String(command, "label");
        if (!IsIdentifier(label)) throw new InvalidDataException($"{fileId}.json: invalid command label.");
        string op = StrictJson.String(command, "op");
        if (!OpNames.Contains(op)) throw new InvalidDataException($"{fileId}.json: unsupported op {op}.");
        string? next = StrictJson.NullableString(command, "next");
        if (next is not null) ValidateReferenceSyntax(next, $"{label}.next");
        var source = StrictJson.Required(command, "source");
        StrictJson.ValidateKeys(source, "source", "index", "opcode", "operands");
        int index = StrictJson.Int(source, "index"), opcodeValue = StrictJson.Int(source, "opcode");
        if (index < 0 || index >= count || opcodeValue is < 0 or > 65535) throw new InvalidDataException($"{label}: source index/opcode out of range.");
        var operands = StrictJson.RequiredArray(source, "operands");
        if (operands.Count != 3) throw new InvalidDataException($"{label}: source.operands must contain exactly three WORD values.");
        foreach (var operand in operands) StrictJson.Word(operand);
        if (op == "raw")
        {
            if (ByOpcode.TryGetValue(opcodeValue, out OpcodeDescriptor? registered) && !registered.AllowLegacyRaw)
                throw new InvalidDataException($"{label}: registered opcode must use its named mapping.");
            StrictJson.ValidateKeys(command, label, "label", "op", "next", "source");
            return;
        }
        if (!ByOpcode.TryGetValue(opcodeValue, out OpcodeDescriptor? descriptor) || descriptor.Op != op)
            throw new InvalidDataException($"{label}: invalid {op} mapping.");
        var allowed = new List<string> { "label", "op" };
        allowed.AddRange(descriptor.Fields.Select(field => field.Name));
        allowed.AddRange(["next", "source"]);
        StrictJson.ValidateKeys(command, label, allowed.ToArray());
        foreach (OpcodeField field in descriptor.Fields)
        {
            switch (field.Kind)
            {
                case OpcodeFieldKind.Reference:
                    ValidateReferenceSyntax(StrictJson.String(command, field.Name), $"{label}.{field.Name}");
                    break;
                case OpcodeFieldKind.BattleTarget:
                    string? target = StrictJson.NullableString(command, field.Name);
                    if (target is not null) ValidateReferenceSyntax(target, $"{label}.{field.Name}");
                    break;
                case OpcodeFieldKind.BossFlag:
                    string context = $"{fileId}.json#{label} @{index:X4}";
                    if (command[field.Name] is not JsonValue value || !value.TryGetValue<bool>(out bool flag))
                        throw new InvalidDataException($"{context}: {descriptor.Op}.{field.Name} must be a boolean.");
                    if (field.CoupledTarget is string coupled && flag != (StrictJson.NullableString(command, coupled) is null))
                        throw new InvalidDataException($"{context}: {descriptor.Op}.{field.Name} must equal {coupled} being null.");
                    break;
            }
        }
    }

    /// Re-encodes one command into its 8 little-endian bytes. Mapped operands are
    /// re-encoded from the semantic fields; unmapped operands and the low-byte fields'
    /// high bits keep their source WORD values.
    public static void EncodeCommand(JsonObject command, string fileId, IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> labelsByFile, Span<byte> destination)
    {
        string label = StrictJson.String(command, "label");
        var source = StrictJson.Required(command, "source");
        ushort opcode = checked((ushort)StrictJson.Int(source, "opcode"));
        var operands = StrictJson.RequiredArray(source, "operands");
        ushort a = StrictJson.Word(operands[0]), b = StrictJson.Word(operands[1]), c = StrictJson.Word(operands[2]);
        string op = StrictJson.String(command, "op");
        if (op != "raw")
        {
            if (!ByOpcode.TryGetValue(opcode, out OpcodeDescriptor? descriptor) || descriptor.Op != op)
                throw new InvalidDataException($"{label}: op does not match registered source opcode.");
            ushort[] words = [a, b, c];
            var bossFlags = new List<(OpcodeField Field, bool Flag)>();
            foreach (OpcodeField field in descriptor.Fields)
            {
                switch (field.Kind)
                {
                    case OpcodeFieldKind.Word:
                        words[field.Operand] = checked((ushort)StrictJson.Int(command, field.Name));
                        break;
                    case OpcodeFieldKind.SignedWord:
                        words[field.Operand] = unchecked((ushort)CheckedSigned(command, field.Name));
                        break;
                    case OpcodeFieldKind.LowByte:
                        int low = StrictJson.Int(command, field.Name);
                        if (low is < 0 or > 255) throw new InvalidDataException($"{descriptor.Op}.{field.Name} must be 0..255.");
                        words[field.Operand] = (ushort)((words[field.Operand] & 0xff00) | low);
                        break;
                    case OpcodeFieldKind.Reference:
                        words[field.Operand] = EncodeReference(command, field.Name, fileId, labelsByFile);
                        break;
                    case OpcodeFieldKind.BattleTarget:
                        words[field.Operand] = EncodeBattleTarget(command, field.Name, fileId, labelsByFile);
                        break;
                    case OpcodeFieldKind.BossFlag:
                        if (command[field.Name] is not JsonValue value || !value.TryGetValue<bool>(out bool flag))
                            throw new InvalidDataException($"{label}: {descriptor.Op}.{field.Name} must be a boolean.");
                        bossFlags.Add((field, flag));
                        break;
                    case OpcodeFieldKind.OpcodeOffset:
                        if (opcode != field.Base + StrictJson.Int(command, field.Name))
                            throw new InvalidDataException($"{label}: {descriptor.Op} {field.Name} does not match its opcode.");
                        break;
                    case OpcodeFieldKind.ConstantString:
                        if (StrictJson.String(command, field.Name) != field.ConstantString)
                            throw new InvalidDataException($"{label}: invalid {descriptor.Op} mapping.");
                        break;
                }
            }
            foreach (var (field, flag) in bossFlags)
                if (field.CoupledTarget is string coupled &&
                    descriptor.Fields.FirstOrDefault(other => other.Name == coupled) is { Operand: int coupledOperand } &&
                    flag != (words[coupledOperand] == 0))
                    throw new InvalidDataException($"{label}: {descriptor.Op}.{field.Name} must equal {coupled} being null.");
            a = words[0]; b = words[1]; c = words[2];
        }
        Write(destination, opcode); Write(destination[2..], a); Write(destination[4..], b); Write(destination[6..], c);
    }

    public static int ResolveReference(string reference, string currentFile, IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> labelsByFile, string context)
    {
        int separator = reference.IndexOf('#');
        string fileId = currentFile;
        string label = reference;
        if (separator >= 0)
        {
            if (separator == 0 || separator == reference.Length - 1 || reference.IndexOf('#', separator + 1) >= 0)
                throw new InvalidDataException($"{context}: malformed file#label reference '{reference}'.");
            fileId = reference[..separator];
            label = reference[(separator + 1)..];
        }
        if (!IsIdentifier(fileId) || !IsIdentifier(label))
            throw new InvalidDataException($"{context}: malformed reference '{reference}'.");
        if (!labelsByFile.TryGetValue(fileId, out var labels) || !labels.TryGetValue(label, out int address))
            throw new InvalidDataException($"{context}: dangling reference '{reference}'.");
        return address;
    }

    public static void ValidateReferenceSyntax(string reference, string context)
    {
        int separator = reference.IndexOf('#');
        if (separator < 0)
        {
            if (!IsIdentifier(reference)) throw new InvalidDataException($"{context}: malformed reference '{reference}'.");
            return;
        }
        if (separator == 0 || separator == reference.Length - 1 || reference.IndexOf('#', separator + 1) >= 0
            || !IsIdentifier(reference[..separator]) || !IsIdentifier(reference[(separator + 1)..]))
            throw new InvalidDataException($"{context}: malformed file#label reference '{reference}'.");
    }

    private static int CheckedSigned(JsonObject command, string field)
    {
        int value = StrictJson.Int(command, field);
        return value is >= -32768 and <= 32767 ? value : throw new InvalidDataException($"{StrictJson.String(command, "label")}.{field} must be a signed WORD.");
    }

    private static ushort EncodeReference(JsonObject command, string field, string fileId, IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> labelsByFile)
    {
        string reference = StrictJson.NullableString(command, field) ?? throw new InvalidDataException($"{StrictJson.String(command, "label")}.{field} must be a reference.");
        string context = $"{fileId}.json#{StrictJson.String(command, "label")} @{StrictJson.Int(StrictJson.Required(command, "source"), "index"):X4}.{field}";
        return checked((ushort)ResolveReference(reference, fileId, labelsByFile, context));
    }

    private static ushort EncodeBattleTarget(JsonObject command, string field, string fileId, IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> labelsByFile)
    {
        string? reference = StrictJson.NullableString(command, field);
        if (reference is null) return 0;
        string context = $"{fileId}.json#{StrictJson.String(command, "label")} @{StrictJson.Int(StrictJson.Required(command, "source"), "index"):X4}.{field}";
        int address = ResolveReference(reference, fileId, labelsByFile, context);
        if (address == 0) throw new InvalidDataException($"{context}: address zero must use null, not a reference.");
        return checked((ushort)address);
    }

    private static void Write(Span<byte> bytes, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
}
