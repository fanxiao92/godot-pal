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

if (args.Length != 0 && (args.Length != 1 || args[0] != "--help"))
{
    Console.Error.WriteLine($"Unsupported arguments: {string.Join(" ", args)}");
    Console.Error.WriteLine("Use --help for usage.");
    return 2;
}

Console.WriteLine("""
    pal-extract - Offline resource conversion tool for godot-pal.

    Usage: pal-extract [--help]
           pal-extract inspect --input <directory>
      --help  Show this help message.
      inspect Read-only MKF inspection; reports structure and compression hints as JSON.

    Resource extraction is not implemented yet.
    """);
return 0;
