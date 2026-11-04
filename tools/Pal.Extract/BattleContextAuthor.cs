using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Pal.Core;

namespace Pal.Extract;

internal static class BattleContextAuthor
{
    internal const string Profile = FirstMandatoryBattleProfile.ProfileIdValue;

    internal static void Author(string input, string output)
    {
        string source = Path.GetFullPath(input);
        string destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
        ValidatePaths(source, destination);
        byte[] contextBytes = FirstMandatoryBattleProfile.ValidateAndCanonicalizeAuthorContext(File.ReadAllBytes(source));
        var index = new JsonObject
        {
            ["formatVersion"] = 1,
            ["profile"] = Profile,
            ["contextFile"] = "battle-context.json",
            ["contextSha256"] = Convert.ToHexStringLower(SHA256.HashData(contextBytes))
        };
        Directory.CreateDirectory(destination);
        WriteNewFile(destination, "battle-context.json", contextBytes);
        WriteNewFile(destination, "index.json", Encoding.UTF8.GetBytes(index.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n"));
    }

    private static void ValidatePaths(string source, string destination)
    {
        if (!File.Exists(source)) throw new InvalidDataException("Battle context input file does not exist.");
        PathLinkGuard.RejectReparsePoints(source, "Battle context input: links/reparse points are not supported.");
        PathLinkGuard.RejectReparsePoints(destination, "Battle context output: links/reparse points are not supported.");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (source.Equals(destination, comparison)) throw new InvalidDataException("Battle context output must differ from input.");
        if (File.Exists(destination)) throw new InvalidDataException("Battle context output must be a new or empty directory, not a file.");
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new InvalidDataException("Battle context output directory is not empty; existing data will not be overwritten.");
    }

    private static void WriteNewFile(string destination, string name, byte[] bytes)
    {
        PathLinkGuard.RejectReparsePoints(destination, "Battle context output: links/reparse points are not supported.");
        string path = Path.Combine(destination, name);
        PathLinkGuard.RejectReparsePoints(path, "Battle context output: links/reparse points are not supported.");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
    }
}
