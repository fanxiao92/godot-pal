using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pal.Extract;

// export-data: opens SSS.MKF and DATA.MKF and lays out the data/extracted/data
// index skeleton (data-schema v1). Table descriptors are registered by the M5
// per-table tickets (#79-#91); this milestone foundation only proves the shared
// skeleton and the strict output rules.
internal static class DataGameData
{
    internal static void Export(string input, string output)
    {
        string destination = ExportPaths.Validate(input, output, ["SSS.MKF", "DATA.MKF"]);
        var archives = MkfArchive.OpenRequired(input, ["SSS.MKF", "DATA.MKF"]);
        try
        {
            var sources = new JsonArray();
            foreach (MkfArchive archive in archives)
                sources.Add(new JsonObject { ["archive"] = archive.Name, ["sha256"] = archive.Sha256 });
            var indexDocument = new JsonObject
            {
                ["formatVersion"] = 1,
                ["profile"] = "faithful",
                ["sources"] = sources,
                ["tables"] = new JsonArray(),
            };
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, "index.json"),
                indexDocument.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        }
        finally
        {
            foreach (MkfArchive archive in archives) archive.Dispose();
        }
    }
}
