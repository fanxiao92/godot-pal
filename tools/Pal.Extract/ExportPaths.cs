namespace Pal.Extract;

internal static class ExportPaths
{
    internal static string Validate(string input, string output, string[] requiredNames)
    {
        string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input));
        string destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
        RejectLinks(source, "Input");
        RejectLinks(destination, "Output");
        if (Directory.Exists(source))
            foreach (string path in Directory.EnumerateFileSystemEntries(source))
                if (requiredNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
                    RejectLinks(path, "Input archive");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (destination.Equals(source, comparison) || destination.StartsWith(
            Path.EndsInDirectorySeparator(source) ? source : source + Path.DirectorySeparatorChar, comparison))
            throw new InvalidDataException("Output must not be inside the input directory.");
        if (File.Exists(destination)) throw new InvalidDataException("Output must be a new or empty directory, not a file.");
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new InvalidDataException("Output directory is not empty; existing data will not be overwritten.");
        return destination;
    }

    private static void RejectLinks(string path, string resource)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"{resource}: links/reparse points in paths are not supported for safe export.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
