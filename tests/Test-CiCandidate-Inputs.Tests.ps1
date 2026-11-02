[CmdletBinding()]
param([string]$EvidenceDirectory)
$ErrorActionPreference='Stop';$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path;$producer=Join-Path $root 'scripts/New-CiCandidateInputs.ps1';$validator=Join-Path $root 'scripts/Test-CiCandidate.ps1'
$evidence=if($EvidenceDirectory){[IO.Path]::GetFullPath($EvidenceDirectory)}else{Join-Path ([IO.Path]::GetTempPath()) ('ci-inputs-'+[guid]::NewGuid().ToString('N'))};if(Test-Path $evidence){throw 'EvidenceDirectory must be new.'};$null=New-Item -ItemType Directory $evidence
$git=(Get-Command git -CommandType Application|Select-Object -First 1).Source;$fixture=Join-Path $evidence 'fixture';$null=New-Item -ItemType Directory $fixture
function G([string[]]$a){$o=@(& $git -C $fixture @a)-join "`n";if($LASTEXITCODE){throw "git failed: $a"};$o.Trim()}
G @('init','--quiet','--initial-branch=main');G @('config','user.email','ci@example.invalid');G @('config','user.name','CI fixture');Set-Content (Join-Path $fixture 'base.txt') 'base' -Encoding utf8;G @('add','.');G @('commit','--quiet','-m','base');$base0=G @('rev-parse','HEAD');G @('checkout','--quiet','-b','pr-topic');Set-Content (Join-Path $fixture 'head.txt') 'head' -Encoding utf8;G @('add','.');G @('commit','--quiet','-m','head');$head=G @('rev-parse','HEAD');G @('checkout','--quiet','main');Set-Content (Join-Path $fixture 'base-change.txt') 'base-change' -Encoding utf8;G @('add','.');G @('commit','--quiet','-m','base-change');$base=G @('rev-parse','HEAD');$tree=G @('write-tree');$env:GIT_AUTHOR_NAME='CI';$env:GIT_AUTHOR_EMAIL='ci@example.invalid';$env:GIT_COMMITTER_NAME='CI';$env:GIT_COMMITTER_EMAIL='ci@example.invalid';$candidate=(& $git -C $fixture commit-tree $tree -p $base -p $head -m merge).Trim();G @('checkout','--quiet','--detach',$candidate)
$global:FixtureHead=$head;$global:FixtureBase=$base;$global:FixtureCandidate=$candidate;$global:ApiHead=$head;$global:ApiBase=$base;$global:ApiCandidate=$candidate;$global:ApiDraft=$false;$env:GITHUB_REPOSITORY='fanxiao92/godot-pal';$env:CI_TOKEN='fixture-token';$env:CI_API_CALLS=0;$env:CI_EXPECTED_URL='https://api.example/repos/fanxiao92/godot-pal/pulls/42'
function Invoke-RestMethod { param([string]$Method,[string]$Uri,[hashtable]$Headers);$null=($env:CI_API_CALLS=[int]$env:CI_API_CALLS+1);if($Method -cne 'Get' -or $Uri -cne $env:CI_EXPECTED_URL -or $Headers.Authorization -cne 'Bearer fixture-token' -or $Headers.Accept -cne 'application/vnd.github+json' -or $Headers.'X-GitHub-Api-Version' -cne '2022-11-28'){throw 'fixture API call contract mismatch'};return [pscustomobject]@{head=[pscustomobject]@{sha=$global:ApiHead};base=[pscustomobject]@{sha=$global:ApiBase};merge_commit_sha=$global:ApiCandidate;draft=$global:ApiDraft}}
function Run([string]$name,[int]$expected,[string]$eventName,[string]$mode,[object]$event,[bool]$useApi=$false){$dir=Join-Path $evidence $name;$null=New-Item -ItemType Directory $dir;$ep=Join-Path $dir 'event.json';$event|ConvertTo-Json -Depth 20|Set-Content $ep -Encoding utf8;$env:CI_API_CALLS=0;$global:ApiDraft=if($mode -eq 'draft-quick' -or ($mode -eq 'pr-unit' -and $event.pull_request.draft)){$true}else{$false};if(-not $useApi){$old=$env:CI_TOKEN;$env:CI_TOKEN=$null};try{$out=& $producer -OutputDirectory (Join-Path $dir 'inputs') -RepositoryRoot $fixture -EventName $eventName -Action 'caller-action' -Mode $mode -GithubSha $candidate -RunId 'run-中文' -RunAttempt 1 -WorkflowRef 'refs/workflows/ci.yml' -PrNumber '42' -ApiUrl 'https://api.example' -EventJsonPath $ep 2>&1;$code=if($LASTEXITCODE -eq $null){0}else{$LASTEXITCODE}}catch{$code=1;$out=@($_.Exception.Message)}finally{if(-not $useApi){$env:CI_TOKEN=$old}};if($code -ne $expected){throw "${name} expected ${expected}, got ${code}: $($out -join ' ')"};if($expected -eq 0){$input=Join-Path $dir 'inputs';$vout=& $validator -EventPath (Join-Path $input 'event.json') -MetadataPath (Join-Path $input 'metadata.json') -RepositoryRoot $fixture -ServerSnapshotPath (Join-Path $input 'snapshot.json') -EvidenceDirectory (Join-Path $dir 'proof') 2>&1;if($LASTEXITCODE -ne 0){throw "${name} validator failed: $($vout -join ' ')"};if($env:CI_API_CALLS -ne 1 -and $useApi){throw "${name} expected one API call"}}}
$prDraft=[ordered]@{action='synchronize';number=42;pull_request=[ordered]@{number=42;draft=$true}}
Run 'draft-pr-producer-validator' 0 'pull_request' 'draft-quick' $prDraft $true
$prReady=[ordered]@{action='ready_for_review';number=42;pull_request=[ordered]@{number=42;draft=$false}}
Run 'ready-pr-producer-validator' 0 'pull_request' 'ready-full' $prReady $true
Run 'edited-pr-producer-validator' 0 'pull_request' 'ready-full' ([ordered]@{action='edited';number=42;pull_request=[ordered]@{number=42;draft=$false}}) $true
Run 'push-main-producer-validator' 0 'push' 'push-main' ([ordered]@{ref='refs/heads/main'})
Run 'manual-producer-validator' 0 'workflow_dispatch' 'manual-full' ([ordered]@{inputs=[ordered]@{}})
Run 'manual-null-inputs-producer-validator' 0 'workflow_dispatch' 'manual-full' ([ordered]@{inputs=$null})
Run 'unit-draft-producer-validator' 0 'pull_request' 'pr-unit' $prDraft $true
Run 'unit-ready-producer-validator' 0 'pull_request' 'pr-unit' $prReady $true
Run 'unit-push-producer-validator' 0 'push' 'push-unit' ([ordered]@{ref='refs/heads/main'})
Run 'unit-manual-producer-validator' 0 'workflow_dispatch' 'manual-unit' ([ordered]@{inputs=$null})
$linear=$head;G @('checkout','--quiet','--detach',$linear);$candidate=$linear;Run 'push-one-parent-producer-validator' 0 'push' 'push-main' ([ordered]@{ref='refs/heads/main'});Run 'manual-one-parent-producer-validator' 0 'workflow_dispatch' 'manual-full' ([ordered]@{inputs=[ordered]@{}});G @('checkout','--quiet','--detach',$global:FixtureCandidate);$candidate=$global:FixtureCandidate
Run 'push-wrong-ref' 1 'push' 'push-main' ([ordered]@{ref='refs/heads/feature'})
Run 'pr-number-mismatch' 1 'pull_request' 'ready-full' ([ordered]@{action='edited';number=41;pull_request=[ordered]@{number=41;draft=$false}}) $true
Run 'ready-with-draft-event' 1 'pull_request' 'ready-full' ([ordered]@{action='ready_for_review';number=42;pull_request=[ordered]@{number=42;draft=$true}}) $true
foreach($badApi in @(@{Name='api-stale-head';Field='ApiHead';Value=('0'*40)},@{Name='api-stale-base';Field='ApiBase';Value=('1'*40)},@{Name='api-stale-candidate';Field='ApiCandidate';Value=('2'*40)})){ $global:ApiHead=$head;$global:ApiBase=$base;$global:ApiCandidate=$candidate;Set-Variable -Name $badApi.Field -Value $badApi.Value -Scope Global; $dir=Join-Path $evidence $badApi.Name;$null=New-Item -ItemType Directory $dir;$ep=Join-Path $dir 'event.json';$prReady|ConvertTo-Json -Depth 20|Set-Content $ep -Encoding utf8;$out=& $producer -OutputDirectory (Join-Path $dir 'inputs') -RepositoryRoot $fixture -EventName 'pull_request' -Action 'caller' -Mode 'ready-full' -GithubSha $candidate -RunId 'r' -RunAttempt 1 -WorkflowRef 'w' -PrNumber '42' -ApiUrl 'https://api.example' -EventJsonPath $ep 2>&1;if($LASTEXITCODE -ne 0){throw "$($badApi.Name) producer unexpectedly rejected"};$input=Join-Path $dir 'inputs';$vout=& $validator -EventPath (Join-Path $input 'event.json') -MetadataPath (Join-Path $input 'metadata.json') -RepositoryRoot $fixture -ServerSnapshotPath (Join-Path $input 'snapshot.json') -EvidenceDirectory (Join-Path $dir 'proof') 2>&1;if($LASTEXITCODE -notin @(2,3)){throw "$($badApi.Name) expected validator identity failure, got $LASTEXITCODE"} }
$global:ApiHead=$head;$global:ApiBase=$base;$global:ApiCandidate=$candidate
foreach($bad in @(@{Name='event-array';Value='[]'},@{Name='event-singleton-array';Value='[{"ref":"refs/heads/main"}]'},@{Name='event-null';Value='null'},@{Name='event-scalar';Value='7'})){$d=Join-Path $evidence $bad.Name;$null=New-Item -ItemType Directory $d;Set-Content (Join-Path $d 'event.json') $bad.Value -Encoding utf8;try{$o=& $producer -OutputDirectory (Join-Path $d 'out') -RepositoryRoot $fixture -EventName 'push' -Action 'main' -Mode 'push-main' -GithubSha $candidate -RunId 'r' -RunAttempt 1 -WorkflowRef 'w' -EventJsonPath (Join-Path $d 'event.json') 2>&1;$code=$LASTEXITCODE}catch{$code=1};if($code -eq 0){throw "$($bad.Name) accepted"}}
foreach($badInput in @(@{Name='dispatch-missing-inputs';Value='{}'},@{Name='dispatch-bool-inputs';Value='{"inputs":true}'},@{Name='dispatch-array-inputs';Value='{"inputs":[]}'})){ $d=Join-Path $evidence $badInput.Name;$null=New-Item -ItemType Directory $d;Set-Content (Join-Path $d 'event.json') $badInput.Value -Encoding utf8;try{$o=& $producer -OutputDirectory (Join-Path $d 'out') -RepositoryRoot $fixture -EventName 'workflow_dispatch' -Action 'manual' -Mode 'manual-full' -GithubSha $candidate -RunId 'r' -RunAttempt 1 -WorkflowRef 'w' -EventJsonPath (Join-Path $d 'event.json') 2>&1;$code=$LASTEXITCODE}catch{$code=1};if($code -eq 0){throw "$($badInput.Name) accepted"} }
$bad=Join-Path $evidence 'invalid-utf8';$null=New-Item -ItemType Directory $bad;[IO.File]::WriteAllBytes((Join-Path $bad 'event.json'),[byte[]](0x7b,0x22,0x78,0x22,0x3a,0xff,0x7d));try{$o=& $producer -OutputDirectory (Join-Path $bad 'out') -RepositoryRoot $fixture -EventName 'push' -Action 'main' -Mode 'push-main' -GithubSha $candidate -RunId 'r' -RunAttempt 1 -WorkflowRef 'w' -EventJsonPath (Join-Path $bad 'event.json') 2>&1;$code=$LASTEXITCODE}catch{$code=1};if($code -eq 0){throw 'invalid UTF-8 accepted'}
# GitHub may regenerate a synthetic merge commit while keeping its exact tree and ordered parents.
# These are public producer -> validator contract cases, not product Unit tests.
$global:RecomputedSha=(& $git -C $fixture commit-tree $tree -p $base -p $head -m recomputed-merge).Trim()
if($LASTEXITCODE -ne 0 -or $global:RecomputedSha -ceq $candidate){throw 'Distinct equivalent merge fixture was not created.'}
$env:CI_TOKEN='fixture-token'
function Invoke-RestMethod {
 param([string]$Method,[string]$Uri,[hashtable]$Headers)
 $null=($env:CI_API_CALLS=[int]$env:CI_API_CALLS+1)
 if($Method -cne 'Get' -or $Headers.Authorization -cne 'Bearer fixture-token' -or $Headers.Accept -cne 'application/vnd.github+json' -or $Headers.'X-GitHub-Api-Version' -cne '2022-11-28'){throw 'equivalent merge API contract mismatch'}
 if($Uri -ceq $env:CI_EXPECTED_URL){return [pscustomobject]@{head=[pscustomobject]@{sha=$global:FixtureHead};base=[pscustomobject]@{sha=$global:FixtureBase};merge_commit_sha=$global:RecomputedSha;draft=$false}}
 if($Uri -ceq "https://api.example/repos/fanxiao92/godot-pal/git/commits/$global:RecomputedSha"){return $global:RecomputedCommit}
 throw 'Unexpected equivalent merge API URL'
}
function Run-UnitMergeCase([string]$Name,[object]$Commit,[int]$Expected) {
 $dir=Join-Path $evidence $Name;$null=New-Item -ItemType Directory $dir
 $ep=Join-Path $dir 'original-event.json';$prReady|ConvertTo-Json -Depth 20|Set-Content $ep -Encoding utf8
 $global:RecomputedCommit=$Commit;$env:CI_API_CALLS=0
 try {$out=& $producer -OutputDirectory (Join-Path $dir 'inputs') -RepositoryRoot $fixture -EventName 'pull_request' -Action 'caller' -Mode 'pr-unit' -GithubSha $candidate -RunId 'equivalent-run' -RunAttempt 1 -WorkflowRef 'refs/workflows/ci.yml' -PrNumber '42' -ApiUrl 'https://api.example' -EventJsonPath $ep 2>&1;$code=$LASTEXITCODE} catch {$out=@($_.Exception.Message);$code=1}
 if($code -ne $Expected){throw "${Name} expected ${Expected}, got ${code}: $($out -join ' ')"}
 if($Expected -ne 0){return}
 $inputs=Join-Path $dir 'inputs';$snapshot=Get-Content (Join-Path $inputs 'snapshot.json') -Raw|ConvertFrom-Json -NoEnumerate
 if($snapshot.candidateSha -cne $candidate -or $snapshot.serverMergeCandidate.sha -cne $global:RecomputedSha){throw "${Name}: actual and API merge identities were not retained separately"}
 foreach($file in @('api-pull-request.json','api-merge-commit.json')){if(-not(Test-Path (Join-Path $inputs $file))){throw "${Name}: API original missing: $file"}}
 if($env:CI_API_CALLS -ne 2){throw "${Name}: expected exactly PR and commit API calls"}
 $out=& $validator -EventPath (Join-Path $inputs 'event.json') -MetadataPath (Join-Path $inputs 'metadata.json') -RepositoryRoot $fixture -ServerSnapshotPath (Join-Path $inputs 'snapshot.json') -EvidenceDirectory (Join-Path $dir 'proof') 2>&1
 if($LASTEXITCODE -ne 0){throw "${Name}: validator rejected verified equivalent merge: $($out -join ' ')"}
 $identity=Get-Content (Join-Path $dir 'proof/candidate-identity.json') -Raw|ConvertFrom-Json -NoEnumerate
 if($identity.actualCheckout -cne $candidate -or $identity.candidateSha -cne $candidate){throw "${Name}: tested commit was relabelled as API merge"}
}
$equivalent=[pscustomobject]@{sha=$global:RecomputedSha;tree=[pscustomobject]@{sha=$tree};parents=@([pscustomobject]@{sha=$base},[pscustomobject]@{sha=$head})}
Run-UnitMergeCase 'unit-equivalent-merge' $equivalent 0
foreach($bad in @('returned-sha','tree','parents-order','parents-singleton','missing-tree','parent-type')) {
 $copy=$equivalent|ConvertTo-Json -Depth 10|ConvertFrom-Json -NoEnumerate
 switch($bad){
  'returned-sha' {$copy.sha='0'*40}
  'tree' {$copy.tree.sha='1'*40}
  'parents-order' {$copy.parents=@($copy.parents[1],$copy.parents[0])}
  'parents-singleton' {$copy.parents=@($copy.parents[0])}
  'missing-tree' {$copy.PSObject.Properties.Remove('tree')}
  'parent-type' {$copy.parents=@('not-an-object',$copy.parents[1])}
 }
 Run-UnitMergeCase ('unit-equivalent-reject-'+$bad) $copy 1
}
$null=$env:CI_TOKEN;Write-Output 'PASS CiCandidateInputs public pipeline, including same-tree/ordered-parent merge regeneration and six exact rejection controls; legacy modes remain strict.';if(-not $EvidenceDirectory){Remove-Item $evidence -Recurse -Force}
