using System.Text.Json;

namespace Pal.Core;

/// <summary>Content shapes only. Registration here never grants trigger or auto execution.</summary>
internal static class TriggerNamedContentProfile
{
    private enum FieldKind { Word, Reference, NullableReference }
    private sealed record Field(string Name, FieldKind Kind);
    private sealed record Shape(int Opcode, string Op, Field[] Fields);

    private static readonly Shape[] Shapes =
    [
        new(0x0004, "call", [new("to", FieldKind.Reference), new("eventObjectId", FieldKind.Word)]),
        new(0x0020, "removeItem", [new("itemId", FieldKind.Word), new("count", FieldKind.Word), new("shortageTarget", FieldKind.NullableReference)]),
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
