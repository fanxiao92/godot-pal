using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Pal.Analyzers;

/// <summary>Compile-time architecture and taste guardrails for godot-pal (#274).
/// Enforces PAL0001 (4 directional layer invariants), PAL0002 (InternalsVisibleTo allowlist),
/// and PAL0003 (no test-only switches/hooks in production src/).</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PalArchitectureAnalyzer : DiagnosticAnalyzer
{
    public const string LayerViolationId = "PAL0001";
    public const string FriendAssemblyViolationId = "PAL0002";
    public const string ProductionTestHookId = "PAL0003";

    public static readonly DiagnosticDescriptor LayerViolationRule = new(
        id: LayerViolationId,
        title: "Architecture layer boundary violation",
        messageFormat: "违规事实：{0} 依赖了禁止的命名空间 {1}。架构理由：{2} 自修复指引：{3}",
        category: "Architecture",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor FriendAssemblyViolationRule = new(
        id: FriendAssemblyViolationId,
        title: "Unauthorized InternalsVisibleTo friend assembly",
        messageFormat: "违规事实：程序集 {0} 声明了未授权的友元程序集 \"{1}\"。架构理由：{2} 自修复指引：{3}",
        category: "Architecture",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

    public static readonly DiagnosticDescriptor ProductionTestHookRule = new(
        id: ProductionTestHookId,
        title: "Test-only switch or hook in production code",
        messageFormat: "违规事实：生产代码声明了测试专用符号 '{0}'。架构理由：src/ 生产代码不得植入测试开关或可变静态测试钩子，避免测试路径与真实运行行为分叉。自修复指引：请通过构造函数参数或显式 public 接口注入依赖，并在 tests/ 内使用局部 IDisposable 夹具替代。",
        category: "Architecture",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(LayerViolationRule, FriendAssemblyViolationRule, ProductionTestHookRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationAction(AnalyzeCompilationAttributes);
        context.RegisterSyntaxNodeAction(AnalyzeUsingDirective, SyntaxKind.UsingDirective);
        context.RegisterSyntaxNodeAction(AnalyzeQualifiedOrIdentifierType, SyntaxKind.IdentifierName, SyntaxKind.QualifiedName);
        context.RegisterSymbolAction(AnalyzeProductionSymbol, SymbolKind.NamedType, SymbolKind.Method, SymbolKind.Property, SymbolKind.Field);
    }

    private static void AnalyzeCompilationAttributes(CompilationAnalysisContext context)
    {
        string assemblyName = context.Compilation.AssemblyName ?? string.Empty;
        if (assemblyName is not ("Pal.Core" or "Pal.Game")) return;

        foreach (AttributeData attr in context.Compilation.Assembly.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != "System.Runtime.CompilerServices.InternalsVisibleToAttribute")
                continue;

            string friend = attr.ConstructorArguments.Length > 0
                ? attr.ConstructorArguments[0].Value as string ?? string.Empty
                : string.Empty;
            Location location = attr.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                ?? Location.None;

            if (assemblyName == "Pal.Game")
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    FriendAssemblyViolationRule,
                    location,
                    assemblyName,
                    friend,
                    "Pal.Game 使用 InternalsVisibleTo 会导致 Godot 源码生成器 CS0436 冲突并破坏测试边界。",
                    "请移除 InternalsVisibleTo，改为显式 public 接缝（见 CODING_STANDARDS.md）。"));
            }
            else if (assemblyName == "Pal.Core" && !string.Equals(friend, "Pal.Extract", StringComparison.Ordinal))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    FriendAssemblyViolationRule,
                    location,
                    assemblyName,
                    friend,
                    "Pal.Core 友元白名单仅允许离线工具 Pal.Extract，禁止向测试或引擎层开放内部实现。",
                    "请移除该 InternalsVisibleTo 声明，或将需要被测试/引擎消费的契约提升为显式 public API。"));
            }
        }
    }

    private static void AnalyzeUsingDirective(SyntaxNodeAnalysisContext context)
    {
        var usingDirective = (UsingDirectiveSyntax)context.Node;
        if (usingDirective.Name is null) return;

        string callerNs = GetEnclosingNamespace(usingDirective);
        if (string.IsNullOrEmpty(callerNs)) return;

        SymbolInfo info = context.SemanticModel.GetSymbolInfo(usingDirective.Name, context.CancellationToken);
        string? targetNs = info.Symbol switch
        {
            INamespaceSymbol ns => ns.ToDisplayString(),
            INamedTypeSymbol t => t.ContainingNamespace?.ToDisplayString(),
            _ => usingDirective.Name.ToString()
        };

        if (string.IsNullOrEmpty(targetNs)) return;
        CheckLayerRule(context, usingDirective.GetLocation(), callerNs, targetNs!);
    }

    private static void AnalyzeQualifiedOrIdentifierType(SyntaxNodeAnalysisContext context)
    {
        if (context.Node.FirstAncestorOrSelf<UsingDirectiveSyntax>() is not null) return;
        if (context.Node.FirstAncestorOrSelf<BaseNamespaceDeclarationSyntax>() is { Name: var nsName } && nsName.Contains(context.Node))
            return;

        // Avoid double-reporting on the right side of a QualifiedName already inspected
        if (context.Node is IdentifierNameSyntax && context.Node.Parent is QualifiedNameSyntax qn && qn.Right == context.Node)
            return;

        string callerNs = GetEnclosingNamespace(context.Node);
        if (string.IsNullOrEmpty(callerNs)) return;

        SymbolInfo info = context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken);
        string? targetNs = info.Symbol switch
        {
            INamedTypeSymbol t => t.ContainingNamespace?.ToDisplayString(),
            IMethodSymbol m => m.ContainingType?.ContainingNamespace?.ToDisplayString(),
            IPropertySymbol p => p.ContainingType?.ContainingNamespace?.ToDisplayString(),
            IFieldSymbol f => f.ContainingType?.ContainingNamespace?.ToDisplayString(),
            _ => null
        };

        if (string.IsNullOrEmpty(targetNs) || string.Equals(callerNs, targetNs, StringComparison.Ordinal)) return;
        CheckLayerRule(context, context.Node.GetLocation(), callerNs, targetNs!);
    }

    private static void CheckLayerRule(SyntaxNodeAnalysisContext context, Location location, string callerNs, string targetNs)
    {
        if (string.Equals(callerNs, targetNs, StringComparison.Ordinal)) return;

        // Rule 1: Formal domains must not depend on Pal.Core.Legacy
        if (IsInNamespace(targetNs, "Pal.Core.Legacy") && IsFormalDomainForbiddenFromLegacy(callerNs))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                LayerViolationRule,
                location,
                callerNs,
                targetNs,
                "Pal.Core.Legacy 为冻结的 M4 首战历史切片，禁止正式域反向依赖。",
                "请将需复用的共享模型提取至 Pal.Core.State 或 Pal.Core.Shared，不要引用 Pal.Core.Legacy。"));
            return;
        }

        // Rule 2: Pal.Game runtime must not depend on Pal.Game.Editor
        if (IsInNamespace(targetNs, "Pal.Game.Editor")
            && IsInNamespace(callerNs, "Pal.Game")
            && !IsInNamespace(callerNs, "Pal.Game.Editor"))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                LayerViolationRule,
                location,
                callerNs,
                targetNs,
                "Pal.Game.Editor 仅用于离线资源准备工具，运行时命名空间不得依赖编辑器脚本。",
                "请将共享运行时数据结构放在 Pal.Game.Resources 或 Pal.Core，避免运行时引用 Pal.Game.Editor。"));
            return;
        }

        // Rule 3: Foundation layers (Pal.Core.Shared, Pal.Core.State, Pal.Game.Resources) must not reverse-depend on upper layers
        if (IsInNamespace(callerNs, "Pal.Core.Shared") && IsUpperCoreNamespace(targetNs, allowState: false))
        {
            ReportFoundationReverseDependency(context, location, callerNs, targetNs);
            return;
        }
        if (IsInNamespace(callerNs, "Pal.Core.State") && IsUpperCoreNamespace(targetNs, allowState: true))
        {
            ReportFoundationReverseDependency(context, location, callerNs, targetNs);
            return;
        }
        if (IsInNamespace(callerNs, "Pal.Game.Resources") && IsUpperGameNamespace(targetNs))
        {
            ReportFoundationReverseDependency(context, location, callerNs, targetNs);
            return;
        }

        // Rule 4: Pal.Game view layers (Dialogue, Battle, UI) must not depend on each other
        if (IsGameViewNamespace(callerNs, out string callerView)
            && IsGameViewNamespace(targetNs, out string targetView)
            && !string.Equals(callerView, targetView, StringComparison.Ordinal))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                LayerViolationRule,
                location,
                callerNs,
                targetNs,
                "Pal.Game 视图层（Dialogue / Battle / UI）必须保持彼此独立，避免跨视图隐式耦合。",
                "请将跨视图协调逻辑上移至顶层宿主 Pal.Game（Main*.cs），或将共享纯契约接口下沉至 Pal.Core。"));
        }
    }

    private static void ReportFoundationReverseDependency(SyntaxNodeAnalysisContext context, Location location, string callerNs, string targetNs)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            LayerViolationRule,
            location,
            callerNs,
            targetNs,
            "底座层（Pal.Core.Shared / Pal.Core.State / Pal.Game.Resources）不得反向依赖上层业务会话、加载器或视图层。",
            "请将共享值类型或接口下沉至底座层，或将依赖反转至上层调用方。"));
    }

    private static void AnalyzeProductionSymbol(SymbolAnalysisContext context)
    {
        string assemblyName = context.Compilation.AssemblyName ?? string.Empty;
        if (assemblyName is not ("Pal.Core" or "Pal.Game")) return;

        ISymbol symbol = context.Symbol;
        if (symbol.IsImplicitlyDeclared) return;
        if (symbol is IMethodSymbol method && method.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove)
            return;

        string name = symbol.Name;
        if (HasForbiddenTestHookName(name))
        {
            ReportTestHook(context, symbol);
            return;
        }

        if (symbol is IFieldSymbol field && field.IsStatic && !field.IsReadOnly && !field.IsConst
            && HasSuspiciousMutableStaticTestName(field.Name))
        {
            ReportTestHook(context, symbol);
        }
        else if (symbol is IPropertySymbol prop && prop.IsStatic && prop.SetMethod is not null
            && HasSuspiciousMutableStaticTestName(prop.Name))
        {
            ReportTestHook(context, symbol);
        }
    }

    private static void ReportTestHook(SymbolAnalysisContext context, ISymbol symbol)
    {
        Location loc = symbol.Locations.FirstOrDefault() ?? Location.None;
        context.ReportDiagnostic(Diagnostic.Create(ProductionTestHookRule, loc, symbol.Name));
    }

    private static bool HasForbiddenTestHookName(string name) =>
        name.IndexOf("ForTest", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("TestMode", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("TestHook", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool HasSuspiciousMutableStaticTestName(string name) =>
        name.IndexOf("Test", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("Mock", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("Stub", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsFormalDomainForbiddenFromLegacy(string ns) =>
        ns == "Pal.Core"
        || IsInNamespace(ns, "Pal.Core.Shared")
        || IsInNamespace(ns, "Pal.Core.State")
        || IsInNamespace(ns, "Pal.Core.Exploration")
        || IsInNamespace(ns, "Pal.Core.Dialogue")
        || IsInNamespace(ns, "Pal.Core.Battle")
        || IsInNamespace(ns, "Pal.Core.Events")
        || IsInNamespace(ns, "Pal.Core.Menu")
        || IsInNamespace(ns, "Pal.Core.Save")
        || IsInNamespace(ns, "Pal.Game.Resources")
        || IsInNamespace(ns, "Pal.Game.Map")
        || IsInNamespace(ns, "Pal.Game.Dialogue")
        || IsInNamespace(ns, "Pal.Game.Battle")
        || IsInNamespace(ns, "Pal.Game.UI");

    private static bool IsUpperCoreNamespace(string ns, bool allowState) =>
        (!allowState && IsInNamespace(ns, "Pal.Core.State"))
        || IsInNamespace(ns, "Pal.Core.Exploration")
        || IsInNamespace(ns, "Pal.Core.Dialogue")
        || IsInNamespace(ns, "Pal.Core.Battle")
        || IsInNamespace(ns, "Pal.Core.Events")
        || IsInNamespace(ns, "Pal.Core.Menu")
        || IsInNamespace(ns, "Pal.Core.Save")
        || IsInNamespace(ns, "Pal.Core.Legacy");

    private static bool IsUpperGameNamespace(string ns) =>
        ns == "Pal.Game"
        || IsInNamespace(ns, "Pal.Game.Map")
        || IsInNamespace(ns, "Pal.Game.Profiles")
        || IsInNamespace(ns, "Pal.Game.Dialogue")
        || IsInNamespace(ns, "Pal.Game.Battle")
        || IsInNamespace(ns, "Pal.Game.UI")
        || IsInNamespace(ns, "Pal.Game.Editor");

    private static bool IsGameViewNamespace(string ns, out string viewRoot)
    {
        if (IsInNamespace(ns, "Pal.Game.Dialogue")) { viewRoot = "Pal.Game.Dialogue"; return true; }
        if (IsInNamespace(ns, "Pal.Game.Battle")) { viewRoot = "Pal.Game.Battle"; return true; }
        if (IsInNamespace(ns, "Pal.Game.UI")) { viewRoot = "Pal.Game.UI"; return true; }
        viewRoot = string.Empty;
        return false;
    }

    private static bool IsInNamespace(string candidate, string root) =>
        string.Equals(candidate, root, StringComparison.Ordinal)
        || (candidate.Length > root.Length
            && candidate.StartsWith(root, StringComparison.Ordinal)
            && candidate[root.Length] == '.');

    private static string GetEnclosingNamespace(SyntaxNode node)
    {
        var decl = node.FirstAncestorOrSelf<BaseNamespaceDeclarationSyntax>();
        if (decl is not null) return decl.Name.ToString();

        var root = node.SyntaxTree.GetRoot();
        var fileScoped = root.DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault();
        if (fileScoped is not null) return fileScoped.Name.ToString();

        var firstNs = root.DescendantNodes().OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
        return firstNs?.Name.ToString() ?? string.Empty;
    }
}
