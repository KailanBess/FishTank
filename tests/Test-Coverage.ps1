$ErrorActionPreference='Stop'
$checks=0
function Assert($Value,$Name){if(!$Value){throw $Name};$script:checks++}
function Reject([scriptblock]$Action,$Name){$failed=$false;try{& $Action | Out-Null}catch{$failed=$true};Assert $failed $Name}
$source=Get-Content -LiteralPath (Join-Path $PSScriptRoot '../desktop/FishTank.Coverage.ps1') -Raw
$tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseInput($source,[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Coverage resource syntax invalid.'}
foreach($node in $ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst]},$true)){Invoke-Expression $node.Extent.Text}
$script:mode=0;$script:writes=0;$script:domain=$false;$script:active=$true;$script:tasksDenied=$false;$script:eventsDenied=$false;$script:fixturePid=123;$script:readbackFails=$false
function Get-MpPreference{[pscustomobject]@{EnableControlledFolderAccess=$script:mode;ControlledFolderAccessProtectedFolders=@('C:\Fixture');ControlledFolderAccessAllowedApplications=@();AttackSurfaceReductionRules_Ids=@('d4f940ab-401b-4efc-aadc-ad5f3c50688a');AttackSurfaceReductionRules_Actions=@($script:mode);AttackSurfaceReductionOnlyExclusions=@();MAPSReporting=2}}
function Get-MpComputerStatus{[pscustomobject]@{AMRunningMode=$(if($script:active){'Normal'}else{'Passive'});AntivirusEnabled=$true;RealTimeProtectionEnabled=$true;IsTamperProtected=$true}}
function Get-CimInstance{
 param($ClassName,$Namespace,$Filter)
 switch($ClassName){
  'Win32_OperatingSystem'{[pscustomobject]@{ProductType=1;BuildNumber='22631'}}
  'Win32_ComputerSystem'{[pscustomobject]@{PartOfDomain=$script:domain}}
  'Win32_StartupCommand'{[pscustomobject]@{Name='Fixture';Location='Fixture';User='Fixture';Command='fixture.exe --token SECRET_FIXTURE_NEVER_RETAIN'}}
  'Win32_Service'{[pscustomobject]@{Name='FixtureService';StartMode='Auto';StartName='Fixture';PathName='fixture.exe --token SECRET_FIXTURE_NEVER_RETAIN'}}
  'Win32_DeviceGuard'{[pscustomobject]@{VirtualizationBasedSecurityStatus=2;SecurityServicesConfigured=@(2);SecurityServicesRunning=@(2);CodeIntegrityPolicyEnforcementStatus=2;UsermodeCodeIntegrityPolicyEnforcementStatus=1}}
  default{@()}
 }
}
function Test-Path{param($Path);$false}
function Set-MpPreference{param($EnableControlledFolderAccess);$script:writes++;if(!$script:readbackFails){$script:mode=[int]$EnableControlledFolderAccess}}
function Add-MpPreference{param($AttackSurfaceReductionRules_Ids,$AttackSurfaceReductionRules_Actions);$script:writes++;if(!$script:readbackFails){$script:mode=[int]$AttackSurfaceReductionRules_Actions}}
function Get-ScheduledTask{if($script:tasksDenied){throw 'Fixture task permission denied'};[pscustomobject]@{TaskName='FixtureTask';TaskPath='\Fixture\';Actions=@([pscustomobject]@{Execute='fixture.exe';Arguments='SECRET_FIXTURE_NEVER_RETAIN'});Settings=[pscustomobject]@{Enabled=$true};Triggers=@();Principal=[pscustomobject]@{UserId='Fixture';RunLevel=0}}}
function Get-WinEvent{param($ListLog,$FilterHashtable,$MaxEvents);if($script:eventsDenied){throw 'Fixture log denied'};if($ListLog){[pscustomobject]@{LogName=$ListLog;IsEnabled=$true}}}
function Get-Tpm{[pscustomobject]@{TpmPresent=$true;TpmReady=$true;TpmEnabled=$true;TpmActivated=$true}}
function Confirm-SecureBootUEFI{$true}
function Get-ItemProperty{param($Path);if($Path -like '*Uninstall*'){[pscustomobject]@{PSPath='fixture-software';DisplayName='Fixture app';DisplayVersion='1.0';Publisher='Fixture'}}else{[pscustomobject]@{VerifiedAndReputablePolicyState=2;VulnerableDriverBlocklistEnable=1;fDenyTSConnections=1}}}
function Get-AppxPackage{param($Name);if(!$Name){[pscustomobject]@{Name='Fixture package';Version='1.0';Publisher='Fixture'}}}
function Get-NetTCPConnection{param($State);[pscustomobject]@{LocalAddress='127.0.0.1';LocalPort=45678;OwningProcess=$script:fixturePid}}
function Get-NetUDPEndpoint{[pscustomobject]@{LocalAddress='127.0.0.1';LocalPort=45679;OwningProcess=$script:fixturePid}}
function Get-Process{param($Id);[pscustomobject]@{ProcessName='Fixture';Path='C:\Fixture\fixture.exe'}}
function Get-NetFirewallRule{param($PolicyStore);[pscustomobject]@{Name='FixtureRule';Enabled='True';Direction='Inbound';Action='Allow';Profile='Private'}}
function Get-NetFirewallPortFilter{param([Parameter(ValueFromPipeline=$true)]$InputObject);process{[pscustomobject]@{Protocol='TCP';LocalPort='45678';RemotePort='Any'}}}
function Get-NetFirewallAddressFilter{param([Parameter(ValueFromPipeline=$true)]$InputObject);process{[pscustomobject]@{LocalAddress='Any';RemoteAddress='LocalSubnet'}}}
function Get-NetFirewallApplicationFilter{param([Parameter(ValueFromPipeline=$true)]$InputObject);process{[pscustomobject]@{Program='C:\Fixture\fixture.exe';Package=''}}}
foreach($area in @('Ransomware','ASR','AppControl','Hardware','Software','Updates','Persistence','Network')){
 $rows=@(Invoke-FTCoverage ([pscustomobject]@{Mode=$area}));Assert ($rows.Count -gt 0) "$area returned no coverage"
 Assert ($script:writes -eq 0) "$area changed protection during collection"
}
$persistence=@(Invoke-FTCoverage ([pscustomobject]@{Mode='Persistence'})) | ConvertTo-Json -Depth 8 -Compress
Assert (!$persistence.Contains('SECRET_FIXTURE_NEVER_RETAIN')) 'Persistence retained command arguments'
$script:tasksDenied=$true;$rows=@(Invoke-FTCoverage ([pscustomobject]@{Mode='Persistence'}));Assert (($rows | Where-Object Name -eq 'Task coverage').State -eq 'Unknown') 'Unavailable task coverage was reported complete';$script:tasksDenied=$false
$before=@(Invoke-FTCoverage ([pscustomobject]@{Mode='Network'})) | ConvertTo-Json -Depth 8 -Compress;$script:fixturePid=456
$after=@(Invoke-FTCoverage ([pscustomobject]@{Mode='Network'})) | ConvertTo-Json -Depth 8 -Compress;Assert ($before -eq $after) 'Process restarts changed endpoint identities without a configuration change'
$script:eventsDenied=$true;$rows=@(Invoke-FTCoverage ([pscustomobject]@{Mode='Ransomware'}));Assert (($rows | Where-Object Name -like 'Events|*').State -eq 'Unknown') 'Event permission gap not unknown';$script:eventsDenied=$false
$request=[pscustomobject]@{Mode='ControlState';Control='ASR';RuleId='d4f940ab-401b-4efc-aadc-ad5f3c50688a'}
$state=Invoke-FTCoverage $request;Assert ($state.Ready -and $state.Value -eq 0) 'Prerequisites/control state incorrect'
$script:domain=$true;$state=Invoke-FTCoverage $request;Assert (!$state.Ready) 'Managed device allowed local changes';$script:domain=$false
$script:active=$false;$state=Invoke-FTCoverage $request;Assert (!$state.Ready) 'Passive Defender allowed protection changes';$script:active=$true
$apply=[pscustomobject]@{Mode='ApplyControl';Control='ASR';RuleId=$request.RuleId;Expected=0;NewValue=1;AuditReviewed=$true}
Reject {Invoke-FTCoverage $apply} 'Block bypassed audit-first gate';Assert ($script:writes -eq 0) 'Rejected plan wrote protection'
$apply.NewValue=2;$reply=Invoke-FTCoverage $apply;Assert ($reply.Verified -and $script:mode -eq 2 -and $script:writes -eq 1) 'Audit request was not readback-verified under mocks'
$apply.Expected=0;Reject {Invoke-FTCoverage $apply} 'Stale preview accepted'
$apply.Expected=2;$apply.NewValue=1;$apply.AuditReviewed=$false;Reject {Invoke-FTCoverage $apply} 'Unreviewed audit allowed blocking'
$apply.AuditReviewed=$true;$reply=Invoke-FTCoverage $apply;Assert ($reply.Verified -and $script:mode -eq 1) 'Reviewed audit did not transition under mocks'
$apply.Expected=1;$apply.NewValue=2;Reject {Invoke-FTCoverage $apply} 'Enforcement was weakened to audit'
$script:mode=6;$apply.Expected=6;Reject {Invoke-FTCoverage $apply} 'Warn enforcement was weakened to audit'
$apply.RuleId='unknown';Reject {Invoke-FTCoverage $apply} 'Unknown rule accepted'
$apply.RuleId=$request.RuleId;$script:mode=0;$script:readbackFails=$true;$apply.Expected=0;$apply.NewValue=2;Reject {Invoke-FTCoverage $apply} 'Failed Windows readback reported success'
Write-Host "$checks coverage fixture checks passed. Every collection/mutation command was mocked; no Windows protection setting changed."
