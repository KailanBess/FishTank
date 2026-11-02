param([ValidateSet('Health','Network','Persistence','Events','Tailscale','Hardening','Accounts','Browsers','ScanPolicy','SecurityHealth','StartupInventory','Permissions','Exposure','DiskHealth')][string]$Mode='Health')
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
function Read-Check($Name,[scriptblock]$Read) {
    try { [pscustomobject]@{Name=$Name; State='Observed'; Value=(& $Read | Out-String).Trim()} }
    catch { [pscustomobject]@{Name=$Name; State='Unknown'; Value=$_.Exception.Message} }
}
function Read-SafetyCheck($Name,[scriptblock]$Read){
 try{$check=& $Read;if($null -eq $check.Problem){throw 'The required status was not returned.'};[pscustomobject]@{Name=$Name;State=$(if($check.Problem){'Review'}else{'No issue observed'});Value=$check.Evidence+"`n"+$check.Recommendation}}
 catch{[pscustomobject]@{Name=$Name;State='Unknown';Value=$_.Exception.Message}}
}
function Read-LocalJson([string]$Path){
 $item=Get-Item -LiteralPath $Path
 if($item.Length -gt 2MB){throw 'Profile metadata exceeds the 2 MiB limit.'}
 for($part=$item;$null -ne $part;$part=$part.Parent){if($part.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse-point profile paths are not inspected.'};if($part -is [IO.FileInfo]){$part=$part.Directory;if($null -ne $part -and ($part.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Reparse-point profile paths are not inspected.'}}}
 Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Get-TextHash([string]$Text) {
 $sha=[Security.Cryptography.SHA256]::Create()
 try {([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text)))).Replace('-','')}
 finally {$sha.Dispose()}
}
$result = switch($Mode) {


 'DiskHealth' {
  try {
   $disks=@(Get-PhysicalDisk | Sort-Object DeviceId,FriendlyName)
   if($disks.Count -eq 0){throw 'No physical-disk telemetry was returned. Controllers and virtual machines may hide it.'}
   if($disks.Count -gt 64){[pscustomobject]@{Name='Physical disk coverage';State='Unknown';Value='PARTIAL: first 64 physical disks only.'}}
   foreach($disk in ($disks | Select-Object -First 64)){
    $name="Disk $($disk.DeviceId) $($disk.FriendlyName)"
    $health=[string]$disk.HealthStatus
    $ops=@($disk.OperationalStatus | ForEach-Object {[string]$_})
    $bad=@($ops | Where-Object {$_ -in @('Degraded','Stressed','Predictive Failure','Error','Non-Recoverable Error','Lost Communication','No Contact','Failed','Inaccessible','Read-only')})
    $unknown=@($ops | Where-Object {$_ -notin @('OK','Online','In Service','InService')})
    $state=if($health -in @('Warning','Unhealthy') -or $bad.Count -gt 0){'Review'}elseif($health -eq 'Healthy' -and $ops.Count -gt 0 -and $unknown.Count -eq 0){'No issue observed'}else{'Unknown'}
    [pscustomobject]@{Name="$name reported health";State=$state;Value="HealthStatus: $health; OperationalStatus: $($ops -join ', '); MediaType: $($disk.MediaType)`nReported health: $health; status: $($ops -join ', '). A healthy report cannot predict every failure. Back up important data and consult the device vendor if a warning is reported."}
    try {
     $readings=@($disk | Get-StorageReliabilityCounter)
     if($readings.Count -ne 1){throw 'No unambiguous storage reliability counter set was returned.'}
     $counter=$readings[0]
     foreach($metric in @(@{Property='Temperature';Label='temperature';Threshold=70;Unit='C'},@{Property='Wear';Label='reported wear used';Threshold=90;Unit='%'},@{Property='ReadErrorsUncorrected';Label='uncorrected read errors';Threshold=1;Unit=''},@{Property='WriteErrorsUncorrected';Label='uncorrected write errors';Threshold=1;Unit=''})){
      $value=$counter.($metric.Property);$state='Unknown'
      if($null -ne $value -and $value -is [ValueType]){
       $numeric=[double]$value
       $valid=$numeric -ge 0 -and ![double]::IsNaN($numeric) -and ![double]::IsInfinity($numeric)
       if($metric.Property -eq 'Temperature'){$valid=$valid -and $numeric -gt 0 -and $numeric -le 150}
       if($metric.Property -eq 'Wear'){$valid=$valid -and $numeric -le 100}
       if($valid){$state=if($numeric -ge $metric.Threshold){'Review'}else{'No issue observed'}}
      }
      [pscustomobject]@{Name="$name $($metric.Label)";State=$state;Value="Reported value: $value $($metric.Unit); review threshold: $($metric.Threshold). Missing/invalid counters remain unknown.`nReported $($metric.Label): $value $($metric.Unit); review at $($metric.Threshold). Thresholds are general prompts, not vendor failure predictions. Error counts can be historical; preserve backups and consult vendor diagnostics."}
     }
    } catch {[pscustomobject]@{Name="$name reliability counters";State='Unknown';Value="Reliability telemetry unavailable: $($_.Exception.Message)`nPermissions, drivers, USB/RAID adapters and virtual disks can hide these counters. No healthy status is inferred from missing data."}}
   }
  } catch {[pscustomobject]@{Name='Physical disk telemetry';State='Unknown';Value=$_.Exception.Message}}
  try {
   $volumes=@(Get-Volume | Where-Object {$_.DriveLetter -and [string]$_.DriveType -eq 'Fixed'} | Sort-Object DriveLetter)
   if($volumes.Count -eq 0){throw 'No mounted fixed-volume free-space data was returned.'}
   if($volumes.Count -gt 64){[pscustomobject]@{Name='Volume coverage';State='Unknown';Value='PARTIAL: first 64 mounted fixed volumes only.'}}
   foreach($volume in ($volumes | Select-Object -First 64)){
    $state='Unknown';$evidence='Free-space measurements are unavailable or invalid.'
    if($null -ne $volume.Size -and $null -ne $volume.SizeRemaining -and $volume.Size -gt 0 -and $volume.SizeRemaining -ge 0 -and $volume.SizeRemaining -le $volume.Size){
     $percent=100.0*[double]$volume.SizeRemaining/[double]$volume.Size
     $state=if($percent -lt 10 -or $volume.SizeRemaining -lt 2GB){'Review'}else{'No issue observed'}
     $evidence='Free: {0:N1} GiB of {1:N1} GiB ({2:N1}%). Review below 10% or 2 GiB.' -f ($volume.SizeRemaining/1GB),($volume.Size/1GB),$percent
    }
    [pscustomobject]@{Name="Volume $($volume.DriveLetter): free space";State=$state;Value="$evidence`n$evidence Review storage usage carefully before deleting files; low free space is not itself evidence of physical disk failure."}
   }
  } catch {[pscustomobject]@{Name='Volume free-space telemetry';State='Unknown';Value=$_.Exception.Message}}
 }

 'SecurityHealth' {
  Read-SafetyCheck 'Defender real-time protection' {$p=Get-MpComputerStatus;if($null -eq $p.RealTimeProtectionEnabled){throw 'Protection status unavailable'};@{Problem=(!$p.RealTimeProtectionEnabled);Evidence="Real-time protection: $($p.RealTimeProtectionEnabled); mode: $($p.AMRunningMode)";Recommendation='Review Windows Security; another antivirus or organizational policy may control this device.'}}
  Read-SafetyCheck 'Defender signatures' {$p=Get-MpComputerStatus;if($null -eq $p.AntivirusSignatureAge){throw 'Signature age unavailable'};@{Problem=($p.AntivirusSignatureAge -gt 2);Evidence="Signature age in days: $($p.AntivirusSignatureAge); version: $($p.AntivirusSignatureVersion)";Recommendation='Review signature updates. Recent signatures do not prove the device is malware-free.'}}
  Read-SafetyCheck 'Windows Firewall' {$p=@(Get-NetFirewallProfile);if($p.Count -eq 0){throw 'No firewall profiles returned'};@{Problem=(@($p | Where-Object {!$_.Enabled}).Count -gt 0);Evidence=($p | Sort-Object Name | Select-Object Name,Enabled | ConvertTo-Json -Compress);Recommendation='Review disabled profiles in Windows Security; do not change managed policy without your administrator.'}}
  Read-SafetyCheck 'Secure Boot' {$p=Confirm-SecureBootUEFI;if($null -eq $p){throw 'Secure Boot status unavailable'};@{Problem=(!$p);Evidence="Secure Boot enabled: $p";Recommendation='Review firmware support and recovery keys before changing boot settings.'}}
  Read-SafetyCheck 'System-drive encryption' {$drive=$env:SystemDrive;$p=Get-BitLockerVolume -MountPoint $drive;if($null -eq $p.ProtectionStatus){throw 'Encryption status unavailable'};@{Problem=([int]$p.ProtectionStatus -ne 1);Evidence="Drive: $drive; protection: $($p.ProtectionStatus); volume: $($p.VolumeStatus)";Recommendation='Review device encryption and safely store its recovery key. Some editions do not expose BitLocker management.'}}
  Read-SafetyCheck 'Windows Update service' {$p=Get-Service wuauserv;@{Problem=($p.StartType -eq 'Disabled');Evidence="Startup type: $($p.StartType)";Recommendation='A demand-start service may normally be stopped. Open Windows Update for an actual missing-update check; this local review does not contact update servers.'}}
  Read-SafetyCheck 'Pending restart indicators' {$p=(Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') -or (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired');@{Problem=$p;Evidence="Known restart marker present: $p";Recommendation='Save work and review Windows Update. Absence of these markers does not prove every update is installed.'}}
 }
 'StartupInventory' {
  Read-Check 'Coverage' {'Read-only startup commands, scheduled task definitions and automatic services. Command/action content is hashed, not retained. Disabled and running task states are not treated as a compromise verdict.'}
  try {
   $items=@(Get-CimInstance Win32_StartupCommand | Sort-Object Location,User,Name,Command)
   if($items.Count -gt 500){[pscustomobject]@{Name='Startup coverage limit';State='Unknown';Value='More than 500 startup commands; inventory is partial.'}}
   foreach($p in ($items | Select-Object -First 500)){$hash=Get-TextHash ([string]$p.Command);[pscustomobject]@{Name="Startup|$($p.Location)|$($p.User)|$($p.Name)|$hash";State='Observed';Value="Command SHA-256: $hash"}}
  } catch {[pscustomobject]@{Name='Startup inventory unavailable';State='Unknown';Value=$_.Exception.Message}}
  try {
   $items=@(Get-ScheduledTask | Sort-Object TaskPath,TaskName)
   if($items.Count -gt 500){[pscustomobject]@{Name='Task coverage limit';State='Unknown';Value='More than 500 tasks; inventory is partial.'}}
   foreach($p in ($items | Select-Object -First 500)){$definition=@{Actions=@($p.Actions | Select-Object Execute,Arguments,WorkingDirectory,ClassId);Triggers=@($p.Triggers | Select-Object Enabled,StartBoundary,EndBoundary,DaysInterval,WeeksInterval,DaysOfWeek,MonthsOfYear,DaysOfMonth,UserId,Delay);Principal=($p.Principal | Select-Object UserId,GroupId,RunLevel,LogonType)} | ConvertTo-Json -Compress -Depth 5;[pscustomobject]@{Name="Task|$($p.TaskPath)$($p.TaskName)";State='Observed';Value="Enabled: $($p.Settings.Enabled); definition SHA-256: $(Get-TextHash $definition)"}}
  } catch {[pscustomobject]@{Name='Task inventory unavailable';State='Unknown';Value=$_.Exception.Message}}
  try {
   $items=@(Get-CimInstance Win32_Service | Where-Object {$_.StartMode -eq 'Auto'} | Sort-Object Name)
   if($items.Count -gt 500){[pscustomobject]@{Name='Service coverage limit';State='Unknown';Value='More than 500 automatic services; inventory is partial.'}}
   foreach($p in ($items | Select-Object -First 500)){[pscustomobject]@{Name="Service|$($p.Name)";State='Observed';Value="Account: $($p.StartName); command SHA-256: $(Get-TextHash ([string]$p.PathName))"}}
  } catch {[pscustomobject]@{Name='Service inventory unavailable';State='Unknown';Value=$_.Exception.Message}}
 }
 'Permissions' {
  foreach($kind in @('webcam','microphone','location')){
   Read-Check "$kind current-user consent" {
    $root="HKCU:\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\$kind"
    if(!(Test-Path -LiteralPath $root)){return 'No explicit current-user consent record found. This is not proof access is denied; review Windows Privacy settings.'}
    $items=@(Get-Item -LiteralPath $root)+@(Get-ChildItem -LiteralPath $root -Recurse | Select-Object -First 300)
    $items | Sort-Object Name | Select-Object -First 300 | ForEach-Object {$p=Get-ItemProperty -LiteralPath $_.PSPath;[pscustomobject]@{Application=$_.Name;Consent=$p.Value}} | ConvertTo-Json -Compress -Depth 4
    if($items.Count -gt 300){'PARTIAL: only the first 300 records were inspected.'}
   }
  }
  Read-Check 'Machine app privacy policy' {$root='HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy';if(!(Test-Path $root)){return 'No explicit machine AppPrivacy policy. Per-app and Windows privacy settings still apply.'};Get-ItemProperty -LiteralPath $root | Select-Object LetAppsAccessCamera,LetAppsAccessMicrophone,LetAppsAccessLocation | ConvertTo-Json -Compress}
  Read-Check 'Interpretation' {'Consent metadata can be absent or stale and does not cover every desktop application. Use Windows Privacy settings and the separate browser extension review. This inventory changes no permissions.'}
 }
 'Exposure' {
  Read-Check 'TCP listening ports and owning processes' {
   $items=@(Get-NetTCPConnection -State Listen | Sort-Object LocalAddress,LocalPort,OwningProcess)
   $items | Select-Object -First 500 | ForEach-Object {$name='Unknown';try{$name=(Get-Process -Id $_.OwningProcess).ProcessName}catch{};[pscustomobject]@{Address=$_.LocalAddress;Port=$_.LocalPort;Process=$name;Pid=$_.OwningProcess;Binding=$(if($_.LocalAddress -in @('127.0.0.1','::1')){'Loopback'}elseif($_.LocalAddress -in @('0.0.0.0','::')){'All interfaces'}else{'Specific interface'})}} | ConvertTo-Json -Compress
   if($items.Count -gt 500){'PARTIAL: first 500 listeners.'}
  }
  Read-Check 'UDP bound endpoints' {$items=@(Get-NetUDPEndpoint | Sort-Object LocalAddress,LocalPort,OwningProcess);$items | Select-Object -First 500 LocalAddress,LocalPort,OwningProcess | ConvertTo-Json -Compress;if($items.Count -gt 500){'PARTIAL: first 500 UDP endpoints.'}}
  Read-Check 'Enabled inbound allow rules and port filters' {$rules=@(Get-NetFirewallRule -Enabled True -Direction Inbound -Action Allow | Sort-Object Name);foreach($rule in ($rules | Select-Object -First 100)){$ports=@($rule | Get-NetFirewallPortFilter);[pscustomobject]@{Name=$rule.DisplayName;Profile=[string]$rule.Profile;Ports=@($ports | Select-Object Protocol,LocalPort,RemotePort)} | ConvertTo-Json -Compress -Depth 5};if($rules.Count -gt 100){'PARTIAL: first 100 inbound allow rules.'}}
  Read-Check 'Reachability limits' {'A listener or allow rule is not proof of internet reachability or maliciousness. Effective access also depends on rule scope, interfaces, upstream firewalls, routers and VPN policy. Process IDs change normally. No remote probe or firewall change was made.'}
 }

 'ScanPolicy' {
  Read-Check 'Defender scanner / signatures / active mode' {Get-MpComputerStatus | Select-Object AMRunningMode,AntivirusEnabled,AMServiceEnabled,AMEngineVersion,AMProductVersion,AntivirusSignatureVersion,AntivirusSignatureLastUpdated,AntivirusSignatureAge,QuickScanEndTime,FullScanEndTime | Format-List}
  Read-Check 'Existing Defender scan policy (read only)' {Get-MpPreference | Select-Object DisableArchiveScanning,DisableRemovableDriveScanning,DisableScanningNetworkFiles,DisableScanningMappedNetworkDrivesForFullScan,ScanAvgCPULoadFactor,ScanOnlyIfIdleEnabled,DisableCpuThrottleOnIdleScans | Format-List}
 }
 'Hardening' {
  Read-SafetyCheck 'Defender potentially unwanted app protection' {$p=Get-MpPreference;if($null -eq $p.PUAProtection){throw 'PUA status unavailable'};@{Problem=($p.PUAProtection -ne 1);Evidence="PUAProtection: $($p.PUAProtection) (0 disabled, 1 enabled, 2 audit)";Recommendation='Review in Windows Security. Audit mode does not block.'}}
  Read-SafetyCheck 'Controlled folder access' {$p=Get-MpPreference;if($null -eq $p.EnableControlledFolderAccess){throw 'Controlled folder access status unavailable'};@{Problem=($p.EnableControlledFolderAccess -ne 1);Evidence="Controlled folder access mode: $($p.EnableControlledFolderAccess) (1 enabled; other modes may audit or disable)";Recommendation='Optional ransomware protection can affect legitimate apps. Review protected folders and recovery before enabling in Windows Security.'}}
  Read-SafetyCheck 'Defender script scanning' {$p=Get-MpPreference;if($null -eq $p.DisableScriptScanning){throw 'Script scanning status unavailable'};@{Problem=[bool]$p.DisableScriptScanning;Evidence="DisableScriptScanning: $($p.DisableScriptScanning)";Recommendation='Keep script scanning enabled unless an administrator has an approved alternative.'}}
  Read-SafetyCheck 'Defender network protection' {$p=Get-MpPreference;if($null -eq $p.EnableNetworkProtection){throw 'Network protection status unavailable'};@{Problem=($p.EnableNetworkProtection -ne 1);Evidence="Network protection mode: $($p.EnableNetworkProtection) (1 enabled)";Recommendation='Availability depends on Windows edition, Defender mode and organizational policy. Review before changing.'}}
  Read-SafetyCheck 'Defender tamper protection' {$p=Get-MpComputerStatus;if($null -eq $p.IsTamperProtected){throw 'Tamper protection status unavailable'};@{Problem=(!$p.IsTamperProtected);Evidence="IsTamperProtected: $($p.IsTamperProtected)";Recommendation='Manage tamper protection through Windows Security or your organization.'}}
  Read-SafetyCheck 'SMBv1 server protocol' {$p=Get-SmbServerConfiguration;@{Problem=[bool]$p.EnableSMB1Protocol;Evidence="SMBv1 server enabled: $($p.EnableSMB1Protocol)";Recommendation='Legacy SMBv1 increases exposure. Review compatibility before disabling through supported Windows settings.'}}
  Read-Check 'Attack surface reduction rules' {Get-MpPreference | Select-Object AttackSurfaceReductionRules_Ids,AttackSurfaceReductionRules_Actions | Format-List}
  Read-Check 'Virtualization-based security / memory integrity' {Get-CimInstance -Namespace root\Microsoft\Windows\DeviceGuard -ClassName Win32_DeviceGuard | Select-Object VirtualizationBasedSecurityStatus,SecurityServicesConfigured,SecurityServicesRunning | Format-List}
 }
 'Accounts' {
  Read-SafetyCheck 'Remote Desktop exposure' {$p=Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server';if($null -eq $p.fDenyTSConnections){throw 'RDP status unavailable'};@{Problem=($p.fDenyTSConnections -eq 0);Evidence="Remote Desktop enabled: $($p.fDenyTSConnections -eq 0)";Recommendation='If remote access is needed, restrict reachability and use authenticated access. Enabled does not prove compromise.'}}
  Read-SafetyCheck 'Remote Desktop network-level authentication' {$p=Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp';if($null -eq $p.UserAuthentication){throw 'NLA status unavailable'};@{Problem=($p.UserAuthentication -ne 1);Evidence="UserAuthentication: $($p.UserAuthentication) (1 requires NLA)";Recommendation='Review NLA before permitting RDP connections.'}}
  Read-SafetyCheck 'User Account Control' {$p=Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System';if($null -eq $p.EnableLUA){throw 'UAC status unavailable'};@{Problem=($p.EnableLUA -ne 1);Evidence="EnableLUA: $($p.EnableLUA)";Recommendation='Keep UAC enabled. FishTank does not change elevation policy.'}}
  Read-Check 'Local administrators (inventory, not a compromise verdict)' {Get-LocalGroupMember -SID 'S-1-5-32-544' | Select-Object Name,ObjectClass,PrincipalSource | Format-Table}
  Read-Check 'Local account status' {Get-LocalUser | Select-Object Name,Enabled,PasswordRequired,PasswordLastSet,LastLogon | Format-Table}
  Read-Check 'Recent failed sign-ins (latest 25; requires log permission)' {Get-WinEvent -FilterHashtable @{LogName='Security';Id=4625} -MaxEvents 25 | Select-Object TimeCreated,Id,Message | Format-List}
  Read-Check 'File shares (inventory, not a vulnerability verdict)' {Get-SmbShare | Select-Object Name,Path,EncryptData,Special | Format-Table}
 }
 'Browsers' {
  foreach($browser in @(@{Name='Chrome';Path=(Join-Path $env:LOCALAPPDATA 'Google\Chrome\User Data')},@{Name='Edge';Path=(Join-Path $env:LOCALAPPDATA 'Microsoft\Edge\User Data')})){
   $name=$browser.Name;$root=$browser.Path
   Read-Check "$name extension permissions (current user, first 200 installed versions)" {
    if(!(Test-Path -LiteralPath $root)){return 'No supported local browser profile directory found.'}
    $seen=0
    foreach($profile in (Get-ChildItem -LiteralPath $root -Directory | Where-Object {($_.Name -eq 'Default' -or $_.Name -like 'Profile *') -and !($_.Attributes -band [IO.FileAttributes]::ReparsePoint)} | Select-Object -First 30)){
     $extensions=Join-Path $profile.FullName 'Extensions';if(!(Test-Path -LiteralPath $extensions)){continue}
     foreach($extension in (Get-ChildItem -LiteralPath $extensions -Directory | Where-Object {!($_.Attributes -band [IO.FileAttributes]::ReparsePoint)} | Select-Object -First 200)){
      foreach($version in (Get-ChildItem -LiteralPath $extension.FullName -Directory | Where-Object {!($_.Attributes -band [IO.FileAttributes]::ReparsePoint)} | Select-Object -First 5)){
       if($seen -ge 200){break};$manifest=Join-Path $version.FullName 'manifest.json';if(!(Test-Path -LiteralPath $manifest)){continue};$m=Read-LocalJson $manifest;$seen++
       [pscustomobject]@{Profile=$profile.Name;Id=$extension.Name;Name=$m.name;Version=$m.version;Permissions=($m.permissions -join ', ');Hosts=($m.host_permissions -join ', ');Note='Inventory may include disabled/older versions. Broad permissions require review, not a malware verdict.'} | Format-List
      }
     }
    }
    if($seen -eq 0){'No extension manifests were found in the inspected profiles.'}
   }
  }
  Read-Check 'Firefox extension metadata (current user)' {
   $root=Join-Path $env:APPDATA 'Mozilla\Firefox\Profiles';if(!(Test-Path -LiteralPath $root)){return 'No supported Firefox profile directory found.'}
   foreach($profile in (Get-ChildItem -LiteralPath $root -Directory | Where-Object {!($_.Attributes -band [IO.FileAttributes]::ReparsePoint)} | Select-Object -First 30)){$path=Join-Path $profile.FullName 'extensions.json';if(Test-Path -LiteralPath $path){$json=Read-LocalJson $path;$json.addons | Select-Object -First 200 id,version,active,userDisabled,type | Format-Table}}
  }
 }
 'Health' {
  Read-Check 'Microsoft Defender' { Get-MpComputerStatus | Select-Object AntivirusEnabled,RealTimeProtectionEnabled,AntivirusSignatureLastUpdated,QuickScanEndTime,FullScanEndTime,AMRunningMode | Format-List }
  Read-Check 'Firewall profiles' { Get-NetFirewallProfile | Select-Object Name,Enabled,DefaultInboundAction,DefaultOutboundAction | Format-Table }
  Read-Check 'Secure Boot' { Confirm-SecureBootUEFI }
  Read-Check 'TPM' { Get-Tpm | Select-Object TpmPresent,TpmReady,TpmEnabled | Format-List }
  Read-Check 'BitLocker' { Get-BitLockerVolume | Select-Object MountPoint,VolumeStatus,ProtectionStatus | Format-Table }
  Read-Check 'Windows Update history (not a missing-update assessment)' { Get-HotFix | Sort-Object InstalledOn -Descending | Select-Object -First 10 HotFixID,InstalledOn | Format-Table }
  Read-Check 'Critical services' { Get-Service WinDefend,mpssvc,wuauserv,BFE,EventLog | Select-Object Name,Status,StartType | Format-Table }
  Read-Check 'Proxy' { Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' | Select-Object ProxyEnable,ProxyServer,AutoConfigURL | Format-List }
  Read-Check 'DNS' { Get-DnsClientServerAddress | Where-Object ServerAddresses | Select-Object InterfaceAlias,AddressFamily,ServerAddresses | Format-Table }
 }
 'Network' {
  Read-Check 'TCP connections / listeners (inventory, not a maliciousness verdict)' { Get-NetTCPConnection | Select-Object -First 500 State,LocalAddress,LocalPort,RemoteAddress,RemotePort,OwningProcess | Format-Table }
  Read-Check 'Remote access services' { Get-Service TermService,RemoteRegistry,WinRM,sshd -ErrorAction SilentlyContinue | Select-Object Name,Status,StartType | Format-Table }
  Read-Check 'DNS' { Get-DnsClientServerAddress | Where-Object ServerAddresses | Format-Table InterfaceAlias,ServerAddresses }
  Read-Check 'Firewall' { Get-NetFirewallProfile | Format-Table Name,Enabled }
 }
 'Persistence' {
  Read-Check 'Startup inventory' { Get-CimInstance Win32_StartupCommand | Select-Object Name,Command,Location,User | Format-List }
  Read-Check 'Scheduled tasks (first 300)' { Get-ScheduledTask | Select-Object -First 300 TaskPath,TaskName,State | Format-Table }
 }
 'Events' {
  Read-Check 'Defender events (latest 50)' { Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-Windows Defender/Operational'} -MaxEvents 50 | Select-Object TimeCreated,Id,LevelDisplayName,Message | Format-List }
  Read-Check 'PowerShell events (latest 25; logging may not be enabled)' { Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-PowerShell/Operational';Id=4104} -MaxEvents 25 | Select-Object TimeCreated,Id,Message | Format-List }
 }
 'Tailscale' {
  Read-Check 'Tailscale service' { Get-Service Tailscale | Format-List Status,StartType }
  Read-Check 'Tailscale status (peer visibility is subject to tailnet policy)' {
   $exe=Join-Path $env:ProgramFiles 'Tailscale\tailscale.exe'
   if(!(Test-Path -LiteralPath $exe -PathType Leaf)){ throw 'Tailscale is not installed in the supported location.' }
   & $exe status --json
   if($LASTEXITCODE -ne 0){throw "Tailscale exited with $LASTEXITCODE"}
  }
 }
}
ConvertTo-Json -InputObject @($result) -Depth 8 -Compress
