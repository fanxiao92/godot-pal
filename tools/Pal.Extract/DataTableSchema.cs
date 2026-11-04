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

/// One field of a data-table record: a scalar WORD or a row-major array of WORDs
/// (nested dimensions, last index varies fastest, matching the C layout).
public sealed record DataField(string Name, DataFieldType Type, int[]? Dimensions = null)
{
    internal int WordCount => Dimensions is null ? 1 : Dimensions.Aggregate(1, (total, size) => total * size);
}

/// Structure descriptor for one extracted data table; the record count is derived from
/// the source chunk byte length divided by the descriptor's record size (data-schema v1).
public sealed record DataTableDescriptor(string FileId, string Archive, int Chunk, DataField[] Fields)
{
    public int RecordByteSize => Fields.Sum(entry => entry.WordCount) * 2;
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
        var records = new JsonArray();
        for (int offset = 0; offset < chunk.Length; offset += recordSize)
        {
            var record = new JsonObject();
            int cursor = offset;
            foreach (DataField field in descriptor.Fields) record[field.Name] = DecodeField(chunk, ref cursor, field, 0);
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
            StrictJson.ValidateKeys(record, context, descriptor.Fields.Select(field => field.Name).ToArray());
            foreach (DataField field in descriptor.Fields) ValidateValue(record, context, field, record[field.Name], 0);
        }
    }

    private static JsonNode DecodeField(byte[] chunk, ref int offset, DataField field, int dimension)
    {
        if (field.Dimensions is null || dimension == field.Dimensions.Length)
        {
            ushort word = BinaryPrimitives.ReadUInt16LittleEndian(chunk.AsSpan(offset));
            offset += 2;
            return field.Type == DataFieldType.SignedWord ? (short)word : (int)word;
        }
        var array = new JsonArray();
        for (int index = 0; index < field.Dimensions[dimension]; index++) array.Add(DecodeField(chunk, ref offset, field, dimension + 1));
        return array;
    }

    private static void ValidateValue(JsonObject record, string context, DataField field, JsonNode? node, int dimension)
    {
        string location = $"{context}.{field.Name}";
        if (field.Dimensions is null || dimension == field.Dimensions.Length)
        {
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
