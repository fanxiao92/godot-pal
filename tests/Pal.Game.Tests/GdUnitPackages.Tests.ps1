# Offline file/bootstrap regression. Requires the accepted four-file bundle; never uses the real network or SDK.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$InputDirectory)
$ErrorActionPreference = 'Stop'
$InputDirectory = (Resolve-Path -LiteralPath $InputDirectory).Path
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$originalWebRequest = Get-Command Invoke-WebRequest
$temp = Join-Path ([IO.Path]::GetTempPath()) ('pal-gdunit-regression-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
function Get-InputIdentity {
    @(Get-ChildItem -LiteralPath $InputDirectory -File -Force | Sort-Object Name | ForEach-Object {
        "$($_.Name)|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)|$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash)"
    }) -join "`n"
}
$before = Get-InputIdentity
try {
    & {
        $lock = Get-Content (Join-Path $repository 'scripts/gdunit-packages.lock.json') -Raw | ConvertFrom-Json
        $acceptedBundle = $InputDirectory
        $results = [Collections.Generic.List[string]]::new()
        function Invoke-WebRequest { throw 'UNEXPECTED_NETWORK: offline regression prohibits real downloads.' }
        function Assert([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
        function Assert-Fails([scriptblock]$Action, [string]$Pattern) {
            $message = $null
            try { & $Action } catch { $message = $_.Exception.Message }
            Assert ($message -and $message -match $Pattern) "Expected failure '$Pattern', got '$message'."
        }
        function New-Fixture {
            $root = Join-Path $temp ([guid]::NewGuid().ToString('N'))
            $scripts = Join-Path $root 'scripts'
            New-Item -ItemType Directory -Path $scripts | Out-Null
            foreach ($file in @('Get-GdUnitPackages.ps1', 'gdunit-packages.lock.json')) {
                [IO.File]::Copy((Join-Path $repository "scripts/$file"), (Join-Path $scripts $file))
            }
            @{ root = $root; bootstrap = Join-Path $scripts 'Get-GdUnitPackages.ps1'; source = Join-Path $root '.packages/gdunit' }
        }
        function Copy-Bundle([string]$Destination) {
            New-Item -ItemType Directory -Path $Destination | Out-Null
            foreach ($asset in $lock.assets) { [IO.File]::Copy((Join-Path $InputDirectory $asset.file), (Join-Path $Destination $asset.file)) }
        }
        function Assert-Ready([string]$Directory) {
            Assert ((@(Get-ChildItem -LiteralPath $Directory -Force)).Count -eq 4) 'Ready source must have exactly four files.'
            foreach ($asset in $lock.assets) {
                $file = Get-Item -LiteralPath (Join-Path $Directory $asset.file)
                Assert ($file.Length -eq $asset.size) 'Ready file has wrong length.'
                Assert ((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -eq $asset.sha256) 'Ready file has wrong hash.'
            }
        }
        function Assert-NoSource($Fixture) {
            Assert (-not (Test-Path -LiteralPath $Fixture.source)) 'Failure exposed a partial NuGet source.'
            $parent = Join-Path $Fixture.root '.packages'
            if (Test-Path -LiteralPath $parent) {
                Assert (@(Get-ChildItem -LiteralPath $parent -Force).Count -eq 0) 'Failure left staging files.'
            }
        }
        function Case([string]$Name, [scriptblock]$Action) {
            & $Action
            $results.Add($Name)
            Write-Host "PASS: $Name"
        }
        Case 'exact pins, local-only mapping and unchanged SDK/TFM/TestSdk/Core reference' {
            [xml]$config = Get-Content (Join-Path $repository 'NuGet.Config') -Raw
            $sources = @($config.configuration.packageSources.add)
            Assert ($null -ne $config.configuration.packageSources.clear -and $sources.Count -eq 2) 'Sources must clear inherited sources and contain only local + official.'
            Assert ($sources[0].key -eq 'gdunit-local' -and $sources[0].value -eq '.packages/gdunit') 'Wrong repository-local source.'
            Assert ($sources[1].key -eq 'nuget.org' -and $sources[1].value -eq 'https://api.nuget.org/v3/index.json') 'Official dependency source changed.'
            $mapping = @($config.configuration.packageSourceMapping.packageSource)
            Assert ($null -ne $config.configuration.packageSourceMapping.clear -and $mapping.Count -eq 2) 'Mapping must clear inherited mappings.'
            Assert ($mapping[0].key -eq 'gdunit-local' -and ($mapping[0].package.pattern -join ',') -ceq 'gdUnit4.api,gdUnit4.test.adapter') 'Only exact GdUnit IDs may use local source.'
            Assert ($mapping[1].key -eq 'nuget.org' -and @($mapping[1].package).Count -eq 1 -and $mapping[1].package.pattern -eq '*') 'All other dependencies must remain official.'
            [xml]$project = Get-Content (Join-Path $repository 'src/Pal.Game/Pal.Game.csproj') -Raw
            Assert ($project.Project.Sdk -eq 'Godot.NET.Sdk/4.7.2' -and $project.Project.PropertyGroup.TargetFramework -eq 'net10.0') 'SDK/TFM changed.'
            Assert ($project.Project.ItemGroup.ProjectReference.Include -eq '../Pal.Core/Pal.Core.csproj') 'Core reference changed.'
            $refs = @($project.Project.ItemGroup.PackageReference)
            Assert ($refs.Count -eq 3 -and ($refs | Where-Object Include -eq 'Microsoft.NET.Test.Sdk').Version -eq '18.0.1') 'TestSdk or dependency set changed.'
            foreach ($asset in @($lock.assets | Where-Object file -Like '*.nupkg')) {
                Assert (($refs | Where-Object Include -eq $asset.id).Version -ceq "[$($asset.version)]") 'Package pin differs from lock.'
            }
            $entry = Get-Content (Join-Path $repository 'scripts/Test-M0.ps1') -Raw
            Assert (([regex]::Matches($entry, "Get-GdUnitPackages.ps1")).Count -eq 1) 'Shared entry must bootstrap exactly once.'
            Assert ($entry.IndexOf('Get-GdUnitPackages.ps1') -lt $entry.IndexOf("Invoke-DotnetChecked @('--info')")) 'Bootstrap must precede SDK/restore.'
            Assert ((Get-Content (Join-Path $repository '.gitignore')) -contains '/.packages/') 'Local package source must be ignored.'
        }
        Case 'locked upstream, recipe, run and fixed future Release identity' {
            Assert ($lock.source.sha -ceq 'a5f20fb8c12864b17c77d433cad14f9bf0094458') 'Wrong source SHA.'
            Assert ($lock.recipe.sha -ceq 'd6d2aea6812debce526aeb3b03b6094edb82a608') 'Wrong recipe SHA.'
            Assert ($lock.run.id -eq '39608085368' -and $lock.run.attempt -eq 1 -and $lock.run.artifactId -eq '12348323804') 'Wrong producer run.'
            Assert ($lock.release.tag -ceq 'pal-gdunit-a5f20fb8-1') 'Wrong prerelease tag.'
            Assert ($lock.release.baseUrl -ceq 'https://github.com/fanxiao92/gdUnit4Net/releases/download/pal-gdunit-a5f20fb8-1/') 'Wrong fixed Release URL.'
            Assert (@($lock.assets).Count -eq 4) 'Exactly four pinned assets required.'
        }
        Case 'explicit accepted input, cached success, byte/mtime preservation and zero downloads' {
            $f = New-Fixture
            $path = & $f.bootstrap -InputDirectory $InputDirectory
            Assert ($path -eq $f.source) 'Bootstrap did not return its source path.'
            Assert-Ready $f.source
            $identity = @(Get-ChildItem $f.source | Sort-Object Name | ForEach-Object { "$($_.Name)|$($_.LastWriteTimeUtc.Ticks)" }) -join "`n"
            $cached = & $f.bootstrap
            Assert ($cached -eq $path) 'Cached path changed.'
            $again = @(Get-ChildItem $f.source | Sort-Object Name | ForEach-Object { "$($_.Name)|$($_.LastWriteTimeUtc.Ticks)" }) -join "`n"
            Assert ($identity -ceq $again) 'Cached success rewrote files.'
        }
        Case 'same-size input corruption rejects SHA without creating source' {
            $f = New-Fixture; $inputCopy = Join-Path $f.root 'input'; Copy-Bundle $inputCopy
            $file = Join-Path $inputCopy $lock.assets[0].file
            $bytes = [IO.File]::ReadAllBytes($file); $bytes[0] = $bytes[0] -bxor 1; [IO.File]::WriteAllBytes($file, $bytes)
            Assert-Fails { & $f.bootstrap -InputDirectory $inputCopy } 'Pinned SHA256 mismatch'
            Assert-NoSource $f
        }
        Case 'truncated input rejects length without creating source' {
            $f = New-Fixture; $inputCopy = Join-Path $f.root 'input'; Copy-Bundle $inputCopy
            [IO.File]::WriteAllBytes((Join-Path $inputCopy $lock.assets[1].file), [byte[]]@(1))
            Assert-Fails { & $f.bootstrap -InputDirectory $inputCopy } 'Pinned length mismatch'
            Assert-NoSource $f
        }
        Case 'incomplete input rejects before staging' {
            $f = New-Fixture; $inputCopy = Join-Path $f.root 'input'; Copy-Bundle $inputCopy
            Remove-Item -LiteralPath (Join-Path $inputCopy 'provenance.json')
            Assert-Fails { & $f.bootstrap -InputDirectory $inputCopy } 'Incomplete or unexpected bundle'
            Assert-NoSource $f
        }
        Case 'stale extra input rejects even with a valid cached source' {
            $f = New-Fixture; $null = & $f.bootstrap -InputDirectory $InputDirectory
            $inputCopy = Join-Path $f.root 'input'; Copy-Bundle $inputCopy
            [IO.File]::WriteAllText((Join-Path $inputCopy 'stale.nupkg'), 'stale')
            Assert-Fails { & $f.bootstrap -InputDirectory $inputCopy } 'Incomplete or unexpected bundle'
            Assert-Ready $f.source
        }
        Case 'wrong cached bytes reject without overwrite or fallback' {
            $f = New-Fixture; Copy-Bundle $f.source
            $file = Join-Path $f.source $lock.assets[0].file
            $bytes = [IO.File]::ReadAllBytes($file); $bytes[0] = $bytes[0] -bxor 1; [IO.File]::WriteAllBytes($file, $bytes)
            $hash = (Get-FileHash $file).Hash; $ticks = (Get-Item $file).LastWriteTimeUtc.Ticks
            Assert-Fails { & $f.bootstrap -InputDirectory $InputDirectory } 'Pinned SHA256 mismatch'
            Assert ((Get-FileHash $file).Hash -eq $hash -and (Get-Item $file).LastWriteTimeUtc.Ticks -eq $ticks) 'Corrupt cache was overwritten.'
        }
        Case 'partial and unknown existing sources refuse repair and preserve unknown files' {
            $f = New-Fixture; New-Item -ItemType Directory -Path $f.source | Out-Null
            $unknown = Join-Path $f.source 'unknown.txt'; [IO.File]::WriteAllText($unknown, 'keep')
            $ticks = (Get-Item $unknown).LastWriteTimeUtc.Ticks
            Assert-Fails { & $f.bootstrap -InputDirectory $InputDirectory } 'Incomplete or unexpected bundle'
            Assert ([IO.File]::ReadAllText($unknown) -ceq 'keep') 'Unknown cache file changed.'
            Assert ((Get-Item $unknown).LastWriteTimeUtc.Ticks -eq $ticks) 'Unknown cache mtime changed.'
            Assert (@(Get-ChildItem $f.source).Count -eq 1) 'Partial cache was filled in.'
        }
        Case 'mock fixed-URL downloads and cached reuse' {
            & {
                $f = New-Fixture; $requests = [Collections.Generic.List[string]]::new()
                function Invoke-WebRequest {
                    [CmdletBinding()] param([string]$Uri, [string]$OutFile)
                    $asset = @($lock.assets | Where-Object { ($lock.release.baseUrl + $_.file) -ceq $Uri })
                    Assert ($asset.Count -eq 1) "Unexpected download URL: $Uri"
                    $requests.Add($Uri)
                    [IO.File]::Copy((Join-Path $acceptedBundle $asset[0].file), $OutFile)
                }
                $null = & $f.bootstrap
                Assert-Ready $f.source
                Assert ($requests.Count -eq 4) 'Expected four fixed-URL downloads.'
                $null = & $f.bootstrap
                Assert ($requests.Count -eq 4) 'Cached bundle unexpectedly downloaded again.'
            }
        }
        Case 'download failure propagates and never exposes partial source' {
            & {
                $f = New-Fixture; $requests = [Collections.Generic.List[string]]::new()
                function Invoke-WebRequest {
                    [CmdletBinding()] param([string]$Uri, [string]$OutFile)
                    $requests.Add($Uri)
                    if ($requests.Count -eq 2) { throw 'FAKE_DOWNLOAD_FAILURE' }
                    [IO.File]::Copy((Join-Path $acceptedBundle $lock.assets[0].file), $OutFile)
                }
                Assert-Fails { & $f.bootstrap } 'FAKE_DOWNLOAD_FAILURE'
                Assert ($requests.Count -eq 2) 'Download failure did not stop immediately.'
                Assert-NoSource $f
            }
        }
        Case 'downloaded corrupt bytes reject against checked-in pins, not downloaded checksums' {
            & {
                $f = New-Fixture
                function Invoke-WebRequest {
                    [CmdletBinding()] param([string]$Uri, [string]$OutFile)
                    $bytes = [IO.File]::ReadAllBytes((Join-Path $acceptedBundle $lock.assets[0].file))
                    $bytes[0] = $bytes[0] -bxor 1; [IO.File]::WriteAllBytes($OutFile, $bytes)
                }
                Assert-Fails { & $f.bootstrap } 'Pinned SHA256 mismatch'
                Assert-NoSource $f
            }
        }
        Case 'last asset length failure leaves no ready source after three valid downloads' {
            & {
                $f = New-Fixture
                function Invoke-WebRequest {
                    [CmdletBinding()] param([string]$Uri, [string]$OutFile)
                    $asset = @($lock.assets | Where-Object { ($lock.release.baseUrl + $_.file) -ceq $Uri })[0]
                    if ($asset.file -eq 'SHA256SUMS') { [IO.File]::WriteAllText($OutFile, 'truncated') }
                    else { [IO.File]::Copy((Join-Path $acceptedBundle $asset.file), $OutFile) }
                }
                Assert-Fails { & $f.bootstrap } 'Pinned length mismatch'
                Assert-NoSource $f
            }
        }
        Case 'destination appearing during staging is never overwritten' {
            & {
                $f = New-Fixture
                function Invoke-WebRequest {
                    [CmdletBinding()] param([string]$Uri, [string]$OutFile)
                    $asset = @($lock.assets | Where-Object { ($lock.release.baseUrl + $_.file) -ceq $Uri })[0]
                    [IO.File]::Copy((Join-Path $acceptedBundle $asset.file), $OutFile)
                    if ($asset.file -eq 'SHA256SUMS') {
                        New-Item -ItemType Directory -Path $f.source | Out-Null
                        [IO.File]::WriteAllText((Join-Path $f.source 'concurrent.txt'), 'keep')
                    }
                }
                Assert-Fails { & $f.bootstrap } 'already exists'
                Assert (@(Get-ChildItem $f.source).Count -eq 1) 'Concurrent destination was filled in.'
                Assert ([IO.File]::ReadAllText((Join-Path $f.source 'concurrent.txt')) -ceq 'keep') 'Concurrent file changed.'
                Assert (@(Get-ChildItem (Join-Path $f.root '.packages') -Force).Count -eq 1) 'Owned staging was not cleaned.'
            }
        }
        Write-Host "PASS: $($results.Count) offline GdUnit package regression cases; real network/SDK/CIM/package execution = 0."
    }
} finally {
    $after = Get-InputIdentity
    Remove-Item -LiteralPath $temp -Recurse -Force
    if ($before -cne $after) { throw 'Accepted input bytes, lengths or mtimes changed.' }
    $restored = Get-Command Invoke-WebRequest
    if ($restored.CommandType -ne $originalWebRequest.CommandType -or $restored.Source -ne $originalWebRequest.Source -or
        $restored.Definition -ne $originalWebRequest.Definition) { throw 'Download mock escaped its child scope.' }
}
