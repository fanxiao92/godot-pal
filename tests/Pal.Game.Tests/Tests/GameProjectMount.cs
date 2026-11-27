using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Godot;

namespace Pal.Game.Tests;

internal static class GameProjectMount
{
    private static readonly Regex TempFixturePattern = new(
        @"res://((?:Assets/RuntimeTests|Tests/RuntimeProfiles|Tests/RuntimeBattleProfiles|Tests/RuntimeStory)/[^/""'\s]+|Tests/Fixtures/TriggerDialogue)",
        RegexOptions.Compiled);

    private static string? mountedPackPath;
    private static int stagedSamplesRefCount;
    private static bool stagedSamplesOwned;

    internal static string TestProjectRoot => ProjectSettings.GlobalizePath("res://").TrimEnd('/', '\\');

    internal static string GameProjectRoot =>
        Path.GetFullPath(Path.Combine(TestProjectRoot, "..", "..", "src", "Pal.Game"));

    internal static string GameSamplePath(string relative) =>
        Path.Combine(GameProjectRoot, relative.Replace('/', Path.DirectorySeparatorChar));

    internal static void DeleteDirectoryLink(string link)
    {
        if (OperatingSystem.IsWindows()) Directory.Delete(link);
        else File.Delete(link);
    }

    internal static void EnsureMounted()
    {
        if (ResourceLoader.Exists("res://Scenes/Main.tscn")) return;
        string gameRoot = GameProjectRoot;
        if (!File.Exists(Path.Combine(gameRoot, "project.godot")))
            throw new DirectoryNotFoundException("Production Pal.Game project not found at: " + gameRoot);

        string zipPath = Path.Combine(Path.GetTempPath(), $"pal-game-mount-{OS.GetProcessId()}-{Guid.NewGuid():N}.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (string file in Directory.EnumerateFiles(gameRoot, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(gameRoot, file).Replace('\\', '/');
                if (ShouldSkipPackFile(relative)) continue;
                archive.CreateEntryFromFile(file, relative, CompressionLevel.NoCompression);
            }
        }

        if (!ProjectSettings.LoadResourcePack(zipPath, replaceFiles: false))
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            throw new InvalidOperationException("ProjectSettings.LoadResourcePack failed for: " + zipPath);
        }

        Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(typeof(Main).Assembly);
        mountedPackPath = zipPath;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (!string.IsNullOrEmpty(mountedPackPath) && File.Exists(mountedPackPath))
                try { File.Delete(mountedPackPath); } catch (IOException) { }
        };
    }

    internal static IDisposable StageSamplesInTestProject()
    {
        string testRoot = TestProjectRoot;
        string srcSamples = GameSamplePath("Samples/AuthoredBattle");
        string dstSamples = Path.Combine(testRoot, "Samples", "AuthoredBattle");
        if (stagedSamplesRefCount == 0 && !Directory.Exists(dstSamples))
        {
            CopyDirectory(srcSamples, dstSamples);
            stagedSamplesOwned = true;
        }
        stagedSamplesRefCount++;
        return new SamplesStageScope(testRoot, dstSamples);
    }

    internal static IDisposable StageFixturesForGameChild(IEnumerable<string> arguments)
    {
        string testRoot = TestProjectRoot;
        string gameRoot = GameProjectRoot;
        var stagedDirs = new List<string>();
        var createdParents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();

        void EnqueueMatches(string text)
        {
            foreach (Match match in TempFixturePattern.Matches(text))
            {
                string relRoot = match.Groups[1].Value;
                if (visited.Add(relRoot)) queue.Enqueue(relRoot);
            }
        }

        foreach (string arg in arguments) EnqueueMatches(arg);

        while (queue.Count > 0)
        {
            string relRoot = queue.Dequeue();
            string srcDir = Path.Combine(testRoot, relRoot.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(srcDir)) continue;

            string dstDir = Path.Combine(gameRoot, relRoot.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(dstDir)) continue;

            TrackCreatedParents(gameRoot, dstDir, createdParents);
            CopyDirectory(srcDir, dstDir);
            stagedDirs.Add(dstDir);

            foreach (string file in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(file);
                if (ext.Equals(".tres", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".tscn", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
                {
                    EnqueueMatches(File.ReadAllText(file));
                }
            }
        }

        return new StagedCleanup(stagedDirs, createdParents);
    }

    private static bool ShouldSkipPackFile(string relative)
    {
        if (relative.Equals("project.godot", StringComparison.OrdinalIgnoreCase)
            || relative.Equals("Pal.Game.csproj", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("obj/", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("gdunit4_testadapter_v5/", StringComparison.OrdinalIgnoreCase))
            return true;

        if (relative.StartsWith(".godot/", StringComparison.OrdinalIgnoreCase))
            return !relative.StartsWith(".godot/imported/", StringComparison.OrdinalIgnoreCase);

        return false;
    }

    private static void TrackCreatedParents(string stopRoot, string targetDir, HashSet<string> createdParents)
    {
        string? current = Path.GetDirectoryName(targetDir);
        var missing = new List<string>();
        while (!string.IsNullOrEmpty(current) && !current.Equals(stopRoot, StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(current)) break;
            missing.Add(current);
            current = Path.GetDirectoryName(current);
        }
        foreach (string dir in missing) createdParents.Add(dir);
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (string file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDir, file);
            string target = Path.Combine(destinationDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void DeleteEmptyDirectory(string path, List<Exception> errors)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).GetEnumerator().MoveNext())
                Directory.Delete(path, false);
        }
        catch (Exception error) { errors.Add(error); }
    }

    private sealed class SamplesStageScope(string testRoot, string dstSamples) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            stagedSamplesRefCount = Math.Max(0, stagedSamplesRefCount - 1);
            if (!stagedSamplesOwned || stagedSamplesRefCount > 0) return;
            stagedSamplesOwned = false;

            var errors = new List<Exception>();
            try { if (Directory.Exists(dstSamples)) Directory.Delete(dstSamples, true); }
            catch (Exception error) { errors.Add(error); }
            DeleteEmptyDirectory(Path.Combine(testRoot, "Samples"), errors);

            if (errors.Count > 0)
                throw new AggregateException("Failed to clean staged test Samples directory.", errors);
        }
    }

    private sealed class StagedCleanup(List<string> stagedDirs, HashSet<string> createdParents) : IDisposable
    {
        public void Dispose()
        {
            var errors = new List<Exception>();
            foreach (string dir in stagedDirs)
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
                catch (Exception error) { errors.Add(error); }
            }
            var parents = new List<string>(createdParents);
            parents.Sort((a, b) => b.Length.CompareTo(a.Length));
            foreach (string parent in parents) DeleteEmptyDirectory(parent, errors);
            if (errors.Count > 0)
                throw new AggregateException("Failed to clean staged child fixture directories.", errors);
        }
    }
}
