[CmdletBinding()]
param([string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'; [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); [Console]::InputEncoding=[Text.UTF8Encoding]::new($false)
$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path; $helper=Join-Path $root 'scripts/Test-CiCandidate.ps1'
$evidence=if($EvidenceDirectory){[IO.Path]::GetFullPath($EvidenceDirectory)}else{Join-Path ([IO.Path]::GetTempPath()) ('ci-candidate-'+[guid]::NewGuid().ToString('N'))}; if(Test-Path $evidence){throw 'EvidenceDirectory must be new.'}; $null=New-Item -ItemType Directory $evidence
$git=(Get-Command git -CommandType Application|Select-Object -First 1).Source; $fixture=Join-Path $evidence 'git-fixture'; $null=New-Item -ItemType Directory $fixture
& $git init --quiet --initial-branch=main $fixture; if($LASTEXITCODE){throw 'git init failed.'}
function G([string[]]$A){$o=@(& $git -C $fixture @A)-join "`n";if($LASTEXITCODE){throw "fixture git failed: $A"};$o.Trim()}
G @('config','user.email','ci@example.invalid');G @('config','user.name','CI fixture');Set-Content (Join-Path $fixture 'base.txt') 'base' -Encoding utf8;G @('add','.');G @('commit','-m','base','--quiet');$base0=G @('rev-parse','HEAD')
G @('checkout','-q','-b','fixture-head');Set-Content (Join-Path $fixture 'head.txt') 'head' -Encoding utf8;G @('add','.');G @('commit','-m','head','--quiet');$head=G @('rev-parse','HEAD')
G @('checkout','-q','main');Set-Content (Join-Path $fixture 'base-change.txt') 'base-change' -Encoding utf8;G @('add','.');G @('commit','-m','base-change','--quiet');$base=G @('rev-parse','HEAD')
$tree=G @('write-tree');$env:GIT_AUTHOR_NAME='CI fixture';$env:GIT_AUTHOR_EMAIL='ci@example.invalid';$env:GIT_COMMITTER_NAME='CI fixture';$env:GIT_COMMITTER_EMAIL='ci@example.invalid';$candidate=(& $git -C $fixture commit-tree $tree -p $base -p $head -m merge).Trim();if($LASTEXITCODE){throw 'commit-tree failed.'};G @('checkout','-q','--detach',$candidate);$tree=G @('show','-s','--format=%T','HEAD^{commit}')
function Run-Case([string]$Name,[int]$Expected,[hashtable]$Changes=@{}){
 $dir=Join-Path $evidence $Name;$null=New-Item -ItemType Directory $dir
 $event=[ordered]@{eventName='pull_request';action='ready_for_review';mode='ready-full'}
 $meta=[ordered]@{githubSha=$candidate;runId='run-中文';runAttempt=1;workflowRef='refs/workflows/ci.yml';prHeadSha=$head;prBaseSha=$base;draft=$false}
 $snap=[ordered]@{eventName=$event.eventName;action=$event.action;mode=$event.mode;runId=$meta.runId;runAttempt=$meta.runAttempt;workflowRef=$meta.workflowRef;draft=$meta.draft;headSha=$head;baseSha=$base;candidateSha=$candidate;actualCheckout=$candidate;tree=$tree;parents=@($base,$head)}
 foreach($key in $Changes.Keys){if($Changes[$key] -is [hashtable]){$target=if($Changes[$key].Target -eq 'meta'){$meta}else{$snap};$target[$key]=$Changes[$key].Value}else{$snap[$key]=$Changes[$key]}}
 if($Name -eq 'push-main'){$event.eventName='push';$event.action='main';$event.mode='push-main';$meta.draft=$false;$snap.eventName='push';$snap.action='main';$snap.mode='push-main'}
 if($Name -eq 'manual-full'){$event.eventName='workflow_dispatch';$event.action='manual';$event.mode='manual-full';$meta.draft=$false;$snap.eventName='workflow_dispatch';$snap.action='manual';$snap.mode='manual-full'}
 $ep=Join-Path $dir 'event.json';$mp=Join-Path $dir 'metadata.json';$sp=Join-Path $dir 'snapshot.json';$event|ConvertTo-Json|Set-Content $ep -Encoding utf8;$meta|ConvertTo-Json|Set-Content $mp -Encoding utf8;$snap|ConvertTo-Json -Depth 8|Set-Content $sp -Encoding utf8
 $out=& pwsh -NoProfile -File $helper -EventPath $ep -MetadataPath $mp -RepositoryRoot $fixture -ServerSnapshotPath $sp -EvidenceDirectory $dir 2>&1;$actual=$LASTEXITCODE;if($actual-ne$Expected){throw "$Name expected $Expected got ${actual}: $($out -join ' ')"};$identity=Join-Path $dir 'candidate-identity.json';if($Expected -eq 0 -and -not(Test-Path $identity)){throw "$Name missing identity"};if($Expected -ne 0 -and (Test-Path $identity)){throw "$Name wrote identity after failure"}
}
Run-Case 'matching-merge' 0
Run-Case 'swapped-metadata-and-snapshot-parents' 2 @{prBaseSha=@{Target='meta';Value=$head};prHeadSha=@{Target='meta';Value=$base};parents=@($head,$base)}
Run-Case 'wrong-checkout' 2 @{githubSha=@{Target='meta';Value=$head};actualCheckout=$head;candidateSha=$head}
Run-Case 'wrong-head' 2 @{prHeadSha=@{Target='meta';Value=('0'*40)}}
Run-Case 'wrong-base' 2 @{prBaseSha=@{Target='meta';Value=('0'*40)}}
Run-Case 'stale-candidate' 3 @{candidateSha=('1'*40)}
Run-Case 'stale-run-id' 3 @{runId='old-run'}
Run-Case 'stale-attempt' 3 @{runAttempt=2}
Run-Case 'stale-workflow' 3 @{workflowRef='old-workflow'}
Run-Case 'stale-mode' 3 @{mode='draft-quick'}
Run-Case 'stale-draft' 3 @{draft=$true}
Run-Case 'draft-borrow-ready' 3 @{mode='draft-quick';draft=$true}
Run-Case 'missing-snapshot-field' 3 @{runId=$null}
Run-Case 'bad-snapshot-type' 3 @{runAttempt='1'}
Run-Case 'push-main' 0 @{eventName='push';action='main';mode='push-main';draft=$false}
Run-Case 'manual-full' 0 @{eventName='workflow_dispatch';action='manual';mode='manual-full';draft=$false}
Run-Case 'missing-event' 3 @{eventName='';action='manual';mode='manual-full';draft=$false}
Run-Case 'bad-attempt' 3 @{runAttempt=0}
Run-Case 'boolean-attempt' 3 @{runAttempt=$true}
$invalid=Join-Path $evidence 'invalid.json';[IO.File]::WriteAllBytes($invalid,[byte[]](0x7b,0x22,0x78,0x22,0x3a,0xff,0x7d));$invalidDir=Join-Path $evidence 'invalid-utf8';$null=New-Item -ItemType Directory $invalidDir;$metaPath=Join-Path $invalidDir 'metadata.json';Copy-Item (Join-Path $evidence 'matching-merge/metadata.json') $metaPath;Copy-Item (Join-Path $evidence 'matching-merge/event.json') (Join-Path $invalidDir 'event.json');Copy-Item (Join-Path $evidence 'matching-merge/snapshot.json') (Join-Path $invalidDir 'snapshot.json');[IO.File]::WriteAllBytes($metaPath,[byte[]](0x7b,0x22,0x78,0x22,0x3a,0xff,0x7d));$o=& pwsh -NoProfile -File $helper -EventPath (Join-Path $invalidDir 'event.json') -MetadataPath $metaPath -RepositoryRoot $fixture -ServerSnapshotPath (Join-Path $invalidDir 'snapshot.json') -EvidenceDirectory $invalidDir 2>&1;if($LASTEXITCODE -ne 2 -or ($o -join ' ') -notmatch 'strict UTF-8'){throw 'invalid UTF-8 was not rejected'}
$existing=Join-Path $evidence 'existing';$null=New-Item -ItemType Directory $existing;$existingIdentity=Join-Path $existing 'candidate-identity.json';[IO.File]::WriteAllText($existingIdentity,'keep',[Text.UTF8Encoding]::new($false));$before=(Get-Item $existingIdentity).LastWriteTimeUtc
# Reuse matching inputs in a separate case directory is the functional overwrite refusal check.
$dir=Join-Path $evidence 'overwrite';$null=New-Item -ItemType Directory $dir;$ep=Join-Path $dir 'event.json';$mp=Join-Path $dir 'metadata.json';$sp=Join-Path $dir 'snapshot.json';([ordered]@{eventName='pull_request';action='ready_for_review';mode='ready-full'}|ConvertTo-Json)|Set-Content $ep -Encoding utf8;([ordered]@{githubSha=$candidate;runId='r';runAttempt=1;workflowRef='w';prHeadSha=$head;prBaseSha=$base;draft=$false}|ConvertTo-Json)|Set-Content $mp -Encoding utf8;([ordered]@{eventName='pull_request';action='ready_for_review';mode='ready-full';runId='r';runAttempt=1;workflowRef='w';draft=$false;headSha=$head;baseSha=$base;candidateSha=$candidate;actualCheckout=$candidate;tree=$tree;parents=@($base,$head)}|ConvertTo-Json -Depth 8)|Set-Content $sp -Encoding utf8;[IO.File]::WriteAllText((Join-Path $dir 'candidate-identity.json'),'keep',[Text.UTF8Encoding]::new($false));$before=(Get-Item (Join-Path $dir 'candidate-identity.json')).LastWriteTimeUtc;$beforeHash=(Get-FileHash (Join-Path $dir 'candidate-identity.json')).Hash;$o=& pwsh -NoProfile -File $helper -EventPath $ep -MetadataPath $mp -RepositoryRoot $fixture -ServerSnapshotPath $sp -EvidenceDirectory $dir 2>&1;$after=(Get-Item (Join-Path $dir 'candidate-identity.json')).LastWriteTimeUtc;if($LASTEXITCODE -ne 4 -or (Get-FileHash (Join-Path $dir 'candidate-identity.json')).Hash -cne $beforeHash -or $after -ne $before){throw 'overwrite refusal failed'}
$badMeta=Join-Path $evidence 'metadata-root-array';$null=New-Item -ItemType Directory $badMeta;Copy-Item $ep (Join-Path $badMeta 'event.json');Copy-Item $sp (Join-Path $badMeta 'snapshot.json');Set-Content (Join-Path $badMeta 'metadata.json') '[]';$o=& pwsh -NoProfile -File $helper -EventPath (Join-Path $badMeta 'event.json') -MetadataPath (Join-Path $badMeta 'metadata.json') -RepositoryRoot $fixture -ServerSnapshotPath (Join-Path $badMeta 'snapshot.json') -EvidenceDirectory $badMeta 2>&1;if($LASTEXITCODE -ne 2){throw 'metadata array root was not rejected'}
$badSnapshot=Join-Path $evidence 'snapshot-root-array';$null=New-Item -ItemType Directory $badSnapshot;Copy-Item $ep (Join-Path $badSnapshot 'event.json');Copy-Item (Join-Path $evidence 'matching-merge/metadata.json') (Join-Path $badSnapshot 'metadata.json');Set-Content (Join-Path $badSnapshot 'snapshot.json') '[]';$o=& pwsh -NoProfile -File $helper -EventPath (Join-Path $badSnapshot 'event.json') -MetadataPath (Join-Path $badSnapshot 'metadata.json') -RepositoryRoot $fixture -ServerSnapshotPath (Join-Path $badSnapshot 'snapshot.json') -EvidenceDirectory $badSnapshot 2>&1;if($LASTEXITCODE -ne 2){throw 'snapshot array root was not rejected'}
$missingRoot=Join-Path $evidence 'missing-root';$o=& pwsh -NoProfile -File $helper -EventPath $ep -MetadataPath $mp -RepositoryRoot $missingRoot -ServerSnapshotPath $sp -EvidenceDirectory $dir 2>&1;if($LASTEXITCODE -ne 2){throw 'missing repository root not rejected'}
$singletonFailures=@()
foreach($field in @('event','metadata','snapshot')){
 $dir=Join-Path $evidence ($field+'-singleton-array');$null=New-Item -ItemType Directory -Path $dir
 foreach($file in @('event','metadata','snapshot')){[IO.File]::Copy((Join-Path $evidence "matching-merge/$file.json"),(Join-Path $dir "$file.json"))}
 $inputPath=Join-Path $dir "$field.json";$original=[IO.File]::ReadAllText($inputPath);[IO.File]::WriteAllText($inputPath,('['+$original.Trim()+']'),[Text.UTF8Encoding]::new($false))
 & pwsh -NoProfile -File $helper -EventPath (Join-Path $dir 'event.json') -MetadataPath (Join-Path $dir 'metadata.json') -RepositoryRoot $fixture -ServerSnapshotPath (Join-Path $dir 'snapshot.json') -EvidenceDirectory $dir 1> (Join-Path $dir 'raw.stdout') 2> (Join-Path $dir 'raw.stderr')
 $exit=$LASTEXITCODE;@{field=$field;exit=$exit;expected=2}|ConvertTo-Json|Set-Content (Join-Path $dir 'exit.json') -Encoding utf8
 if($exit -ne 2 -or (Test-Path (Join-Path $dir 'candidate-identity.json'))){$singletonFailures+=$field}
}
if($singletonFailures.Count){throw "Singleton root arrays accepted: $($singletonFailures -join ', ')"}
Write-Output 'PASS CiCandidate repair regression: baseline identity cases plus 3 singleton-root controls; ordered parents, typed snapshot freshness, strict UTF-8, and atomic no-overwrite verified.'
if(-not $EvidenceDirectory){Remove-Item $evidence -Recurse -Force}
