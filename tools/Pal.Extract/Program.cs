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

if (args.Length == 7 && args[0] == "extract-dialogue" && args[1] == "--input" && args[3] == "--reference" && args[5] == "--output")
{
    try
    {
        DialogueLanguageExporter.Export(args[2], args[4], args[6]);
        return 0;
    }
    catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
    {
        Console.Error.WriteLine(exception is InvalidDataException ? $"Dialogue extraction failed: {exception.Message}"
            : $"Dialogue extraction failed: unable to read input or write output ({exception.GetType().Name}).");
        return 1;
    }
}

if (args.Length == 5 && args[0] == "export-events" && args[1] == "--input" && args[3] == "--output")
{
    try
    {
        EventArchive.Export(args[2], args[4]);
        return 0;
    }
    catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
    {
        Console.Error.WriteLine(exception is InvalidDataException ? $"Event export failed: {exception.Message}"
            : $"Event export failed: unable to read input or write output ({exception.GetType().Name}).");
        return 1;
    }
}

if (args.Length == 5 && args[0] == "author-battle-context" && args[1] == "--input" && args[3] == "--output")
{
    try
    {
        BattleContextAuthor.Author(args[2], args[4]);
        Console.WriteLine($"Battle context authored: {BattleContextAuthor.Profile}");
        return 0;
    }
    catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or JsonException or InvalidOperationException or FormatException or OverflowException)
    {
        Console.Error.WriteLine(exception is InvalidDataException or JsonException or InvalidOperationException or FormatException or OverflowException
            ? $"Battle context authoring failed: {exception.Message}"
            : $"Battle context authoring failed: unable to read input or write output ({exception.GetType().Name}).");
        return 1;
    }
}

if (args.Length == 5 && args[0] == "rebuild-events" && args[1] == "--input" && args[3] == "--output")
{
    try
    {
        bool differsFromSource = EventArchive.Rebuild(args[2], args[4]);
        if (differsFromSource) Console.WriteLine("Rebuilt chunk differs from the recorded source SHA-256; semantic edits were encoded.");
        return 0;
    }
    catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Text.Json.JsonException or InvalidOperationException or FormatException or OverflowException)
    {
        Console.Error.WriteLine(exception is InvalidDataException ? $"Event rebuild failed: {exception.Message}"
            : $"Event rebuild failed: unable to read input or write output ({exception.GetType().Name}).");
        return 1;
    }
}

if (args.Length == 11 && args[0] is "export-map" or "export-scene" or "extract-scene")
{
    var options = new Dictionary<string, string>();
    for (int i = 1; i < args.Length; i += 2)
    {
        if (string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("--", StringComparison.Ordinal)
            || !options.TryAdd(args[i], args[i + 1])) break;
    }
    if (options.Count == 5 && options.TryGetValue("--input", out var input)
        && options.TryGetValue("--output", out var output)
        && options.TryGetValue(args[0] == "export-map" ? "--map" : "--scene", out var idValue)
        && int.TryParse(idValue, out int id) && id > 0
        && options.TryGetValue("--palette", out var paletteValue) && int.TryParse(paletteValue, out int paletteId) && paletteId >= 0
        && options.TryGetValue("--lighting", out var lighting) && lighting is "day" or "night")
    {
        try
        {
            if (args[0] == "export-map") MapExporter.Export(input, output, id, paletteId, lighting);
            else if (args[0] == "export-scene") SceneExporter.Export(input, output, id, paletteId, lighting);
            else SceneBundleExporter.Export(input, output, id, paletteId, lighting);
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
    Console.Error.WriteLine(args[0] is "export-map" or "export-scene" or "extract-scene" or "export-events" or "rebuild-events" or "extract-dialogue" or "author-battle-context" ? $"Unsupported arguments for {args[0]}."
        : $"Unsupported arguments: {string.Join(" ", args)}");
    Console.Error.WriteLine("Use --help for usage.");
    return 2;
}

Console.WriteLine("""
    pal-extract - Offline resource conversion tool for godot-pal.

    Usage: pal-extract [--help]
           pal-extract inspect --input <directory>
           pal-extract export-map --input <directory> --output <directory> --map <id> --palette <id> --lighting day|night
           pal-extract export-scene --input <directory> --output <directory> --scene <id> --palette <id> --lighting day|night
           pal-extract extract-scene --input <directory> --output <directory> --scene <id> --palette <id> --lighting day|night
           pal-extract extract-dialogue --input <directory> --reference <reference/sdlpal> --output <new-or-empty-directory>
           pal-extract export-events --input <directory> --output <directory>
           pal-extract rebuild-events --input <directory> --output <file>
           pal-extract author-battle-context --input <author.json> --output <new-or-empty-directory>
      --help  Show this help message.
      inspect Read-only MKF inspection; reports structure and compression hints as JSON.

      export-map Export Windows MAP/GOP/PAT tiles and a static map preview.
                 Requires a new or empty output directory outside input; links are rejected.
                 No scripts or scene objects are executed or exported.

      export-scene Export Windows initial SSS/DATA records and static MGO/PAT sprite frames.
                   Requires a new or empty output directory outside input; links are rejected.
                   Script entries remain numeric; scripts are never loaded or executed.

      extract-dialogue Convert the Chinese and English SLF message sections to strict modern JSON packages.
                       Preserves source message ranges, blank lines, and CLEAR controls; extracts target art and reference glyphs.
                       Writes a new or empty directory.
                       Runtime does not read SLF and missing selected-language content is not replaced.

      export-events Convert SSS.MKF chunk 4 into a complete faithful event set; unverified opcodes remain raw.
                    Writes a new or empty output directory; this offline command does not execute scripts.

      rebuild-events Reconstruct SSS.MKF chunk 4 from a complete faithful event set.
                     Writes a new file only; edited semantic fields are re-encoded.

      author-battle-context Validate and canonicalize the authored @09C3 pre-trigger context.
                            Records source provenance and the complete local closure without claiming a natural save.
                            Writes a new or empty directory; runtime does not read original archives or this tool.

      extract-scene Compose a complete static scene bundle with a v1 source/artifact manifest.
                    Derives the map from the scene record; requires only six scene MKFs.
                    Same output safety rules; no text, fonts, SLF or event artifacts required.
    """);
return 0;
