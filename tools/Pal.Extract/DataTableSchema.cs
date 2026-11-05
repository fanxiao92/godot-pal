using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Pal.Extract;

/// Element type of an extracted data-table field (data-schema v1).
public enum DataFieldType
{
    /// Unsigned WORD, 0..65535.
    Word,
    /// Signed WORD (C SHORT), -32768..32767.
    SignedWord,
}

/// One field of a data-table record: a scalar WORD, a row-major array of WORDs
/// (nested dimensions, last index varies fastest, matching the C layout), or - when
/// Members is set - a repeated sub-record array of objects (one per dimension element,
/// members in declaration order; LEVELUPMAGIC's m[5], #90). A Members field declares
/// no word Type of its own; its members are plain scalar/array fields.
public sealed record DataField(string Name, DataFieldType? Type, int[]? Dimensions = null, DataField[]? Members = null)
{
    internal int WordCount => Members is { } members
        ? members.Sum(member => member.WordCount) * RepeatCount
        : RepeatCount;

    private int RepeatCount => Dimensions is null ? 1 : Dimensions.Aggregate(1, (total, size) => total * size);
}

/// One exported view of a shared dispatched WORD slot (data-schema v1, #78): Name is
/// the JSON field a record exports the slot as, Type that view's signedness, and
/// MatchValue the dispatch field value that selects this view. A null MatchValue is
/// the fallback view for every other dispatch value; at most one option may declare it.
public sealed record DispatchedWordOption(string Name, DataFieldType Type, int? MatchValue = null);

/// A single shared WORD of the record layout whose exported field name and signedness
/// are selected per record (MAGIC's rgSpecific union, #78): the option whose MatchValue
/// equals the decoded value of DispatchFieldName - a scalar WORD field decoded before
/// SlotWordIndex - wins, the null-MatchValue fallback covers every other value. A
/// record exports exactly the selected option's name, never two and never none; the
/// strict validator enforces the same rule on reload.
public sealed record DispatchedWordSlot(string CName, string DispatchFieldName, int SlotWordIndex, DispatchedWordOption[] Options);

/// Structure descriptor for one extracted data table; the record count is derived from
/// the source chunk byte length divided by the descriptor's record size (data-schema v1).
/// DispatchedSlot optionally declares one shared WORD viewed under per-record names.
public sealed record DataTableDescriptor(string FileId, string Archive, int Chunk, DataField[] Fields, DispatchedWordSlot? DispatchedSlot = null)
{
    public int RecordByteSize => (Fields.Sum(entry => entry.WordCount) + (DispatchedSlot is null ? 0 : 1)) * 2;
}

/// Shared skeleton for the M5 data-table extraction tickets (#79–#91): structure
/// descriptor -> strict little-endian decode -> JSON document -> index registration,
/// plus the strict validator used for reload checks. Tables are read-only: there is
/// no re-encoder back to raw bytes (ADR-0004).
public static class DataTableSchema
{
    /// Decodes a whole source chunk into the strict per-table document
    /// (formatVersion/profile/fileId/records). Rejects lengths that are not a
    /// positive multiple of the record size, locating the source archive and chunk.
    public static JsonObject DecodeTable(DataTableDescriptor descriptor, byte[] chunk)
    {
        int recordSize = descriptor.RecordByteSize;
        if (recordSize == 0) throw new InvalidDataException($"{descriptor.Archive}: chunk {descriptor.Chunk}: table descriptor declares no fields.");
        if (chunk.Length == 0 || chunk.Length % recordSize != 0)
            throw new InvalidDataException($"{descriptor.Archive}: chunk {descriptor.Chunk}: byte length {chunk.Length} is not a positive multiple of the {recordSize}-byte record size.");
        ValidateSlotPlacement(descriptor);
        var records = new JsonArray();
        for (int offset = 0; offset < chunk.Length; offset += recordSize)
        {
            var record = new JsonObject();
            int cursor = offset;
            int wordIndex = 0;
            foreach (DataField field in descriptor.Fields)
            {
                DecodeSlotIfDue(descriptor, record, chunk, ref cursor, ref wordIndex);
                record[field.Name] = DecodeField(chunk, ref cursor, field, 0);
                wordIndex += field.WordCount;
            }
            DecodeSlotIfDue(descriptor, record, chunk, ref cursor, ref wordIndex);
            records.Add(record);
        }
        return new JsonObject
        {
            ["formatVersion"] = 1,
            ["profile"] = "faithful",
            ["fileId"] = descriptor.FileId,
            ["records"] = records,
        };
    }

    /// Builds the index.json registration entry for one table: source archive, chunk,
    /// derived record count, byte length, and the SHA-256 of the chunk bytes.
    public static JsonObject DescribeIndexEntry(DataTableDescriptor descriptor, byte[] chunk)
    {
        int recordSize = descriptor.RecordByteSize;
        if (chunk.Length == 0 || chunk.Length % recordSize != 0)
            throw new InvalidDataException($"{descriptor.Archive}: chunk {descriptor.Chunk}: byte length {chunk.Length} is not a positive multiple of the {recordSize}-byte record size.");
        return new JsonObject
        {
            ["file"] = descriptor.FileId + ".json",
            ["archive"] = descriptor.Archive,
            ["chunk"] = descriptor.Chunk,
            ["recordCount"] = chunk.Length / recordSize,
            ["byteLength"] = chunk.Length,
            ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(chunk)),
        };
    }

    /// Strictly validates a decoded table document against the descriptor:
    /// version/profile/fileId, unknown or missing fields, array dimensions, and
    /// WORD/signed WORD ranges are all enforced with the record index in the message.
    public static void ValidateDocument(DataTableDescriptor descriptor, JsonObject document)
    {
        ValidateSlotPlacement(descriptor);
        StrictJson.ValidateKeys(document, descriptor.FileId, "formatVersion", "profile", "fileId", "records");
        if (StrictJson.Int(document, "formatVersion") != 1 || StrictJson.String(document, "profile") != "faithful")
            throw new InvalidDataException($"{descriptor.FileId}.json: unsupported formatVersion or profile.");
        if (StrictJson.String(document, "fileId") != descriptor.FileId)
            throw new InvalidDataException($"{descriptor.FileId}.json: fileId does not match the table.");
        var records = StrictJson.RequiredArray(document, "records");
        for (int index = 0; index < records.Count; index++)
        {
            var record = records[index]?.AsObject() ?? throw new InvalidDataException($"{descriptor.FileId}.json: record {index} must be an object.");
            string context = $"{descriptor.FileId}.json record {index}";
            // A dispatched slot selects its one exported name per record from the dispatch
            // value before the key check, so the other view's name is unknown there and the
            // selected one is required - both views or the wrong view cannot pass.
            DispatchedWordOption? selected = null;
            if (descriptor.DispatchedSlot is { } slot)
                selected = SelectOption(slot, ReadDispatchValue(record, context, slot));
            string[] allowed = selected is null
                ? descriptor.Fields.Select(field => field.Name).ToArray()
                : [.. descriptor.Fields.Select(field => field.Name), selected.Name];
            StrictJson.ValidateKeys(record, context, allowed);
            foreach (DataField field in descriptor.Fields) ValidateValue(record, context, field, record[field.Name], 0);
            if (selected is not null) ValidateValue(record, context, new DataField(selected.Name, selected.Type), record[selected.Name], 0);
        }
    }

    private static JsonNode DecodeField(byte[] chunk, ref int offset, DataField field, int dimension)
    {
        // A leaf Members field decodes one sub-record object (or, without dimensions,
        // the single object itself); a leaf word field decodes its one WORD.
        if (field.Dimensions is null || dimension == field.Dimensions.Length)
        {
            if (field.Members is { } members)
            {
                var element = new JsonObject();
                foreach (DataField member in members) element[member.Name] = DecodeField(chunk, ref offset, member, 0);
                return element;
            }
            if (field.Type is null)
                throw new InvalidDataException($"field '{field.Name}' declares neither a word type nor member fields.");
            ushort word = BinaryPrimitives.ReadUInt16LittleEndian(chunk.AsSpan(offset));
            offset += 2;
            return field.Type == DataFieldType.SignedWord ? (short)word : (int)word;
        }
        var array = new JsonArray();
        for (int index = 0; index < field.Dimensions[dimension]; index++) array.Add(DecodeField(chunk, ref offset, field, dimension + 1));
        return array;
    }

    // Decodes the dispatched slot once the cursor reaches its word, after the dispatch
    // field has been decoded; the emitted field keeps the slot's physical position in
    // the JSON key order.
    private static void DecodeSlotIfDue(DataTableDescriptor descriptor, JsonObject record, byte[] chunk, ref int offset, ref int wordIndex)
    {
        if (descriptor.DispatchedSlot is not { } slot || wordIndex != slot.SlotWordIndex) return;
        DispatchedWordOption option = SelectOption(slot, record[slot.DispatchFieldName]?.GetValue<int>()
            ?? throw new InvalidDataException($"dispatched slot '{slot.CName}': dispatch field '{slot.DispatchFieldName}' is not decoded yet."));
        ushort word = BinaryPrimitives.ReadUInt16LittleEndian(chunk.AsSpan(offset));
        offset += 2;
        record[option.Name] = option.Type == DataFieldType.SignedWord ? (short)word : (int)word;
        wordIndex++;
    }

    // Selects the option a record exports the slot as: the first exact MatchValue hit,
    // else the single fallback option; no match and no fallback is not a decodable record.
    private static DispatchedWordOption SelectOption(DispatchedWordSlot slot, int dispatchValue)
    {
        foreach (DispatchedWordOption option in slot.Options) if (option.MatchValue == dispatchValue) return option;
        return slot.Options.FirstOrDefault(option => option.MatchValue is null)
            ?? throw new InvalidDataException($"dispatched slot '{slot.CName}': dispatch value {dispatchValue} selects no exported view.");
    }

    private static int ReadDispatchValue(JsonObject record, string context, DispatchedWordSlot slot)
    {
        JsonNode? node = record[slot.DispatchFieldName];
        try { return node?.GetValue<int>() ?? throw new InvalidDataException($"{context}.{slot.DispatchFieldName} must be an integer."); }
        catch (InvalidOperationException) { throw new InvalidDataException($"{context}.{slot.DispatchFieldName} must be an integer."); }
        catch (FormatException) { throw new InvalidDataException($"{context}.{slot.DispatchFieldName} must be an integer."); }
    }

    // Rejects inconsistent dispatched-slot descriptors before any record is decoded: the
    // slot's word index must land on a field boundary, the dispatch field must be a
    // scalar WORD decoded before the slot, and option names must be unique without
    // colliding with field names or stacking fallbacks.
    private static void ValidateSlotPlacement(DataTableDescriptor descriptor)
    {
        if (descriptor.DispatchedSlot is not { } slot) return;
        string location = $"{descriptor.Archive}: chunk {descriptor.Chunk}: dispatched slot '{slot.CName}'";
        if (slot.SlotWordIndex < 0 || slot.Options.Length == 0)
            throw new InvalidDataException($"{location} declares no usable option.");
        if (slot.Options.Count(option => option.MatchValue is null) > 1)
            throw new InvalidDataException($"{location} declares more than one fallback view.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (DispatchedWordOption option in slot.Options)
        {
            if (!names.Add(option.Name)) throw new InvalidDataException($"{location} declares view '{option.Name}' twice.");
            if (descriptor.Fields.Any(field => field.Name == option.Name))
                throw new InvalidDataException($"{location} view '{option.Name}' collides with a field name.");
        }
        int wordIndex = 0, dispatchEnd = -1;
        DataField? dispatch = null;
        bool aligned = false;
        foreach (DataField field in descriptor.Fields)
        {
            if (wordIndex == slot.SlotWordIndex) aligned = true;
            if (field.Name == slot.DispatchFieldName) { dispatch = field; dispatchEnd = wordIndex + field.WordCount; }
            wordIndex += field.WordCount;
        }
        if (wordIndex == slot.SlotWordIndex) aligned = true;
        if (!aligned)
            throw new InvalidDataException($"{location} word index {slot.SlotWordIndex} does not fall on a field boundary.");
        if (dispatch is null || dispatch.Dimensions is not null || dispatch.Type != DataFieldType.Word || dispatchEnd > slot.SlotWordIndex)
            throw new InvalidDataException($"{location} dispatch field '{slot.DispatchFieldName}' must be a scalar WORD before the slot.");
    }

    private static void ValidateValue(JsonObject record, string context, DataField field, JsonNode? node, int dimension)
    {
        string location = $"{context}.{field.Name}";
        if (field.Dimensions is null || dimension == field.Dimensions.Length)
        {
            if (field.Members is { } members)
            {
                // One sub-record object: exactly the declared member keys, each member
                // then range-checked as its own field.
                JsonObject element;
                try { element = node?.AsObject() ?? throw new InvalidDataException($"{location} must be an object."); }
                catch (InvalidOperationException) { throw new InvalidDataException($"{location} must be an object."); }
                StrictJson.ValidateKeys(element, location, members.Select(member => member.Name).ToArray());
                foreach (DataField member in members) ValidateValue(record, context, member, element[member.Name], 0);
                return;
            }
            if (field.Type is null)
                throw new InvalidDataException($"{location} field declares neither a word type nor member fields.");
            int value;
            try { value = node?.GetValue<int>() ?? throw new InvalidDataException($"{location} must be an integer."); }
            catch (InvalidOperationException) { throw new InvalidDataException($"{location} must be an integer."); }
            catch (FormatException) { throw new InvalidDataException($"{location} must be an integer."); }
            if (field.Type == DataFieldType.Word && value is < 0 or > 65535)
                throw new InvalidDataException($"{location} must be a WORD (0..65535).");
            if (field.Type == DataFieldType.SignedWord && value is < -32768 or > 32767)
                throw new InvalidDataException($"{location} must be a signed WORD (-32768..32767).");
            return;
        }
        var array = node?.AsArray() ?? throw new InvalidDataException($"{location} must be an array.");
        if (array.Count != field.Dimensions[dimension])
            throw new InvalidDataException($"{location} must have {field.Dimensions[dimension]} elements at dimension {dimension}.");
        foreach (JsonNode? child in array) ValidateValue(record, context, field, child, dimension + 1);
    }
}
