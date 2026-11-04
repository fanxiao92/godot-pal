using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pal.Extract;

// export-data: opens SSS.MKF and DATA.MKF and extracts the registered read-only data
// tables (data-schema v1). Each M5 ticket (#79–#91) registers its descriptor in
// DataTables and appends it to RegisteredTables; the OBJECT union table (#81/#82) is
// view-aware and exports through ObjectUnionTable instead of a flat descriptor.
// index.json lists tables in lexicographic file order regardless of registration
// order. Tables are read-only: no re-encoder back to raw bytes (ADR-0004).
internal static class DataGameData
{
    private const int MaximumChunkBytes = 64 * 1024 * 1024;

    // Registered tables in source order (SSS chunks first, then DATA chunks).
    private static readonly DataTableDescriptor[] RegisteredTables = [DataTables.EventObject, DataTables.Scene, DataTables.Store, DataTables.Enemy];

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
            // so a failing table leaves no partial output. Index entries are collected and
            // emitted in lexicographic file order regardless of registration order.
            var tables = new JsonArray();
            var files = new List<(string Name, JsonObject Document)>();
            var entries = new List<(string Name, JsonObject Entry)>();
            foreach (DataTableDescriptor descriptor in RegisteredTables.OrderBy(table => table.FileId, StringComparer.Ordinal))
            {
                byte[] chunk = archiveByName[descriptor.Archive].ReadChunk(descriptor.Chunk, MaximumChunkBytes);
                var document = DataTableSchema.DecodeTable(descriptor, chunk);
                DataTableSchema.ValidateDocument(descriptor, document);
                entries.Add((descriptor.FileId + ".json", DataTableSchema.DescribeIndexEntry(descriptor, chunk)));
                files.Add((descriptor.FileId + ".json", document));
            }
            // object.json (#81/#82): the OBJECT union table reads SSS.MKF chunk 2 plus the
            // DATA.MKF view-evidence chunks (store/enemy/enemy-team/player-roles item,
            // player, magic and enemy references; the poison view has no extraction-time
            // source and stays undetermined until script-reference evidence exists).
            // DATA chunks 0 and 1 are read twice by design - chunk 0 once as the
            // registered store table above and once here as the item-view evidence
            // source, chunk 1 once as the registered enemy table above and once here
            // as the item- and magic-view evidence source (wAttackEquivItem/wStealItem/
            // wMagic); both paths use the same archive read and the same 18-byte STORE
            // / 70-byte ENEMY record semantics, so the exported store.json/enemy.json
            // and the evidence stay consistent.
            var dataArchive = archiveByName["DATA.MKF"];
            {
                byte[] chunk = archiveByName[ObjectUnionTable.Archive].ReadChunk(ObjectUnionTable.Chunk, MaximumChunkBytes);
                var evidence = ObjectUnionTable.ReadViewEvidence(
                    dataArchive.ReadChunk(ObjectUnionTable.StoreEvidenceChunk, MaximumChunkBytes),
                    dataArchive.ReadChunk(ObjectUnionTable.EnemyEvidenceChunk, MaximumChunkBytes),
                    dataArchive.ReadChunk(ObjectUnionTable.EnemyTeamEvidenceChunk, MaximumChunkBytes),
                    dataArchive.ReadChunk(ObjectUnionTable.PlayerRolesEvidenceChunk, MaximumChunkBytes));
                var document = ObjectUnionTable.DecodeTable(chunk, evidence);
                ObjectUnionTable.ValidateDocument(chunk.Length, document);
                entries.Add((ObjectUnionTable.FileId + ".json", ObjectUnionTable.DescribeIndexEntry(chunk)));
                files.Add((ObjectUnionTable.FileId + ".json", document));
            }
            foreach (var (_, entry) in entries.OrderBy(entry => entry.Name, StringComparer.Ordinal)) tables.Add(entry);
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
