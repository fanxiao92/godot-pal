using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pal.Extract;

// export-data: opens SSS.MKF and DATA.MKF and extracts the registered read-only data
// tables (data-schema v1). Each M5 ticket (#79-#91) registers its descriptor in
// DataTables and appends it to RegisteredTables; index.json lists tables in
// lexicographic file order regardless of registration order. Tables are read-only:
// no re-encoder back to raw bytes (ADR-0004).
internal static class DataGameData
{
    private const int MaximumChunkBytes = 64 * 1024 * 1024;

    // Registered tables in source order (SSS chunks first, then DATA chunks).
    private static readonly DataTableDescriptor[] RegisteredTables = [DataTables.EventObject, DataTables.Scene];

    internal static void Export(string input, string output)
    {
        string destination = ExportPaths.Validate(input, output, ["SSS.MKF", "DATA.MKF"]);
        var archives = MkfArchive.OpenRequired(input, ["SSS.MKF", "DATA.MKF"]);
        try
        {
            var archiveByName = archives.ToDictionary(archive => archive.Name, StringComparer.Ordinal);
            var sources = new JsonArray();
            foreach (MkfArchive archive in archives)
                sources.Add(new JsonObject { ["archive"] = archive.Name, ["sha256"] = archive.Sha256 });
            // Decode and strictly validate every registered table before writing anything,
            // so a failing table leaves no partial output.
            var tables = new JsonArray();
            var files = new List<(string Name, JsonObject Document)>();
            foreach (DataTableDescriptor descriptor in RegisteredTables.OrderBy(table => table.FileId, StringComparer.Ordinal))
            {
                byte[] chunk = archiveByName[descriptor.Archive].ReadChunk(descriptor.Chunk, MaximumChunkBytes);
                var document = DataTableSchema.DecodeTable(descriptor, chunk);
                DataTableSchema.ValidateDocument(descriptor, document);
                tables.Add(DataTableSchema.DescribeIndexEntry(descriptor, chunk));
                files.Add((descriptor.FileId + ".json", document));
            }
            var indexDocument = new JsonObject
            {
                ["formatVersion"] = 1,
                ["profile"] = "faithful",
                ["sources"] = sources,
                ["tables"] = tables,
            };
            Directory.CreateDirectory(destination);
            foreach (var (name, document) in files) WriteJson(Path.Combine(destination, name), document);
            WriteJson(Path.Combine(destination, "index.json"), indexDocument);
        }
        finally
        {
            foreach (MkfArchive archive in archives) archive.Dispose();
        }
    }

    private static void WriteJson(string path, JsonObject value) => File.WriteAllText(path, value.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
}
