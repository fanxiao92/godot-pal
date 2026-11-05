using System.Text.RegularExpressions;
using Xunit;

namespace Pal.Extract.Tests;

// M5 retro guard (#121): the offline tool's OpcodeRegistry and the runtime loader's
// TriggerEventCollection are two strict validators of the same faithful events.json v1
// vocabulary. Three drifts shipped during M5 (giveItem vs addItem, fadeOut.frames vs
// delay, redraw operand rebinding) because nothing compared them mechanically. This test
// source-scans both files and asserts the loader's verb/field vocabulary is exactly
// covered by the registry's, so any future rename that lands on one side fails here.
//
// Layer: file I/O reads repository sources, so this lives in the integration project
// beside OpcodeRegistryCompletenessTests (unit-boundary keeps the unit project in-memory).
public sealed class OpcodeVocabularyParityTests
{
    // Loader verbs that are intentionally absent from the registry's descriptor table.
    // Keep each entry with the reason it is legitimate.
    private static readonly string[] LoaderOnlyVerbs = [];

    private static readonly Regex RegistryDescriptor = new(
        @"new\(0x[0-9A-Fa-f]+,\s*""(?<verb>\w+)""", RegexOptions.CultureInvariant);

    private static readonly Regex RegistryField = new(
        @"new\(""(?<field>\w+)"",\s*OpcodeFieldKind\.", RegexOptions.CultureInvariant);

    // Arms appear as `"verb" =>` anywhere in the block; see LoaderVocabulary.

    private static readonly Regex FieldList = new(@"\[(?<fields>[^\]]*)\]");

    [Fact]
    public void Runtime_loader_vocabulary_is_exactly_covered_by_the_offline_registry()
    {
        string root = OpcodeRegistryCompletenessTests.FindRepositoryRootForTests();
        var registry = RegistryVocabulary(Path.Combine(root, "tools", "Pal.Extract", "OpcodeRegistry.cs"));
        var loader = LoaderVocabulary(Path.Combine(root, "src", "Pal.Core", "TriggerEventCollection.cs"));

        Assert.NotEmpty(registry);
        Assert.NotEmpty(loader);

        var unregistered = loader.Keys
            .Where(verb => !registry.ContainsKey(verb) && !LoaderOnlyVerbs.Contains(verb))
            .Order().ToArray();
        Assert.True(unregistered.Length == 0,
            $"Loader verbs missing from OpcodeRegistry (rename drift?): {string.Join(", ", unregistered)}");

        var mismatches = new List<string>();
        foreach ((string verb, var loaderFields) in loader)
        {
            if (!registry.TryGetValue(verb, out var registryFields)) continue;
            if (!loaderFields.SetEquals(registryFields))
                mismatches.Add($"{verb}: loader={{{string.Join(",", loaderFields.Order())}}} registry={{{string.Join(",", registryFields.Order())}}}");
        }
        Assert.True(mismatches.Count == 0,
            $"Field-vocabulary drift between loader and registry:{Environment.NewLine}{string.Join(Environment.NewLine, mismatches)}");
    }

    private static Dictionary<string, HashSet<string>> RegistryVocabulary(string path)
    {
        string source = File.ReadAllText(path);
        var result = new Dictionary<string, HashSet<string>>();
        // "raw" is the registry's lossless fallback form, not a descriptor: it carries no
        // semantic fields on either side.
        result["raw"] = [];
        string[] chunks = source.Split(["new(0x"], StringSplitOptions.None);
        foreach (string chunk in chunks.Skip(1))
        {
            Match verb = RegistryDescriptor.Match("new(0x" + chunk);
            Assert.True(verb.Success, $"Unparseable registry descriptor near: {chunk[..Math.Min(80, chunk.Length)]}");
            string name = verb.Groups["verb"].Value;
            var fields = new HashSet<string>(RegistryField.Matches(chunk).Select(m => m.Groups["field"].Value));
            if (result.TryGetValue(name, out var existing)) existing.UnionWith(fields);
            else result[name] = fields;
        }
        return result;
    }

    private static Dictionary<string, HashSet<string>> LoaderVocabulary(string path)
    {
        string source = File.ReadAllText(path);
        // The allowlist is the `string[] fields = op switch { ... };` expression.
        int start = source.IndexOf("string[] fields = op switch", StringComparison.Ordinal);
        Assert.True(start >= 0, "Loader allowlist switch not found in TriggerEventCollection.cs.");
        int braceStart = source.IndexOf('{', start);
        int depth = 0;
        int end = -1;
        for (int i = braceStart; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) { end = i; break; }
        }
        Assert.True(end > 0, "Loader allowlist switch braces unbalanced.");
        string block = source[(braceStart + 1)..end];

        // Arms appear as `"verb" =>` anywhere in the block (several per line, or-chains,
        // nested switches). Brace depth at the arm's position separates top-level verb
        // arms (depth 0 in the block, which excludes the outer switch brace) from nested
        // selector arms (depth >= 1, e.g. the setDialog box variants), whose fields
        // belong to the enclosing verb.
        var arms = new List<(int Position, int Depth, string Verb)>();
        var depthAt = new int[block.Length + 1];
        int current = 0;
        for (int i = 0; i < block.Length; i++)
        {
            depthAt[i] = current;
            if (block[i] == '{') current++;
            else if (block[i] == '}') current--;
        }
        depthAt[block.Length] = current;
        foreach (Match arm in Regex.Matches(block, @"""(?<verb>\w+)""(?:\s+or\s+""\w+"" )*\s*=>"))
            arms.Add((arm.Index, depthAt[arm.Index], arm.Groups["verb"].Value));

        var result = new Dictionary<string, HashSet<string>>();
        string? enclosing = null;
        for (int i = 0; i < arms.Count; i++)
        {
            var (position, armDepth, verb) = arms[i];
            if (armDepth == 0)
            {
                enclosing = verb;
                if (!result.TryGetValue(verb, out _)) result[verb] = [];
            }
            string owner = armDepth == 0 ? verb : enclosing is null ? verb : enclosing;
            if (!result.TryGetValue(owner, out _)) result[owner] = [];
            int until = i + 1 < arms.Count ? arms[i + 1].Position : block.Length;
            string segment = block[position..until];
            foreach (Match list in FieldList.Matches(segment))
                foreach (Match field in Regex.Matches(list.Groups["fields"].Value, @"""(\w+)"""))
                    result[owner].Add(field.Groups[1].Value);
        }
        return result;
    }
}
