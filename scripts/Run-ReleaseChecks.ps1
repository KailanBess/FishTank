param([string]$DotnetPath='dotnet',[string]$OutputDirectory='')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if(!$OutputDirectory){$OutputDirectory=Join-Path $root ('test-artifacts/release-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))}
$output=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$results=[Collections.Generic.List[object]]::new()
function Check-Step([string]$Name,[scriptblock]$Action){
 $log=Join-Path $output ($Name+'.log')
 try{& $Action *> $log;if($LASTEXITCODE -ne 0){throw ('Exit code '+$LASTEXITCODE)};$state='Passed';$reason=''}
 catch{$state='Failed';$reason=$_.Exception.Message}
 $results.Add([pscustomobject]@{Name=$Name;State=$state;Reason=$reason;Log=[IO.Path]::GetFileName($log)})
 Write-Host "$Name`: $state"
}
Check-Step 'Logic' { & $DotnetPath run --project (Join-Path $root 'tests/Logic/Logic.csproj') }
Check-Step 'Browser' { & $DotnetPath run --project (Join-Path $root 'tests/Browser/Browser.csproj') }
Check-Step 'PowerShell' { & (Join-Path $root 'tests/Check-PowerShell.ps1'); if($?){$global:LASTEXITCODE=0}else{$global:LASTEXITCODE=1} }
Check-Step 'Collectors' { & (Join-Path $root 'tests/Test-Collectors.ps1'); if($?){$global:LASTEXITCODE=0}else{$global:LASTEXITCODE=1} }
foreach($suite in @('Resilience','WindowsSecurity','WindowsUi')){
 Check-Step $suite {
  & $DotnetPath build (Join-Path $root "tests/$suite/$suite.csproj") -c Release
  if($LASTEXITCODE -ne 0){throw 'Build failed'}
  $program=Join-Path $root "tests/$suite/bin/Release/net48/$suite.exe"
  if($suite -eq 'WindowsUi'){& $program (Join-Path $output 'ui')}else{& $program}
 }
}
Check-Step 'UpdateRollback' {
 & $DotnetPath build (Join-Path $root 'desktop/FishTank.csproj') -c Release -p:BuildCli=true
 if($LASTEXITCODE -ne 0){throw 'CLI build failed'}
 & $DotnetPath build (Join-Path $root 'tests/UpdateFixture/UpdateFixture.csproj') -c Release
 if($LASTEXITCODE -ne 0){throw 'Update fixture build failed'}
 & (Join-Path $root 'tests/Test-UpdateHelper.ps1')
}
$pending=@('Trusted signed install/rollback with maintainer certificate','Separate Windows account and physical display scaling','External authenticator and live email/SMS delivery','Outside usability feedback','Independent security review')
$report=[pscustomobject]@{GeneratedUtc=[DateTime]::UtcNow.ToString('o');Automated=$results;Pending=$pending;ReadyForTrustedRelease=$false}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'release-checks.json') -Encoding UTF8
if(@($results | Where-Object State -eq 'Failed').Count){exit 1}
Write-Host 'Automated checks passed. Manual and external release gates remain pending; see release-checks.json.'
