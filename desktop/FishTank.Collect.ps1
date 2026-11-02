param([ValidateSet('Health','Network','Persistence','Events','Tailscale','Hardening','Accounts','Browsers','ScanPolicy')][string]$Mode='Health')
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
$result = switch($Mode) {
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
