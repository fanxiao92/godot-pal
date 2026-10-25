[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [Parameter(Mandatory)][string]$RunId,
    [Parameter(Mandatory)][string]$CodeIdentity,
    [Parameter(Mandatory)][int]$TaskExitCode,
    [Parameter(Mandatory)][double]$TaskWallClockMs,
    [string]$OutputPath
)
$ErrorActionPreference='Stop'; [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); $utf8=[Text.UTF8Encoding]::new($false,$true)
function Read-Strict([string]$Path){[IO.File]::ReadAllText($Path,$utf8)|ConvertFrom-Json -AsHashtable -Depth 60 -NoEnumerate}
$root=[IO.Path]::GetFullPath($EvidenceDirectory);$commandsPath=Join-Path $root 'commands.json'
if(-not(Test-Path -LiteralPath $commandsPath -PathType Leaf)){throw 'Current-run commands.json is missing.'}
function Resolve-OwnedPath([string]$Value,[string]$Label){
 if([string]::IsNullOrWhiteSpace($Value)){throw "$Label is empty."}
 $path=if([IO.Path]::IsPathRooted($Value)){[IO.Path]::GetFullPath($Value)}else{[IO.Path]::GetFullPath((Join-Path $root $Value))}
 $relative=[IO.Path]::GetRelativePath($root,$path)
 if($relative -eq '..' -or $relative.StartsWith('..'+[IO.Path]::DirectorySeparatorChar) -or [IO.Path]::IsPathRooted($relative)){throw "$Label escapes the owned evidence root: $Value"}
 return $path
}
$raw=@(Read-Strict $commandsPath);if($raw.Count -eq 1 -and $raw[0] -is [array]){$raw=@($raw[0])};if(-not$raw.Count){throw 'Current-run command index is empty.'}
$selections=[Collections.Generic.List[object]]::new();$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach($record in $raw){
 if($record.log -isnot [string] -or -not$seen.Add($record.log)){throw "Missing or duplicate command log identity: $($record.log)"}
 $args=@($record.arguments);if($record.command -isnot [string] -or $args.Count -eq 0 -or @($args|?{$_ -isnot[string]}).Count){throw "Invalid command record: $($record.log)"}
 $isTest=$record.command -ceq 'dotnet' -and $args.Count -gt 0 -and $args[0] -ceq 'test';$isList=$args -contains '--list-tests';$category=if($isTest -and -not$isList){'test'}elseif($isList){'discovery'}else{'other'}
 $trx=@();$ri=[Array]::IndexOf($args,'--results-directory');$dir=$null;if($ri-ge0 -and $ri+1-lt$args.Count){$dir=Resolve-OwnedPath $args[$ri+1] 'Results directory';if(-not(Test-Path -LiteralPath $dir -PathType Container)){throw "Results directory missing for $($record.log)."}}
 if($category-ceq'test'){
  $li=[Array]::IndexOf($args,'--logger');if($li-lt0){$li=[Array]::IndexOf($args,'-l')};$file=$null
  if($li-ge0 -and $li+1-lt$args.Count -and $args[$li+1]-match '(?i)^trx;LogFileName=([^;]+)$'){$file=$Matches[1]}
  if($file){$path=Resolve-OwnedPath ([IO.Path]::Combine($dir,$file)) 'TRX result';if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw "Expected current TRX missing: $path"};$trx=@([ordered]@{path=$path;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash})}
  elseif($dir){$files=@(Get-ChildItem -LiteralPath $dir -Filter '*.trx' -File);if($files.Count-ne1){throw "Bare TRX logger requires exactly one current result in $dir."};$trx=@([ordered]@{path=$files[0].FullName;sha256=(Get-FileHash -LiteralPath $files[0].FullName -Algorithm SHA256).Hash})}
  else{throw "Test command has no results directory: $($record.log)."}
 }
 $round=$null;$batch=$null
 if($isTest){
  $project=($args|Where-Object {$_ -match '\.csproj$'}|Select-Object -First 1)
  if($project -match 'Pal.Core.Tests'){ $round='solution';$batch='core' }
  elseif($project -match 'Pal.Extract.UnitTests'){ $round='solution';$batch='cli-unit' }
  elseif($project -match 'Pal.Extract.IntegrationTests'){ $round='solution';$batch='cli-integration' }
  elseif($project -match 'Pal.Prepare.Tests'){ $round='solution';$batch='prepare' }
  elseif($project -match 'Pal.Game.csproj'){
   $leaf=if($dir){[IO.DirectoryInfo]$dir|Select-Object -ExpandProperty Name}else{''}
   $round=if($leaf -ceq 'game-repeat'){'game-repeat'}elseif($leaf -ceq 'game'){'game'}else{'solution'}
   if(@($args|Where-Object {$_ -match 'IntentionalAssertionMismatchPropagates'}).Count){$round='controls';$batch='IntentionalAssertionMismatch'}
   elseif(@($args|Where-Object {$_ -match 'IntentionalTimeoutPropagates'}).Count){$round='controls';$batch='IntentionalTimeout'}
   elseif(@($args|Where-Object {$_ -match 'NativeReadyExceptionIsNotSwallowed'}).Count){$round='controls';$batch='NativeReadyException'}
   elseif($file -match 'game-process'){$batch='process'}elseif($file -match 'game-scene-dialogue'){$batch='scene-dialogue'}elseif($file -match 'game-native-map'){$batch='native-map'}elseif($isList){$round='solution';$batch='discovery'}
  }
 }
 $selections.Add([ordered]@{log=$record.log;category=$category;scope='M0';round=$round;batch=$batch;trx=$trx})
}
$completion=if(@($raw|?{$_.phase-ne'completed' -or $_.exitKnown -ne$true -or $_.rootExited -ne$true -or $_.stdoutComplete -ne$true -or $_.stderrComplete -ne$true -or $_.outputComplete -ne$true -or $_.cleanupComplete -ne$true}).Count){'incomplete'}else{'completed'}
$out=if($OutputPath){[IO.Path]::GetFullPath($OutputPath)}else{Join-Path $root 'cost-input.json'}
$record=[ordered]@{schemaVersion=1;runId=$RunId;evidenceKind='measured';commandsPath=$commandsPath;selections=$selections;context=[ordered]@{codeIdentity=$CodeIdentity;machine=$null;toolchain=$null;scope='M0';cache=$null;build=$null};task=[ordered]@{completion=$completion;exitCode=$TaskExitCode;wallClockMs=$TaskWallClockMs}}
if(Test-Path $out){throw "Refusing to overwrite cost input: $out"};[IO.File]::WriteAllText($out,($record|ConvertTo-Json -Depth 30),[Text.UTF8Encoding]::new($false));Write-Output $out
