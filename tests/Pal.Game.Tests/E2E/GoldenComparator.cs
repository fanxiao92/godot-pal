using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;

namespace Pal.Game.Tests.E2E;

public sealed class FrameComparisonResult
{
    public string FrameName { get; init; } = "";
    public string Status { get; init; } = "failed"; // passed | updated | failed
    public string? Error { get; init; }
    public string ActualPath { get; init; } = "";
    public string ActualSha256 { get; init; } = "";
    public string? GoldenSha256 { get; init; }
    public string? DiffPath { get; init; }
    public double DiffPercent { get; init; }
    public double TolerancePercent { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}

public static class GoldenComparator
{
    public const int ExpectedWidth = 320;
    public const int ExpectedHeight = 200;
    // Small per-channel rounding tolerance (2/255) for GPU format conversion; pixel counts above this count as diff.
    private const float ChannelEpsilon = 2.1f / 255f;

    public static void ValidateImageIntegrity(Image? image, string label)
    {
        if (image is null)
            throw new InvalidDataException($"Captured image '{label}' is null.");
        var size = image.GetSize();
        if (size.X != ExpectedWidth || size.Y != ExpectedHeight)
            throw new InvalidDataException($"Captured image '{label}' size {size.X}x{size.Y} does not match expected {ExpectedWidth}x{ExpectedHeight}.");

        // Ensure frame is not completely blank / single solid color.
        Color first = image.GetPixel(0, 0);
        bool hasVariation = false;
        for (int y = 0; y < size.Y && !hasVariation; y += 4)
        for (int x = 0; x < size.X && !hasVariation; x += 4)
        {
            Color c = image.GetPixel(x, y);
            if (Math.Abs(c.R - first.R) > ChannelEpsilon
                || Math.Abs(c.G - first.G) > ChannelEpsilon
                || Math.Abs(c.B - first.B) > ChannelEpsilon)
            {
                hasVariation = true;
            }
        }
        if (!hasVariation)
            throw new InvalidDataException($"Captured image '{label}' failed integrity check: frame is a single uniform color ({first}).");
    }

    public static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public static FrameComparisonResult CompareOrUpdate(
        Image actual,
        string scenarioName,
        string scenarioVersion,
        string frameName,
        double tolerancePercent,
        string artifactsDir,
        string goldenDir,
        bool updateGolden,
        string commitSha)
    {
        ValidateImageIntegrity(actual, $"{scenarioName}/{frameName}");

        string screenshotDir = Path.Combine(artifactsDir, "screenshots", scenarioName);
        Directory.CreateDirectory(screenshotDir);
        string actualPath = Path.Combine(screenshotDir, frameName + ".png");
        if (actual.SavePng(actualPath) != Error.Ok)
            throw new IOException($"Failed to save actual screenshot: {actualPath}");
        string actualSha = Sha256File(actualPath);

        string goldenScenarioDir = Path.Combine(goldenDir, scenarioName);
        string goldenPath = Path.Combine(goldenScenarioDir, frameName + ".png");

        if (updateGolden)
        {
            Directory.CreateDirectory(goldenScenarioDir);
            File.Copy(actualPath, goldenPath, overwrite: true);
            UpdateGoldenManifest(goldenDir, scenarioName, scenarioVersion, frameName, actualSha, tolerancePercent, commitSha);
            return new FrameComparisonResult
            {
                FrameName = frameName,
                Status = "updated",
                ActualPath = Path.GetRelativePath(artifactsDir, actualPath).Replace('\\', '/'),
                ActualSha256 = actualSha,
                GoldenSha256 = actualSha,
                DiffPercent = 0.0,
                TolerancePercent = tolerancePercent,
                Width = actual.GetWidth(),
                Height = actual.GetHeight()
            };
        }

        if (!File.Exists(goldenPath))
        {
            return new FrameComparisonResult
            {
                FrameName = frameName,
                Status = "failed",
                Error = $"Missing golden image for '{scenarioName}/{frameName}'. Run with --update-golden to establish baseline.",
                ActualPath = Path.GetRelativePath(artifactsDir, actualPath).Replace('\\', '/'),
                ActualSha256 = actualSha,
                GoldenSha256 = null,
                DiffPercent = 100.0,
                TolerancePercent = tolerancePercent,
                Width = actual.GetWidth(),
                Height = actual.GetHeight()
            };
        }

        string goldenSha = Sha256File(goldenPath);
        using var golden = new Image();
        if (golden.Load(goldenPath) != Error.Ok)
            throw new IOException($"Failed to load golden image: {goldenPath}");

        var (diffPercent, diffImage) = ComputeDiff(actual, golden);
        using (diffImage)
        {
            bool passed = diffPercent <= tolerancePercent;
            string? diffRelPath = null;
            if (!passed)
            {
                string diffDir = Path.Combine(artifactsDir, "diffs", scenarioName);
                Directory.CreateDirectory(diffDir);
                string diffFullPath = Path.Combine(diffDir, frameName + "-diff.png");
                if (diffImage.SavePng(diffFullPath) != Error.Ok)
                    throw new IOException($"Failed to save diff image: {diffFullPath}");
                diffRelPath = Path.GetRelativePath(artifactsDir, diffFullPath).Replace('\\', '/');
            }

            return new FrameComparisonResult
            {
                FrameName = frameName,
                Status = passed ? "passed" : "failed",
                Error = passed ? null : $"Golden diff {diffPercent:0.###}% exceeds tolerance {tolerancePercent:0.###}%.",
                ActualPath = Path.GetRelativePath(artifactsDir, actualPath).Replace('\\', '/'),
                ActualSha256 = actualSha,
                GoldenSha256 = goldenSha,
                DiffPath = diffRelPath,
                DiffPercent = Math.Round(diffPercent, 4),
                TolerancePercent = tolerancePercent,
                Width = actual.GetWidth(),
                Height = actual.GetHeight()
            };
        }
    }

    public static (double DiffPercent, Image DiffImage) ComputeDiff(Image actual, Image golden)
    {
        int w = actual.GetWidth();
        int h = actual.GetHeight();
        if (w != golden.GetWidth() || h != golden.GetHeight())
        {
            var mismatch = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
            mismatch.Fill(new Color(1f, 0f, 1f, 1f));
            return (100.0, mismatch);
        }

        var diff = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        int differing = 0;
        int total = w * h;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            Color a = actual.GetPixel(x, y);
            Color g = golden.GetPixel(x, y);
            bool same = Math.Abs(a.R - g.R) <= ChannelEpsilon
                && Math.Abs(a.G - g.G) <= ChannelEpsilon
                && Math.Abs(a.B - g.B) <= ChannelEpsilon
                && Math.Abs(a.A - g.A) <= ChannelEpsilon;
            if (same)
            {
                diff.SetPixel(x, y, new Color(a.R * 0.35f, a.G * 0.35f, a.B * 0.35f, 1f));
            }
            else
            {
                differing++;
                diff.SetPixel(x, y, new Color(1f, 0f, 1f, 1f));
            }
        }
        double pct = total == 0 ? 0.0 : (differing * 100.0) / total;
        return (pct, diff);
    }

    public static void BuildContactSheet(string artifactsDir)
    {
        string screenshotsRoot = Path.Combine(artifactsDir, "screenshots");
        if (!Directory.Exists(screenshotsRoot)) return;
        var pngs = new List<string>(Directory.EnumerateFiles(screenshotsRoot, "*.png", SearchOption.AllDirectories));
        pngs.Sort(StringComparer.OrdinalIgnoreCase);
        if (pngs.Count == 0) return;

        const int cellW = ExpectedWidth;
        const int cellH = ExpectedHeight;
        const int pad = 8;
        int cols = Math.Min(3, pngs.Count);
        int rows = (pngs.Count + cols - 1) / cols;
        int sheetW = cols * cellW + (cols + 1) * pad;
        int sheetH = rows * cellH + (rows + 1) * pad;

        using var sheet = Image.CreateEmpty(sheetW, sheetH, false, Image.Format.Rgba8);
        sheet.Fill(new Color(0.12f, 0.12f, 0.14f, 1f));
        for (int i = 0; i < pngs.Count; i++)
        {
            using var tile = new Image();
            if (tile.Load(pngs[i]) != Error.Ok) continue;
            int col = i % cols;
            int row = i / cols;
            int dstX = pad + col * (cellW + pad);
            int dstY = pad + row * (cellH + pad);
            sheet.BlitRect(tile, new Rect2I(0, 0, Math.Min(cellW, tile.GetWidth()), Math.Min(cellH, tile.GetHeight())), new Vector2I(dstX, dstY));
        }
        sheet.SavePng(Path.Combine(artifactsDir, "contact-sheet.png"));
    }

    private static void UpdateGoldenManifest(
        string goldenDir,
        string scenarioName,
        string scenarioVersion,
        string frameName,
        string sha256,
        double tolerancePercent,
        string commitSha)
    {
        string manifestPath = Path.Combine(goldenDir, "golden-manifest.json");
        var entries = new SortedDictionary<string, GoldenManifestEntry>(StringComparer.Ordinal);
        if (File.Exists(manifestPath))
        {
            try
            {
                var existing = JsonSerializer.Deserialize<GoldenManifestDocument>(File.ReadAllText(manifestPath, Encoding.UTF8));
                if (existing?.Frames is not null)
                {
                    foreach (var kv in existing.Frames) entries[kv.Key] = kv.Value;
                }
            }
            catch (JsonException) { }
        }

        string key = $"{scenarioName}/{frameName}.png";
        entries[key] = new GoldenManifestEntry
        {
            Scenario = scenarioName,
            Frame = frameName,
            ScenarioVersion = scenarioVersion,
            Sha256 = sha256,
            TolerancePercent = tolerancePercent,
            Commit = commitSha,
            UpdatedAtUtc = DateTime.UtcNow.ToString("O")
        };

        var doc = new GoldenManifestDocument
        {
            FormatVersion = 1,
            UpdatedAtUtc = DateTime.UtcNow.ToString("O"),
            Frames = entries
        };
        var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(manifestPath, json + "\n", new UTF8Encoding(false));
    }

    private sealed class GoldenManifestDocument
    {
        public int FormatVersion { get; set; } = 1;
        public string UpdatedAtUtc { get; set; } = "";
        public SortedDictionary<string, GoldenManifestEntry> Frames { get; set; } = new();
    }

    private sealed class GoldenManifestEntry
    {
        public string Scenario { get; set; } = "";
        public string Frame { get; set; } = "";
        public string ScenarioVersion { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public double TolerancePercent { get; set; }
        public string Commit { get; set; } = "";
        public string UpdatedAtUtc { get; set; } = "";
    }
}
