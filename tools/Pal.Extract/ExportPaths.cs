namespace Pal.Extract;

internal static class ExportPaths
{
    internal static string Validate(string input, string output, string[] requiredNames)
    {
        string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input));
        string destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
        PathLinkGuard.RejectReparsePoints(source, "Input: links/reparse points in paths are not supported for safe export.");
        PathLinkGuard.RejectReparsePoints(destination, "Output: links/reparse points in paths are not supported for safe export.");
        if (Directory.Exists(source))
            foreach (string path in Directory.EnumerateFileSystemEntries(source))
                if (requiredNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
                    PathLinkGuard.RejectReparsePoints(path, "Input archive: links/reparse points in paths are not supported for safe export.");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (destination.Equals(source, comparison) || destination.StartsWith(
            Path.EndsInDirectorySeparator(source) ? source : source + Path.DirectorySeparatorChar, comparison))
            throw new InvalidDataException("Output must not be inside the input directory.");
        if (File.Exists(destination)) throw new InvalidDataException("Output must be a new or empty directory, not a file.");
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new InvalidDataException("Output directory is not empty; existing data will not be overwritten.");
        return destination;
    }

}
