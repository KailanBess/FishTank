$ErrorActionPreference='Stop'
$global:FishTankFixtureRealtime=$true
$global:FishTankFixtureSignatureAge=0
$global:FishTankFixtureBoot=$true
$global:FishTankFixtureTasksUnavailable=$false
$global:FishTankFixtureStatusUnavailable=$false
$global:FishTankFixtureMode='Normal'
$global:FishTankFixtureDiskHealth='Healthy'
$global:FishTankFixtureDiskOps=@('OK')
$global:FishTankFixtureWear=10
$global:FishTankFixtureTemperature=35
$global:FishTankFixtureCountersUnavailable=$false
$global:FishTankFixtureFree=50GB
$global:FishTankFixtureVolumeSize=100GB
$script:checks=0
function Assert($Condition,[string]$Name){if(!$Condition){throw $Name};$script:checks++}
function Get-MpComputerStatus{if($global:FishTankFixtureStatusUnavailable){throw 'Fixture Defender unavailable'};[pscustomobject]@{AntivirusEnabled=$true;RealTimeProtectionEnabled=$global:FishTankFixtureRealtime;AMRunningMode=$global:FishTankFixtureMode;AntivirusSignatureAge=$global:FishTankFixtureSignatureAge;AntivirusSignatureVersion='fixture';AMServiceEnabled=$true;BehaviorMonitorEnabled=$true;IoavProtectionEnabled=$true;NISEnabled=$true;IsTamperProtected=$true}}
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
function Get-PhysicalDisk{[pscustomobject]@{DeviceId='0';FriendlyName='Fixture SSD';HealthStatus=$global:FishTankFixtureDiskHealth;OperationalStatus=$global:FishTankFixtureDiskOps;MediaType='SSD'}}
function Get-StorageReliabilityCounter{param([Parameter(ValueFromPipeline=$true)]$InputObject);process{if($global:FishTankFixtureCountersUnavailable){throw 'Fixture telemetry unavailable'};[pscustomobject]@{Temperature=$global:FishTankFixtureTemperature;Wear=$global:FishTankFixtureWear;ReadErrorsUncorrected=0;WriteErrorsUncorrected=0}}}
function Get-Volume{[pscustomobject]@{DriveLetter='Z';DriveType='Fixed';Size=$global:FishTankFixtureVolumeSize;SizeRemaining=$global:FishTankFixtureFree}}
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
$disk=& $collector -Mode DiskHealth | ConvertFrom-Json
Assert (($disk | Where-Object Name -eq 'Disk 0 Fixture SSD reported health').State -eq 'No issue observed') 'Reported healthy disk not recognized'
Assert (($disk | Where-Object Name -eq 'Disk 0 Fixture SSD temperature').State -eq 'No issue observed') 'Normal temperature misclassified'
Assert (($disk | Where-Object Name -eq 'Volume Z: free space').State -eq 'No issue observed') 'Adequate space misclassified'
$global:FishTankFixtureDiskHealth='Unhealthy';$global:FishTankFixtureWear=95;$global:FishTankFixtureTemperature=75;$global:FishTankFixtureFree=1GB
$disk=& $collector -Mode DiskHealth | ConvertFrom-Json
Assert (($disk | Where-Object Name -eq 'Disk 0 Fixture SSD reported health').State -eq 'Review') 'Unhealthy disk not flagged'
Assert (($disk | Where-Object Name -eq 'Disk 0 Fixture SSD reported wear used').State -eq 'Review') 'High wear not flagged'
Assert (($disk | Where-Object Name -eq 'Disk 0 Fixture SSD temperature').State -eq 'Review') 'High temperature not flagged'
Assert (($disk | Where-Object Name -eq 'Volume Z: free space').State -eq 'Review') 'Low space not flagged'
$global:FishTankFixtureCountersUnavailable=$true;$global:FishTankFixtureDiskHealth=$null;$global:FishTankFixtureFree=$null
$disk=& $collector -Mode DiskHealth | ConvertFrom-Json
Assert (($disk | Where-Object Name -eq 'Disk 0 Fixture SSD reliability counters').State -eq 'Unknown') 'Unavailable reliability data reported healthy'
Assert (($disk | Where-Object Name -eq 'Disk 0 Fixture SSD reported health').State -eq 'Unknown') 'Missing disk health reported healthy'
Assert (($disk | Where-Object Name -eq 'Volume Z: free space').State -eq 'Unknown') 'Missing volume size reported adequate'
$global:FishTankFixtureCountersUnavailable=$false;$global:FishTankFixtureTemperature=0;$global:FishTankFixtureWear=101;$global:FishTankFixtureFree=2GB;$global:FishTankFixtureVolumeSize=20GB
$disk=& $collector -Mode DiskHealth | ConvertFrom-Json
Assert (($disk | Where-Object Name -eq 'Disk 0 Fixture SSD temperature').State -eq 'Unknown') 'Missing/sentinel temperature reported healthy'
Assert (($disk | Where-Object Name -eq 'Disk 0 Fixture SSD reported wear used').State -eq 'Unknown') 'Invalid wear reported healthy'
Assert (($disk | Where-Object Name -eq 'Volume Z: free space').State -eq 'No issue observed') 'Free space boundary misclassified'
$global:FishTankFixtureRealtime=$true
$live=& $collector -Mode LiveProtection | ConvertFrom-Json
Assert ($live.Count -eq 1 -and $live[0].RealTimeProtectionEnabled -eq $true -and $live[0].AntivirusEnabled -eq $true) 'Live readiness flags were not preserved'
Assert ($live[0].ServiceEnabled -eq $true -and $live[0].BehaviorMonitorEnabled -eq $true -and $live[0].IoavProtectionEnabled -eq $true -and $live[0].NisEnabled -eq $true -and $live[0].TamperProtected -eq $true) 'Extended live protection fields lost'
Assert ($live[0].SignatureAgeDays -eq 10 -and $live[0].SignatureVersion -eq 'fixture') 'Live signature age/version lost'
$global:FishTankFixtureRealtime=$false
$live=& $collector -Mode LiveProtection | ConvertFrom-Json
Assert ($live[0].RealTimeProtectionEnabled -eq $false) 'Disabled real-time status was coerced on'
$global:FishTankFixtureRealtime=$null
$live=& $collector -Mode LiveProtection | ConvertFrom-Json
Assert ($null -eq $live[0].RealTimeProtectionEnabled) 'Missing real-time status was coerced to a known value'
$global:FishTankFixtureMode='Passive Mode'
$live=& $collector -Mode LiveProtection | ConvertFrom-Json
Assert ($live[0].Mode -eq 'Passive Mode') 'Passive antivirus mode was hidden'
$global:FishTankFixtureStatusUnavailable=$true
$live=& $collector -Mode LiveProtection | ConvertFrom-Json
Assert ($null -eq $live[0].RealTimeProtectionEnabled -and $live[0].Error) 'Unavailable Defender status was reported active'
Write-Host "$script:checks collector fixture checks passed; no Windows security configuration changed."
