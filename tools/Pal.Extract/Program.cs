using System.Text;
using System.Text.Json;
using Pal.Extract;

Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

if (args.Length == 3 && args[0] == "inspect" && args[1] == "--input"
    && !string.IsNullOrWhiteSpace(args[2]) && !args[2].StartsWith("--", StringComparison.Ordinal))
{
    try
    {
        var report = ArchiveInspector.Inspect(args[2]);
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        }));
        return 0;
    }
    catch (InvalidDataException exception)
    {
        Console.Error.WriteLine($"Inspection failed: {exception.Message}");
        return 1;
    }
}

if (args.Length == 11 && args[0] == "export-map")
{
    var options = new Dictionary<string, string>();
    for (int i = 1; i < args.Length; i += 2)
    {
        if (string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("--", StringComparison.Ordinal)
            || !options.TryAdd(args[i], args[i + 1])) break;
    }
    if (options.Count == 5 && options.TryGetValue("--input", out var input)
        && options.TryGetValue("--output", out var output)
        && options.TryGetValue("--map", out var mapValue) && int.TryParse(mapValue, out int mapId) && mapId > 0
        && options.TryGetValue("--palette", out var paletteValue) && int.TryParse(paletteValue, out int paletteId) && paletteId >= 0
        && options.TryGetValue("--lighting", out var lighting) && lighting is "day" or "night")
    {
        try
        {
            MapExporter.Export(input, output, mapId, paletteId, lighting);
            return 0;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine(exception is InvalidDataException ? $"Export failed: {exception.Message}"
                : $"Export failed: unable to read input or write output ({exception.GetType().Name}).");
            return 1;
        }
    }
}

if (args.Length != 0 && (args.Length != 1 || args[0] != "--help"))
{
    Console.Error.WriteLine(args[0] == "export-map" ? "Unsupported arguments for export-map."
        : $"Unsupported arguments: {string.Join(" ", args)}");
    Console.Error.WriteLine("Use --help for usage.");
    return 2;
}

Console.WriteLine("""
    pal-extract - Offline resource conversion tool for godot-pal.

    Usage: pal-extract [--help]
           pal-extract inspect --input <directory>
           pal-extract export-map --input <directory> --output <directory> --map <id> --palette <id> --lighting day|night
      --help  Show this help message.
      inspect Read-only MKF inspection; reports structure and compression hints as JSON.

      export-map Export Windows MAP/GOP/PAT tiles and a static map preview.
                 Requires a new or empty output directory outside input; links are rejected.
                 No scripts or scene objects are executed or exported.
    """);
return 0;
