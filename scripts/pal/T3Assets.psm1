Set-StrictMode -Version Latest

if (-not (Get-Command Resolve-ReadyGodotBinary -ErrorAction SilentlyContinue)) {
    Import-Module (Join-Path $PSScriptRoot 'PalCommon.psm1') -Global
}

$script:T3PrepareVersion = 'v1'
$script:CoreRawArchives = @(
    'sss.mkf', 'data.mkf', 'map.mkf', 'gop.mkf', 'mgo.mkf',
    'pat.mkf', 'rgm.mkf', 'm_chs.slf', 'm_eng.slf'
)
$script:OptionalRawArchives = @(
    'm.msg', 'word.dat', 'abc.mkf', 'f.mkf', 'fbp.mkf', 'fire.mkf', 'ball.mkf'
)

function Get-T3CacheRoot {
    if (-not [string]::IsNullOrWhiteSpace($env:PAL_CACHE_DIR)) {
        return [IO.Path]::GetFullPath($env:PAL_CACHE_DIR)
    }
    if ($IsWindows -and -not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        return [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'godot-pal-cache'))
    }
    $homeDir = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    if ([string]::IsNullOrWhiteSpace($homeDir)) { $homeDir = [IO.Path]::GetTempPath() }
    return [IO.Path]::GetFullPath((Join-Path $homeDir '.cache/godot-pal-cache'))
}

function Get-T3RawDataDirectory {
    $rawDir = $env:PAL_RAW_DATA_DIR
    if ([string]::IsNullOrWhiteSpace($rawDir)) {
        throw 'PAL_RAW_DATA_DIR is not set. Set PAL_RAW_DATA_DIR to the local directory containing original PAL game archives (SSS.MKF, DATA.MKF, MAP.MKF, GOP.MKF, MGO.MKF, PAT.MKF, RGM.MKF, m_chs.slf, m_eng.slf).'
    }
    $full = [IO.Path]::GetFullPath($rawDir)
    if (-not (Test-Path -LiteralPath $full -PathType Container)) {
        throw 'PAL_RAW_DATA_DIR does not point to an existing directory.'
    }
    return $full
}

function Get-FileSha256Lower {
    param([Parameter(Mandatory)][string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-T3RawDataHash {
    param([Parameter(Mandatory)][string]$RawDataDir)
    $byLower = @{}
    foreach ($file in @(Get-ChildItem -LiteralPath $RawDataDir -File)) {
        $byLower[$file.Name.ToLowerInvariant()] = $file
    }
    $missing = @($script:CoreRawArchives | Where-Object { -not $byLower.ContainsKey($_) })
    if ($missing.Count -gt 0) {
        throw "PAL_RAW_DATA_DIR is missing required archive(s): $($missing -join ', ')."
    }
    $lines = [Collections.Generic.List[string]]::new()
    foreach ($name in @($script:CoreRawArchives + $script:OptionalRawArchives | Sort-Object -Unique)) {
        if ($byLower.ContainsKey($name)) {
            $fi = $byLower[$name]
            $h = Get-FileSha256Lower -Path $fi.FullName
            $lines.Add("$name|$($fi.Length)|$h")
        }
    }
    $payload = [Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))
    $sha = [Security.Cryptography.SHA256]::HashData($payload)
    return ([Convert]::ToHexString($sha).ToLowerInvariant()).Substring(0, 16)
}

function Get-T3CacheInfo {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [switch]$RequireRawData
    )
    $cacheRoot = Get-T3CacheRoot
    $worktreeLink = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot 'src/Pal.Game/Assets/T3Real'))
    $rawPresent = -not [string]::IsNullOrWhiteSpace($env:PAL_RAW_DATA_DIR) -and (Test-Path -LiteralPath $env:PAL_RAW_DATA_DIR -PathType Container)
    if ($RequireRawData) {
        $rawDir = Get-T3RawDataDirectory
        $rawHash = Get-T3RawDataHash -RawDataDir $rawDir
    } elseif ($rawPresent) {
        $rawDir = [IO.Path]::GetFullPath($env:PAL_RAW_DATA_DIR)
        $rawHash = Get-T3RawDataHash -RawDataDir $rawDir
    } else {
        return [ordered]@{
            Configured       = $false
            RawHash          = $null
            PrepareVersion   = $script:T3PrepareVersion
            CacheKey         = $null
            CacheRoot        = $cacheRoot
            AssetsDir        = $null
            GoldensDir       = $null
            WorktreeLinkDir  = $worktreeLink
            AssetsHit        = $false
            GoldensHit       = $false
            GoldenManifest   = $null
        }
    }

    $assetsDir = Join-Path $cacheRoot "assets/$rawHash-$($script:T3PrepareVersion)"
    $goldensDir = Join-Path $cacheRoot "goldens/$rawHash"
    $assetsManifest = Join-Path $assetsDir 'cache-manifest.json'
    $goldenManifest = Join-Path $goldensDir 'golden-manifest.json'
    return [ordered]@{
        Configured       = $true
        RawHash          = $rawHash
        PrepareVersion   = $script:T3PrepareVersion
        CacheKey         = "raw:sha256-$rawHash + prepare:$($script:T3PrepareVersion)"
        CacheRoot        = $cacheRoot
        AssetsDir        = [IO.Path]::GetFullPath($assetsDir)
        GoldensDir       = [IO.Path]::GetFullPath($goldensDir)
        WorktreeLinkDir  = $worktreeLink
        AssetsHit        = (Test-Path -LiteralPath $assetsManifest -PathType Leaf)
        GoldensHit       = (Test-Path -LiteralPath $goldenManifest -PathType Leaf)
        GoldenManifest   = [IO.Path]::GetFullPath($goldenManifest)
    }
}

function Remove-T3WorktreeLinkOrDir {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $attrs = [IO.File]::GetAttributes($Path)
    if ($attrs -band [IO.FileAttributes]::ReparsePoint) {
        [IO.Directory]::Delete($Path, $false)
    } else {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Copy-DirectoryRecursive {
    param(
        [Parameter(Mandatory)][string]$SourceDir,
        [Parameter(Mandatory)][string]$DestinationDir
    )
    [void][IO.Directory]::CreateDirectory($DestinationDir)
    foreach ($file in [IO.Directory]::EnumerateFiles($SourceDir, '*', [IO.SearchOption]::AllDirectories)) {
        $rel = [IO.Path]::GetRelativePath($SourceDir, $file)
        $dst = Join-Path $DestinationDir $rel
        [void][IO.Directory]::CreateDirectory((Split-Path $dst -Parent))
        [IO.File]::Copy($file, $dst, $true)
    }
}

function Set-T3WorktreeLink {
    param(
        [Parameter(Mandatory)][string]$WorktreeLinkDir,
        [Parameter(Mandatory)][string]$CacheAssetsDir
    )
    $parent = Split-Path $WorktreeLinkDir -Parent
    [void][IO.Directory]::CreateDirectory($parent)
    if (Test-Path -LiteralPath $WorktreeLinkDir) {
        $item = Get-Item -LiteralPath $WorktreeLinkDir -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -and $item.Target) {
            $targetFull = [IO.Path]::GetFullPath(([string]@($item.Target)[0]))
            $cmp = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
            if ($targetFull.Equals($CacheAssetsDir, $cmp)) {
                return 'linked'
            }
        }
        Remove-T3WorktreeLinkOrDir -Path $WorktreeLinkDir
    }
    try {
        if ($IsWindows) {
            [void](New-Item -ItemType Junction -Path $WorktreeLinkDir -Target $CacheAssetsDir -Force)
        } else {
            [void](New-Item -ItemType SymbolicLink -Path $WorktreeLinkDir -Target $CacheAssetsDir -Force)
        }
        return 'linked'
    } catch {
        Copy-DirectoryRecursive -SourceDir $CacheAssetsDir -DestinationDir $WorktreeLinkDir
        return 'copied'
    }
}

function Write-T3Scene4Profiles {
    param(
        [Parameter(Mandatory)][string]$T3RootDir,
        [Parameter(Mandatory)][string]$ExtractScene4Dir
    )
    $utf8 = [Text.UTF8Encoding]::new($false)
    $manifestPath = Join-Path $ExtractScene4Dir 'manifest.json'
    $manifest = [IO.File]::ReadAllText($manifestPath, $utf8) | ConvertFrom-Json
    $bundleSha = [string]$manifest.bundleSha256
    $dataSource = @($manifest.sources | Where-Object { $_.name -ieq 'data.mkf' }) | Select-Object -First 1
    if (-not $dataSource) { throw 'Scene4 extract manifest is missing data.mkf source entry.' }
    $dataSha = [string]$dataSource.sha256

    $explorationTres = @"
[gd_resource type="Resource" script_class="PalExplorationProfile" format=3]

[ext_resource type="Script" path="res://Scripts/Resources/PalExplorationStart.cs" id="1"]
[ext_resource type="Script" path="res://Scripts/Resources/PalWalkFramesSource.cs" id="2"]
[ext_resource type="Script" path="res://Scripts/Resources/PalExplorationLeader.cs" id="3"]
[ext_resource type="Script" path="res://Scripts/Resources/PalExplorationProfile.cs" id="4"]

[sub_resource type="Resource" id="s0"]
script = ExtResource("1")
X = 1232
Y = 1336
Direction = 0
Layer = 0
StepPhase = 0

[sub_resource type="Resource" id="s1"]
script = ExtResource("2")
Archive = "data.mkf"
Sha256 = "$dataSha"
Chunk = 3
ByteOffset = 768
RoleId = 0
Field = "rgwWalkFrames"

[sub_resource type="Resource" id="s2"]
script = ExtResource("3")
RoleId = 0
WalkFramesWord = 3
WalkFramesSource = SubResource("s1")

[resource]
script = ExtResource("4")
FormatVersion = 1
SceneId = 4
BundleSha256 = "$bundleSha"
Start = SubResource("s0")
Leader = SubResource("s2")
"@
    $explorationPath = Join-Path $T3RootDir 'Scene4/Exploration.tres'
    [IO.File]::WriteAllText($explorationPath, $explorationTres.Replace("`r`n", "`n"), $utf8)

    $eventsIndexPath = Join-Path $T3RootDir 'Events/index.json'
    $eventsIndex = [IO.File]::ReadAllText($eventsIndexPath, $utf8) | ConvertFrom-Json
    $hashEntries = [Collections.Generic.List[string]]::new()
    $uriPaths = [Collections.Generic.List[object]]::new()
    foreach ($fid in @($eventsIndex.files)) {
        $uriPaths.Add(@{ Uri = "res://Assets/T3Real/Events/$fid.json"; Full = (Join-Path $T3RootDir "Events/$fid.json") })
    }
    $uriPaths.Add(@{ Uri = 'res://Assets/T3Real/Events/index.json'; Full = $eventsIndexPath })
    $uriPaths.Add(@{ Uri = 'res://Assets/T3Real/Scene4/Exploration.tres'; Full = $explorationPath })
    foreach ($dlgFile in @('Font.tres', 'Language.zh-Hans.tres', 'Language.en.tres', 'Portrait-059.tres', 'Prompt-00.tres', 'Prompt-01.tres', 'Prompt-02.tres')) {
        $uriPaths.Add(@{ Uri = "res://Assets/T3Real/Dialogue/Dialogue/$dlgFile"; Full = (Join-Path $T3RootDir "Dialogue/Dialogue/$dlgFile") })
    }
    foreach ($item in $uriPaths) {
        $h = Get-FileSha256Lower -Path $item.Full
        $hashEntries.Add("`"$($item.Uri)`": `"$h`"")
    }
    $dictText = '{' + ($hashEntries -join ', ') + '}'
    $npcTres = @"
[gd_resource type="Resource" script_class="PalNpcInteractionProfile" format=3]

[ext_resource type="Script" path="res://Scripts/Resources/PalNpcInteractionProfile.cs" id="1"]

[resource]
script = ExtResource("1")
FormatVersion = 1
StaticResources = "res://Assets/T3Real/Scene4"
StaticProfile = "res://Assets/T3Real/Scene4/Exploration.tres"
BundleSha256 = "$bundleSha"
EventsIndex = "res://Assets/T3Real/Events/index.json"
DialogueDirectory = "res://Assets/T3Real/Dialogue/Dialogue"
ResourceSha256 = $dictText
"@
    $npcPath = Join-Path $T3RootDir 'Scene4/NpcInteraction.tres'
    [IO.File]::WriteAllText($npcPath, $npcTres.Replace("`r`n", "`n"), $utf8)
}

function Invoke-PrepareT3StoryPackage {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$SampleName,
        [Parameter(Mandatory)][string]$TargetSubdir,
        [Parameter(Mandatory)][string]$StaticResourcesUri,
        [string]$StaticProfileUri,
        [Parameter(Mandatory)][string]$T3RootDir,
        [Parameter(Mandatory)][string]$TempRoot,
        [Parameter(Mandatory)][string]$GodotExe
    )
    $utf8 = [Text.UTF8Encoding]::new($false)
    $sampleDir = Join-Path $RepositoryRoot "samples/authored-battle/$SampleName"
    $numbersIn = Join-Path $sampleDir 'Numbers'
    $numbersOut = Join-Path $T3RootDir "$TargetSubdir/Numbers"
    & pwsh -NoProfile -NonInteractive -File (Join-Path $RepositoryRoot 'scripts/Prepare-BattleData.ps1') `
        -InputPath $numbersIn -OutputPath $numbersOut -GodotPath $GodotExe | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Prepare-BattleData failed for T3 $SampleName (exit $LASTEXITCODE)." }

    $storyIn = Join-Path $TempRoot "story-$SampleName"
    [void][IO.Directory]::CreateDirectory((Join-Path $storyIn 'events'))
    Copy-DirectoryRecursive -SourceDir (Join-Path $sampleDir 'Story/events') -DestinationDir (Join-Path $storyIn 'events')
    $metaPath = Join-Path $sampleDir 'Story/story-package.json'
    $meta = [IO.File]::ReadAllText($metaPath, $utf8) | ConvertFrom-Json
    $meta.staticResources = $StaticResourcesUri
    if ($StaticProfileUri) { $meta.staticProfile = $StaticProfileUri }
    $meta.battleContentUri = "res://Assets/T3Real/$TargetSubdir/Numbers/BattleContent.tres"
    [IO.File]::WriteAllText((Join-Path $storyIn 'story-package.json'), ($meta | ConvertTo-Json -Depth 12), $utf8)

    $storyOut = Join-Path $T3RootDir "$TargetSubdir/Story"
    & pwsh -NoProfile -NonInteractive -File (Join-Path $RepositoryRoot 'scripts/Prepare-StoryPackage.ps1') `
        -InputPath $storyIn -OutputPath $storyOut -GodotPath $GodotExe | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Prepare-StoryPackage failed for T3 $SampleName (exit $LASTEXITCODE)." }
}

function Initialize-T3Assets {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [string]$GodotPath = ''
    )
    $rawDir = Get-T3RawDataDirectory
    $info = Get-T3CacheInfo -RepositoryRoot $RepositoryRoot -RequireRawData
    [void][IO.Directory]::CreateDirectory($info.GoldensDir)

    if ($info.AssetsHit) {
        $linkMode = Set-T3WorktreeLink -WorktreeLinkDir $info.WorktreeLinkDir -CacheAssetsDir $info.AssetsDir
        $info['LinkMode'] = $linkMode
        $info['PreparedNow'] = $false
        return $info
    }

    $godot = Resolve-ReadyGodotBinary -ExplicitPath $GodotPath
    $extractProj = Join-Path $RepositoryRoot 'tools/Pal.Extract/Pal.Extract.csproj'
    Write-Host '==> Building Pal.Extract for T3 real-asset preparation'
    & dotnet build $extractProj --configuration Debug --nologo --verbosity quiet | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Failed to build Pal.Extract (exit $LASTEXITCODE)." }

    $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('pal-t3-prepare-' + [guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($tempRoot)
    $t3BuildDir = $info.WorktreeLinkDir
    try {
        Remove-T3WorktreeLinkOrDir -Path $t3BuildDir
        [void][IO.Directory]::CreateDirectory($t3BuildDir)

        $extScene4 = Join-Path $tempRoot 'extract-scene4'
        $extDialogue = Join-Path $tempRoot 'extract-dialogue'
        $extEvents = Join-Path $tempRoot 'extract-events'
        $refSdlpal = Join-Path $RepositoryRoot 'reference/sdlpal'

        Write-Host '==> Extracting real Scene 4 / Map 10 from PAL_RAW_DATA_DIR'
        & dotnet run --no-build --project $extractProj --configuration Debug -- extract-scene --input $rawDir --output $extScene4 --scene 4 --palette 0 --lighting day | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "pal-extract extract-scene failed (exit $LASTEXITCODE)." }

        Write-Host '==> Extracting real bilingual dialogue from PAL_RAW_DATA_DIR'
        & dotnet run --no-build --project $extractProj --configuration Debug -- extract-dialogue --input $rawDir --reference $refSdlpal --output $extDialogue | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "pal-extract extract-dialogue failed (exit $LASTEXITCODE)." }

        Write-Host '==> Exporting real SSS.MKF events from PAL_RAW_DATA_DIR'
        & dotnet run --no-build --project $extractProj --configuration Debug -- export-events --input $rawDir --output $extEvents | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "pal-extract export-events failed (exit $LASTEXITCODE)." }

        Write-Host '==> Preparing native Godot Scene 4 assets'
        & pwsh -NoProfile -NonInteractive -File (Join-Path $RepositoryRoot 'scripts/Prepare-GameScene.ps1') `
            -InputPath $extScene4 -OutputPath (Join-Path $t3BuildDir 'Scene4') -GodotPath $godot | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Prepare-GameScene failed for T3 Scene4 (exit $LASTEXITCODE)." }

        Write-Host '==> Preparing native Godot bilingual dialogue assets'
        & pwsh -NoProfile -NonInteractive -File (Join-Path $RepositoryRoot 'scripts/Prepare-DialoguePackage.ps1') `
            -InputPath $extDialogue -OutputPath (Join-Path $t3BuildDir 'Dialogue') -GodotPath $godot | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Prepare-DialoguePackage failed for T3 Dialogue (exit $LASTEXITCODE)." }

        Copy-DirectoryRecursive -SourceDir $extEvents -DestinationDir (Join-Path $t3BuildDir 'Events')
        Write-T3Scene4Profiles -T3RootDir $t3BuildDir -ExtractScene4Dir $extScene4

        # Prepare authored static + story packages for battle-closure, save-load-restore, and dynamic-scene
        $authoredStaticDst = Join-Path $t3BuildDir 'AuthoredStatic/Scenes'
        [void][IO.Directory]::CreateDirectory($authoredStaticDst)
        [IO.File]::Copy(
            (Join-Path $RepositoryRoot 'src/Pal.Game/Samples/AuthoredBattle/Static/Scenes/Scene.tscn'),
            (Join-Path $authoredStaticDst 'Scene.tscn'),
            $true
        )
        $dynamicStaticSrc = Join-Path $RepositoryRoot 'samples/authored-battle/Dynamic/Static'
        Copy-DirectoryRecursive -SourceDir $dynamicStaticSrc -DestinationDir (Join-Path $t3BuildDir 'DynamicStatic')

        Write-Host '==> Preparing T3 story packages (BattleStory, LostReloadStory, DynamicStory)'
        Invoke-PrepareT3StoryPackage -RepositoryRoot $RepositoryRoot -SampleName 'PartyWin' -TargetSubdir 'BattleStory' `
            -StaticResourcesUri 'res://Assets/T3Real/AuthoredStatic' -StaticProfileUri 'res://Samples/AuthoredBattle/Static/Exploration.tres' `
            -T3RootDir $t3BuildDir -TempRoot $tempRoot -GodotExe $godot
        Invoke-PrepareT3StoryPackage -RepositoryRoot $RepositoryRoot -SampleName 'LostReload' -TargetSubdir 'LostReloadStory' `
            -StaticResourcesUri 'res://Assets/T3Real/AuthoredStatic' -StaticProfileUri 'res://Samples/AuthoredBattle/Static/Exploration.tres' `
            -T3RootDir $t3BuildDir -TempRoot $tempRoot -GodotExe $godot
        Invoke-PrepareT3StoryPackage -RepositoryRoot $RepositoryRoot -SampleName 'Dynamic' -TargetSubdir 'DynamicStory' `
            -StaticResourcesUri 'res://Assets/T3Real/DynamicStatic' -StaticProfileUri 'res://Assets/T3Real/DynamicStatic/Exploration.tres' `
            -T3RootDir $t3BuildDir -TempRoot $tempRoot -GodotExe $godot

        # Publish into shared cache atomically via sibling temp dir
        $assetsParent = Split-Path $info.AssetsDir -Parent
        [void][IO.Directory]::CreateDirectory($assetsParent)
        $stageCache = Join-Path $assetsParent ('.staging-' + [guid]::NewGuid().ToString('N'))
        Copy-DirectoryRecursive -SourceDir $t3BuildDir -DestinationDir $stageCache
        $cacheManifest = [ordered]@{
            formatVersion  = 1
            rawHash        = $info.RawHash
            prepareVersion = $info.PrepareVersion
            cacheKey       = $info.CacheKey
            createdAtUtc   = [DateTime]::UtcNow.ToString('O')
        }
        [IO.File]::WriteAllText(
            (Join-Path $stageCache 'cache-manifest.json'),
            ($cacheManifest | ConvertTo-Json -Depth 5) + "`n",
            [Text.UTF8Encoding]::new($false)
        )
        if (Test-Path -LiteralPath $info.AssetsDir) {
            Remove-Item -LiteralPath $stageCache -Recurse -Force
        } else {
            [IO.Directory]::Move($stageCache, $info.AssetsDir)
        }
        Remove-T3WorktreeLinkOrDir -Path $t3BuildDir
        $linkMode = Set-T3WorktreeLink -WorktreeLinkDir $info.WorktreeLinkDir -CacheAssetsDir $info.AssetsDir
        $info['AssetsHit'] = $true
        $info['LinkMode'] = $linkMode
        $info['PreparedNow'] = $true
        return $info
    } finally {
        if (Test-Path -LiteralPath $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Export-ModuleMember -Function @(
    'Get-T3CacheRoot',
    'Get-T3RawDataDirectory',
    'Get-T3RawDataHash',
    'Get-T3CacheInfo',
    'Initialize-T3Assets'
)
