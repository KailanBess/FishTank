$ErrorActionPreference='Stop'
$global:FishTankFixtureRealtime=$true
$global:FishTankFixtureSignatureAge=0
$global:FishTankFixtureBoot=$true
$global:FishTankFixtureTasksUnavailable=$false
$script:checks=0
function Assert($Condition,[string]$Name){if(!$Condition){throw $Name};$script:checks++}
function Get-MpComputerStatus{[pscustomobject]@{RealTimeProtectionEnabled=$global:FishTankFixtureRealtime;AMRunningMode='Normal';AntivirusSignatureAge=$global:FishTankFixtureSignatureAge;AntivirusSignatureVersion='fixture'}}
function Get-NetFirewallProfile{[pscustomobject]@{Name='Private';Enabled=$true}}
function Confirm-SecureBootUEFI{if($null -eq $global:FishTankFixtureBoot){throw 'Fixture access denied'};$global:FishTankFixtureBoot}
function Get-BitLockerVolume{param($MountPoint);[pscustomobject]@{ProtectionStatus=1;VolumeStatus='FullyEncrypted'}}
function Get-Service{param($Name);[pscustomobject]@{StartType='Manual'}}
function Test-Path{param($Path,$LiteralPath);$target=$Path;if($LiteralPath){$target=$LiteralPath};if($target -like 'HKLM:*'){return $false};Microsoft.PowerShell.Management\Test-Path -LiteralPath $target}
function Get-CimInstance{
 param($ClassName)
 if($ClassName -eq 'Win32_StartupCommand'){[pscustomobject]@{Name='Fixture';Command='fixture.exe --token SECRET_FIXTURE_DO_NOT_RETAIN';Location='Fixture startup';User='Fixture user'}}
 elseif($ClassName -eq 'Win32_Service'){[pscustomobject]@{Name='FixtureService';StartMode='Auto';StartName='Fixture account';PathName='fixture.exe --token SECRET_FIXTURE_DO_NOT_RETAIN'}}
 else{throw 'Unexpected fixture class'}
}
function Get-ScheduledTask{
 if($global:FishTankFixtureTasksUnavailable){throw 'Fixture task access denied'}
 [pscustomobject]@{TaskPath='\Fixture\';TaskName='FixtureTask';Settings=[pscustomobject]@{Enabled=$true};Actions=@([pscustomobject]@{Execute='fixture.exe';Arguments='--token SECRET_FIXTURE_DO_NOT_RETAIN'});Triggers=@([pscustomobject]@{Enabled=$true;StartBoundary='2026-01-01T00:00:00'});Principal=[pscustomobject]@{UserId='Fixture user';RunLevel=0}}
}
$collector=Join-Path $PSScriptRoot '..\desktop\FishTank.Collect.ps1'
$health=& $collector -Mode SecurityHealth | ConvertFrom-Json
Assert ($health.Count -eq 7) 'Health checks did not all execute'
Assert (($health | Where-Object Name -eq 'Defender real-time protection').State -eq 'No issue observed') 'Enabled protection not recognized'
$global:FishTankFixtureRealtime=$false
$global:FishTankFixtureSignatureAge=10
$global:FishTankFixtureBoot=$null
$health=& $collector -Mode SecurityHealth | ConvertFrom-Json
Assert (($health | Where-Object Name -eq 'Defender real-time protection').State -eq 'Review') 'Disabled protection not flagged'
Assert (($health | Where-Object Name -eq 'Defender signatures').State -eq 'Review') 'Old signatures not flagged'
Assert (($health | Where-Object Name -eq 'Secure Boot').State -eq 'Unknown') 'Unavailable boot check reported safe'
$global:FishTankFixtureRealtime=$null
$health=& $collector -Mode SecurityHealth | ConvertFrom-Json
Assert (($health | Where-Object Name -eq 'Defender real-time protection').State -eq 'Unknown') 'Missing protection value reported safe'
$first=& $collector -Mode StartupInventory
$second=& $collector -Mode StartupInventory
Assert ($first -eq $second) 'Unchanged startup collection was unstable'
Assert (!$first.Contains('SECRET_FIXTURE_DO_NOT_RETAIN')) 'Startup argument secret was retained'
$startup=$first | ConvertFrom-Json
Assert ($startup.Count -eq 4) 'Startup fixture coverage incomplete'
$global:FishTankFixtureTasksUnavailable=$true
$startup=& $collector -Mode StartupInventory | ConvertFrom-Json
Assert (($startup | Where-Object Name -eq 'Task inventory unavailable').State -eq 'Unknown') 'Task access failure not unknown'
Write-Host "$script:checks collector fixture checks passed; no Windows security configuration changed."
