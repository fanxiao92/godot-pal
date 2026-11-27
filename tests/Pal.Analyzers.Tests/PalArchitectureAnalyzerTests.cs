using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Pal.Analyzers;
using Xunit;

namespace Pal.Analyzers.Tests;

public sealed class PalArchitectureAnalyzerTests
{
    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string assemblyName, params string[] sources)
    {
        var trees = sources.Select(s => CSharpSyntaxTree.ParseText(s)).ToArray();
        var refs = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Runtime.CompilerServices.InternalsVisibleToAttribute).Assembly.Location),
        };
        var compilation = CSharpCompilation.Create(
            assemblyName,
            trees,
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new PalArchitectureAnalyzer()));
        return await withAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    [Fact]
    public async Task PAL0001_rejects_formal_core_domain_referencing_Legacy_with_remediation()
    {
        var diags = await AnalyzeAsync("Pal.Core",
            "namespace Pal.Core.Legacy; public sealed class OldSlice {}",
            "using Pal.Core.Legacy; namespace Pal.Core.Battle; public sealed class Modern { OldSlice? _x; }");

        var pal1 = diags.Where(x => x.Id == PalArchitectureAnalyzer.LayerViolationId).ToArray();
        Assert.NotEmpty(pal1);
        Assert.All(pal1, d =>
        {
            Assert.Contains("违规事实", d.GetMessage());
            Assert.Contains("架构理由", d.GetMessage());
            Assert.Contains("自修复指引", d.GetMessage());
        });
    }

    [Fact]
    public async Task PAL0001_rejects_game_runtime_referencing_Editor()
    {
        var diags = await AnalyzeAsync("Pal.Game",
            "namespace Pal.Game.Editor; public sealed class PrepTool {}",
            "using Pal.Game.Editor; namespace Pal.Game.Profiles; public sealed class Loader {}");

        Assert.Contains(diags, x => x.Id == PalArchitectureAnalyzer.LayerViolationId);
    }

    [Fact]
    public async Task PAL0001_rejects_foundation_reverse_dependency_and_allows_upward_consumption()
    {
        var bad = await AnalyzeAsync("Pal.Core",
            "namespace Pal.Core.Battle; public sealed class Combat {}",
            "using Pal.Core.Battle; namespace Pal.Core.State; public sealed class RoleState {}");
        Assert.Contains(bad, x => x.Id == PalArchitectureAnalyzer.LayerViolationId);

        var good = await AnalyzeAsync("Pal.Core",
            "namespace Pal.Core.Shared; public static class Consts {}",
            "using Pal.Core.Shared; namespace Pal.Core.State; public sealed class RoleState {}");
        Assert.DoesNotContain(good, x => x.Id == PalArchitectureAnalyzer.LayerViolationId);
    }

    [Fact]
    public async Task PAL0001_rejects_cross_view_dependency_and_allows_Main_orchestration()
    {
        var bad = await AnalyzeAsync("Pal.Game",
            "namespace Pal.Game.Battle; public sealed class BattleView {}",
            "using Pal.Game.Battle; namespace Pal.Game.Dialogue; public sealed class DialogueView {}");
        Assert.Contains(bad, x => x.Id == PalArchitectureAnalyzer.LayerViolationId);

        var good = await AnalyzeAsync("Pal.Game",
            "namespace Pal.Game.Battle; public sealed class BattleView {}",
            "using Pal.Game.Battle; namespace Pal.Game; public sealed class Main { BattleView? _b; }");
        Assert.DoesNotContain(good, x => x.Id == PalArchitectureAnalyzer.LayerViolationId);
    }

    [Fact]
    public async Task PAL0002_allows_PalExtract_on_Core_but_rejects_other_friends_and_any_Game_friend()
    {
        var coreGood = await AnalyzeAsync("Pal.Core",
            "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"Pal.Extract\")]");
        Assert.DoesNotContain(coreGood, x => x.Id == PalArchitectureAnalyzer.FriendAssemblyViolationId);

        var coreBad = await AnalyzeAsync("Pal.Core",
            "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"Pal.Core.UnitTests\")]");
        var dCore = Assert.Single(coreBad, x => x.Id == PalArchitectureAnalyzer.FriendAssemblyViolationId);
        Assert.Contains("Pal.Extract", dCore.GetMessage());

        var gameBad = await AnalyzeAsync("Pal.Game",
            "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"Pal.Game.Tests\")]");
        Assert.Single(gameBad, x => x.Id == PalArchitectureAnalyzer.FriendAssemblyViolationId);
    }

    [Fact]
    public async Task PAL0003_rejects_test_hooks_in_production_and_allows_normal_production_members()
    {
        var bad = await AnalyzeAsync("Pal.Core",
            """
            namespace Pal.Core.Battle;
            public sealed class Engine
            {
                public static bool EnableTestMode { get; set; }
                public void ResetForTest() {}
                public static int TestOverrideCounter;
            }
            """);
        var pal3 = bad.Where(x => x.Id == PalArchitectureAnalyzer.ProductionTestHookId).ToArray();
        Assert.Equal(3, pal3.Length);
        Assert.All(pal3, d => Assert.Contains("自修复指引", d.GetMessage()));

        var good = await AnalyzeAsync("Pal.Core",
            """
            namespace Pal.Core.Battle;
            public sealed class Engine
            {
                public static readonly int DefaultLimit = 10;
                public void Step() {}
            }
            """);
        Assert.DoesNotContain(good, x => x.Id == PalArchitectureAnalyzer.ProductionTestHookId);
    }
}
