using System.Text.RegularExpressions;
using Pal.Extract;
using Xunit;

namespace Pal.Extract.Tests;

// M5 final acceptance (#113): two permanent machine checks on the opcode registry,
// both reading the repository's own files (never real game data). This file lives in the
// integration layer (#116): unit-boundary.md forbids File/Directory tokens in unit test
// sources, and both checks are repository-file reads, so the unit layer cannot host them.
//   1) The deduplicated `case 0x…:` label set of PAL_InterpretInstruction /
//      PAL_RunTriggerScript / PAL_RunAutoScript in the read-only reference
//      reference/sdlpal/script.c must equal OpcodeRegistry.ByOpcode's key set in
//      both directions - 165 opcodes, with 0x0032/0x0048/0x0072/0x009D absent
//      (unused by the original, per docs/milestones/m5/opcode-inventory.md).
//      This turns the "165 without omission" inventory into a CI-repeated assertion
//      instead of a one-off scan (the #112 receipt's closing self-check).
//   2) Every registered opcode must have at least one reverse-encoding
//      counterexample test: the Opcode*Tests.cs sources are scanned for
//      Decode(<index>, 0xOPCODE, ...) literals and compared with the registry. The scan
//      deliberately still targets tests/Pal.Extract.UnitTests: the Opcode*Tests.cs batches
//      it measures remain unit-layer sources (their Decode call shapes feed this check).
//      The only test-side opcode outside the registry is OpcodeCounterexampleTests'
//      raw sample 0x0032, which has no case anywhere in script.c and stays raw.
// The shared counterexample skeleton (edit one semantic field -> the re-encoded
// bytes must differ from the source bytes and equal a hand-computed expectation)
// is documented in events-schema.md's 反向编码反例覆盖 section, whose coverage
// table is generated from this same scan.
public sealed class OpcodeRegistryCompletenessTests
{
    private const int RegistrySize = 165;
    private const int TotalCaseLabels = 173; // 147 shared + 17 trigger + 9 auto case labels

    // The four positions the original never wired to any switch (opcode-inventory.md).
    private static readonly int[] UnusedOpcodes = [0x0032, 0x0048, 0x0072, 0x009D];

    private static readonly string[] SwitchFunctions =
    [
        "PAL_InterpretInstruction", // shared switch (script.c:587, switch at 650)
        "PAL_RunTriggerScript",     // trigger-only switch (script.c:3142, switch at 3204)
        "PAL_RunAutoScript",        // auto-only switch (script.c:3484, switch at 3520)
    ];

    // The test output lives under tests/<project>/bin/<config>/<tfm>; the repository
    // root is the nearest ancestor holding both the reference copy and AGENTS.md.
    internal static string FindRepositoryRootForTests()
    {
        for (string? directory = AppContext.BaseDirectory; directory is not null;
             directory = Path.GetDirectoryName(directory))
        {
            if (File.Exists(Path.Combine(directory, "reference", "sdlpal", "script.c")) &&
                File.Exists(Path.Combine(directory, "AGENTS.md")))
                return directory;
        }
        throw new InvalidOperationException(
            "Repository root not found: expected reference/sdlpal/script.c above the test output.");
    }

    // Replaces comments and string/char literals with spaces (keeping newlines) so that
    // brace matching and case-label scanning only see real code. A minimal C lexer for
    // the frozen script.c; not a general preprocessor.
    private static string StripCommentsAndLiterals(string source)
    {
        var output = new char[source.Length];
        int read = 0, written = 0;
        int state = 0; // 0 code, 1 block comment, 2 line comment, 3 string, 4 char
        while (read < source.Length)
        {
            char c = source[read];
            char next = read + 1 < source.Length ? source[read + 1] : '\0';
            switch (state)
            {
                case 0:
                    if (c == '/' && next == '*') { state = 1; output[written++] = ' '; output[written++] = ' '; read += 2; }
                    else if (c == '/' && next == '/') { state = 2; output[written++] = ' '; output[written++] = ' '; read += 2; }
                    else if (c == '"') { state = 3; output[written++] = ' '; read++; }
                    else if (c == '\'') { state = 4; output[written++] = ' '; read++; }
                    else { output[written++] = c; read++; }
                    break;
                case 1:
                    if (c == '*' && next == '/') { state = 0; output[written++] = ' '; output[written++] = ' '; read += 2; }
                    else { output[written++] = c == '\n' ? '\n' : ' '; read++; }
                    break;
                case 2:
                    if (c == '\n') { state = 0; output[written++] = '\n'; }
                    else output[written++] = ' ';
                    read++;
                    break;
                default: // 3 or 4: string or char literal
                    if (c == '\\') { output[written++] = ' '; output[written++] = ' '; read += 2; }
                    else
                    {
                        output[written++] = c == '\n' ? '\n' : ' ';
                        if (c == '"' && state == 3 || c == '\'' && state == 4) state = 0;
                        read++;
                    }
                    break;
            }
        }
        return new string(output, 0, written);
    }

    // Returns the brace-balanced body of the named function (comments/literals stripped).
    private static string FunctionBody(string code, string function)
    {
        Match signature = Regex.Match(code, "^" + function + @"\s*\(", RegexOptions.Multiline);
        Assert.True(signature.Success, $"{function} not found in script.c.");
        int opening = code.IndexOf('{', signature.Index + signature.Length);
        Assert.True(opening >= 0, $"{function} has no body.");
        int depth = 0;
        for (int i = opening; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}' && --depth == 0)
                return code[(opening + 1)..i];
        }
        Assert.Fail($"{function} body is not brace-balanced.");
        throw new InvalidOperationException("unreachable");
    }

    private static List<int> CaseOpcodes(string body) =>
        [.. Regex.Matches(body, @"\bcase\s+0x([0-9A-Fa-f]+)\s*:")
            .Select(match => Convert.ToInt32(match.Groups[1].Value, 16))];

    [Fact]
    public void Script_switch_case_labels_match_the_registry_in_both_directions()
    {
        string root = FindRepositoryRootForTests();
        string code = StripCommentsAndLiterals(
            File.ReadAllText(Path.Combine(root, "reference", "sdlpal", "script.c")));

        var opcodes = new HashSet<int>();
        int totalLabels = 0;
        foreach (string function in SwitchFunctions)
        {
            List<int> cases = CaseOpcodes(FunctionBody(code, function));
            Assert.NotEmpty(cases);
            totalLabels += cases.Count;
            opcodes.UnionWith(cases);
        }
        Assert.Equal(TotalCaseLabels, totalLabels);  // 147 shared + 17 trigger + 9 auto
        Assert.Equal(RegistrySize, opcodes.Count);   // deduplicated union

        Assert.All(UnusedOpcodes, opcode =>
            Assert.False(opcodes.Contains(opcode), $"0x{opcode:X4} is unused by the original."));

        var registry = OpcodeRegistry.ByOpcode.Keys.ToHashSet();
        string[] unregistered = [.. opcodes.Except(registry).Select(opcode => $"0x{opcode:X4}")];
        Assert.True(unregistered.Length == 0,
            $"script.c case labels missing from OpcodeRegistry: {string.Join(", ", unregistered)}");
        string[] unhandled = [.. registry.Except(opcodes).Select(opcode => $"0x{opcode:X4}")];
        Assert.True(unhandled.Length == 0,
            $"OpcodeRegistry descriptors missing from the script.c switches: {string.Join(", ", unhandled)}");
    }

    [Fact]
    public void Every_registered_opcode_has_a_counterexample_test()
    {
        string testsDirectory = Path.Combine(FindRepositoryRootForTests(), "tests", "Pal.Extract.UnitTests");
        // The shared skeleton decodes hand-built little-endian bytes through
        // Decode(index, opcode, operand0, operand1, operand2); each Opcode*Tests.cs
        // batch lists its opcodes as 0x literals in those calls.
        Regex decodeCall = new(@"Decode\(\s*\d+\s*,\s*0x([0-9A-Fa-f]{1,4})\b");
        var coverage = new Dictionary<int, SortedSet<string>>();
        foreach (string path in Directory.GetFiles(testsDirectory, "Opcode*Tests.cs").Order())
        {
            foreach (Match match in decodeCall.Matches(File.ReadAllText(path)))
            {
                int opcode = Convert.ToInt32(match.Groups[1].Value, 16);
                if (!coverage.TryGetValue(opcode, out SortedSet<string>? classes))
                    coverage[opcode] = classes = [];
                classes.Add(Path.GetFileName(path));
            }
        }
        Assert.NotEmpty(coverage);

        string[] uncovered = [.. OpcodeRegistry.ByOpcode.Keys
            .Where(opcode => !coverage.ContainsKey(opcode))
            .OrderBy(opcode => opcode)
            .Select(opcode => $"0x{opcode:X4} {OpcodeRegistry.ByOpcode[opcode].Op}")];
        Assert.True(uncovered.Length == 0,
            $"opcodes without a counterexample test: {string.Join(", ", uncovered)}");

        // The scan's only non-registry opcode is the raw sample (no case in script.c).
        int[] extras = [.. coverage.Keys.Except(OpcodeRegistry.ByOpcode.Keys).OrderBy(opcode => opcode)];
        Assert.Equal(UnusedOpcodes.Take(1), extras); // 0x0032 only
    }
}
