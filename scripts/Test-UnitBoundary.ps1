# Lightweight source/project inspection only: never executes MSBuild or test discovery.
[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [AllowEmptyCollection()][AllowEmptyString()][string[]]$Project = @('core', 'cli-unit')
)
$ErrorActionPreference = 'Stop'
function Fail([string]$Code, [string]$Message) { throw "[boundary:$Code] $Message" }
# Root-level entries are allowlisted: one-off helper scripts accidentally committed at the repository
# root (e.g. _fix_loader.py in #53, _pb2.py caught by the M4 dual review) must fail here instead of
# riding along in delivery diffs. Missing entries are fine; unexpected entries are not.
$script:AllowedRootFiles = @('.gitattributes', '.gitignore', 'AGENTS.md', 'GLOSSARY.md', 'GodotPal.sln', 'NuGet.Config', 'README.md', 'global.json')
$script:AllowedRootDirectories = @('.github', 'data', 'docs', 'reference', 'samples', 'scripts', 'src', 'tests', 'tools')
function Test-RootFiles([string]$Root) {
    foreach ($entry in @(Get-ChildItem -LiteralPath $Root -Force | Where-Object Name -ne '.git')) {
        if ($entry.PSIsContainer) {
            if ($entry.Name -cnotin $script:AllowedRootDirectories) { Fail 'root-files' "Unexpected root directory: $($entry.Name); allowed: $($script:AllowedRootDirectories -join ', ')" }
        }
        elseif ($entry.Name -cnotin $script:AllowedRootFiles) {
            Fail 'root-files' "Unexpected root file: $($entry.Name); one-off helper scripts do not belong at the repository root (allowed: $($script:AllowedRootFiles -join ', '))"
        }
    }
}
$identities = @{
    core = 'tests/Pal.Core.UnitTests/Pal.Core.UnitTests.csproj'
    'cli-unit' = 'tests/Pal.Extract.UnitTests/Pal.Extract.UnitTests.csproj'
}
$production = @{
    core = 'src/Pal.Core/Pal.Core.csproj'
    'cli-unit' = 'tools/Pal.Extract/Pal.Extract.csproj'
}
function Test-ProjectShape([string]$Path, [xml]$Xml) {
    $allowedProperties = @('TargetFramework', 'ImplicitUsings', 'Nullable', 'IsPackable', 'IsTestProject', 'OutputType')
    $allowedPackages = @('Microsoft.NET.Test.Sdk', 'xunit', 'xunit.runner.visualstudio')
    # Only the InternalsVisibleToAttribute assembly attribute (friend assembly for Pal.Extract) is allowed;
    # any other AssemblyAttribute keeps failing this allowlist.
    $allowedAssemblyAttributes = @('System.Runtime.CompilerServices.InternalsVisibleToAttribute')
    if ($Xml.DocumentElement.Name -cne 'Project' -or $Xml.DocumentElement.NamespaceURI -or $Xml.Project.Sdk -cne 'Microsoft.NET.Sdk') {
        Fail 'project-shape' "$Path requires the ordinary Microsoft.NET.Sdk project shape"
    }
    foreach ($node in $Xml.SelectNodes('//*')) {
        $allowedAttributes = switch -CaseSensitive ($node.Name) {
            Project { @('Sdk') }
            ProjectReference { @('Include') }
            PackageReference { @('Include', 'Version') }
            Compile { @('Include', 'Link') }
            AssemblyAttribute { @('Include') }
            default { @() }
        }
        foreach ($attribute in $node.Attributes) {
            if ($attribute.Name -cnotin $allowedAttributes -or $attribute.Value -match '[$@]') {
                Fail 'project-shape' "$Path unsupported attribute: $($node.Name).$($attribute.Name)"
            }
        }
        $parent = $node.ParentNode.Name
        $supported = switch -CaseSensitive ($parent) {
            '#document' { $node.Name -ceq 'Project' }
            Project { $node.Name -cin @('PropertyGroup', 'ItemGroup') }
            PropertyGroup { $node.Name -cin $allowedProperties -and -not [string]::IsNullOrWhiteSpace($node.InnerText) -and $node.InnerText -notmatch '[$@]' -and $node.SelectNodes('*').Count -eq 0 }
            ItemGroup { $node.Name -cin @('ProjectReference', 'PackageReference', 'Compile', 'AssemblyAttribute') }
            PackageReference { $node.Name -cin @('PrivateAssets', 'IncludeAssets') -and $node.SelectNodes('*').Count -eq 0 -and $node.InnerText -notmatch '[$@]' }
            AssemblyAttribute { $node.Name -ceq '_Parameter1' -and $node.SelectNodes('*').Count -eq 0 -and -not [string]::IsNullOrWhiteSpace($node.InnerText) -and $node.InnerText -notmatch '[$@]' }
            default { $false }
        }
        if (-not $supported) { Fail 'project-shape' "$Path unsupported project element: $($node.Name)" }
        if ($node.Name -ceq 'PackageReference' -and ($node.Include -cnotin $allowedPackages -or $node.Version -notmatch '^\d[\w.+-]*$')) {
            Fail 'project-shape' "$Path unapproved package dependency: $($node.Include)"
        }
        if ($node.Name -ceq 'AssemblyAttribute' -and $node.Include -cnotin $allowedAssemblyAttributes) {
            Fail 'project-shape' "$Path unapproved assembly attribute: $($node.Include)"
        }
    }
    $directory = Split-Path $Path -Parent
    while ($directory -and $directory.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        foreach ($file in @('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props')) {
            if (Test-Path -LiteralPath (Join-Path $directory $file)) { Fail 'project-shape' "$Path has unsupported implicit import: $directory/$file" }
        }
        $directory = Split-Path $directory -Parent
    }
}
function Read-Project([string]$Path) {
    try {
        $settings = [Xml.XmlReaderSettings]::new()
        $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
        $reader = [Xml.XmlReader]::Create($Path, $settings)
        try { $xml = [xml]::new(); $xml.Load($reader) } finally { $reader.Dispose() }
    }
    catch { Fail 'project-read' "Cannot read project ${Path}: $($_.Exception.Message)" }
    if ($xml.SelectNodes('//Target | //Copy | //Exec').Count) {
        Fail 'build-target' "$Path contains custom build targets or deployment tasks"
    }
    Test-ProjectShape $Path $xml
    return $xml
}
function Test-Source([string]$Path) {
    $source = [IO.File]::ReadAllText($Path, [Text.Encoding]::UTF8)
    # Entire compilation unit, not just test methods. Interpolated text is conservatively retained.
    $literals = '(?s)//[^\r\n]*|/\*.*?\*/|\$*(?<quotes>"{3,}).*?\k<quotes>|\$?@"(?:""|[^"])*"|\$?"(?:\\.|[^"\\])*"|''(?:\\.|[^''\\])*'''
    $code = [regex]::Replace($source, $literals, [Text.RegularExpressions.MatchEvaluator]{
        param($match)
        if ($match.Value.StartsWith('$')) { return $match.Value }
        return [regex]::Replace($match.Value, '[^\r\n]', ' ')
    })
    $forbidden = '\b(?:CliProcess|DirectoryLinkFixture|MkfFixture|CaseSensitiveFileSystemFact(?:Attribute)?|File|Directory|FileInfo|DirectoryInfo|FileStream|FileSystemWatcher|StreamReader|StreamWriter|Process|ProcessStartInfo|Godot|GdUnit4|DllImport|LibraryImport)\b|\bPath\s*\.\s*GetTemp(?:Path|FileName)\s*\('
    $match = [regex]::Match($code, $forbidden)
    if ($match.Success) {
        $line = 1 + ([regex]::Matches($code.Substring(0, $match.Index), '\n')).Count
        Fail 'source-forbidden' "${Path}:${line} forbidden unit fixture token: $($match.Value)"
    }
}
function Test-UnitSources([string]$Path, [xml]$Xml) {
    $directory = Split-Path $Path -Parent
    $sources = @(Get-ChildItem -LiteralPath $directory -Recurse -File -Filter '*.cs' | Where-Object {
        [IO.Path]::GetRelativePath($directory, $_.FullName) -notmatch '(^|[\\/])(bin|obj)([\\/]|$)'
    } | ForEach-Object FullName)
    foreach ($compile in $Xml.SelectNodes('//Compile')) {
        $include = $compile.GetAttribute('Include')
        if (-not $include -or $include -match '[$*?;]') { Fail 'source-selection' "$Path requires literal explicit Compile Include" }
        $linkedPath = [IO.Path]::GetFullPath((Join-Path $directory $include))
        # Allowed explicit Compile links are pure shared fixtures only; every linked file is still
        # scanned by Test-Source below. Yj2TestData is the CLI chunk fixture; BattleContextTestFixture
        # is the in-memory battle context author document shared by Core unit tests.
        $allowedLinks = @(
            @{ Path = [IO.Path]::GetFullPath((Join-Path $root 'tests/Pal.Extract.Tests/Fixtures/Yj2TestData.cs')); Link = 'Fixtures/Yj2TestData.cs' }
            @{ Path = [IO.Path]::GetFullPath((Join-Path $root 'tests/Fixtures/BattleContextTestFixture.cs')); Link = 'BattleContextTestFixture.cs' }
        )
        if (-not (@($allowedLinks | Where-Object { $_.Path -ieq $linkedPath -and $_.Link -ceq $compile.GetAttribute('Link') }))) {
            Fail 'source-selection' "$Path only permits the explicit pure shared fixture links, not: $include"
        }
        $sources += $linkedPath
    }
    if (-not $sources.Count) { Fail 'source-selection' "$Path has no source files to inspect" }
    foreach ($source in ($sources | Sort-Object -Unique)) {
        if (-not [IO.File]::Exists($source)) { Fail 'source-selection' "Missing Compile source: $source" }
        Test-Source $source
    }
    return ($sources | Sort-Object -Unique).Count
}
try {
    if (-not $Project.Count) { Fail 'selection' 'Select at least one of core, cli-unit' }
    $seen = @{}
    foreach ($name in $Project) {
        if (-not $name -or -not $identities.ContainsKey($name) -or $seen.ContainsKey($name)) {
            Fail 'selection' "Unknown, empty or duplicate project selection: '$name'; allowed: core, cli-unit"
        }
        $seen[$name] = $true
    }
    if (-not $RepositoryRoot -or -not [IO.Directory]::Exists($RepositoryRoot)) { Fail 'project-missing' "Repository root not found: $RepositoryRoot" }
    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    Test-RootFiles $root
    foreach ($name in $Project) {
        $path = Join-Path $root $identities[$name]
        if (-not [IO.File]::Exists($path)) { Fail 'project-missing' "Missing applicable unit project: $path" }
        $xml = Read-Project $path
        $testIdentity = @($xml.SelectNodes('/Project/PropertyGroup/IsTestProject'))
        if ($testIdentity.Count -ne 1 -or $testIdentity[0].InnerText -ne 'true') { Fail 'test-project' "$path requires explicit IsTestProject=true" }
        $expected = [IO.Path]::GetFullPath((Join-Path $root $production[$name]))
        $references = @($xml.SelectNodes('//ProjectReference'))
        foreach ($reference in $references) {
            $include = $reference.GetAttribute('Include')
            if ($include -match '[$*?;]' -or [IO.Path]::GetFullPath((Join-Path (Split-Path $path -Parent) $include)) -ne $expected) {
                Fail 'project-reference' "$path references forbidden or nonliteral project: $include"
            }
        }
        if ($references.Count -ne 1) { Fail 'project-reference' "$path must reference exactly $expected" }
        if (-not [IO.File]::Exists($expected)) { Fail 'project-missing' "Missing production project: $expected" }
        $productionXml = Read-Project $expected
        # Approved production-to-production references keep the unit closure pure .NET:
        # Pal.Core must stay self-contained; the Pal.Extract CLI may reference only Pal.Core.
        $approvedProductionReferences = @{
            core = @()
            'cli-unit' = @([IO.Path]::GetFullPath((Join-Path $root 'src/Pal.Core/Pal.Core.csproj')))
        }
        $approved = @($approvedProductionReferences[$name] | ForEach-Object { [string]$_ })
        foreach ($reference in @($productionXml.SelectNodes('//ProjectReference'))) {
            $include = $reference.GetAttribute('Include')
            if ($include -match '[$*?;]') { Fail 'project-reference' "$expected has a nonliteral production reference: $include" }
            $resolved = [IO.Path]::GetFullPath((Join-Path (Split-Path $expected -Parent) $include))
            if ($resolved -cnotin $approved) {
                Fail 'project-reference' "$expected has an unapproved transitive project reference: $include"
            }
        }
        $sourceCount = Test-UnitSources $path $xml
        Write-Output "PASS unit-boundary: $name ($sourceCount sources inspected; explicit production reference checked)"
    }
    exit 0
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
