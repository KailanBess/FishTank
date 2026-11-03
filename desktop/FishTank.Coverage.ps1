# Request is supplied by FishTank as base64 JSON, not as executable command text.
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
function FT-Item($Name,$State,$Value){[pscustomobject]@{Name=[string]$Name;State=[string]$State;Value=[string]$Value}}
function FT-Read($Name,[scriptblock]$Read){try{FT-Item $Name 'Observed' ((& $Read | Out-String).Trim())}catch{FT-Item $Name 'Unknown' $_.Exception.Message}}
function FT-Status($Name,[scriptblock]$Read){try{$value=& $Read;if(!$value.State -or $null -eq $value.Value){throw 'Status unavailable.'};FT-Item $Name $value.State $value.Value}catch{FT-Item $Name 'Unknown' $_.Exception.Message}}
function FT-Hash([string]$Text){$sha=[Security.Cryptography.SHA256]::Create();try{([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text)))).Replace('-','')}finally{$sha.Dispose()}}
function FT-Events($Log,[int[]]$Ids){
 try{
  $logInfo=Get-WinEvent -ListLog $Log -ErrorAction Stop;if(!$logInfo.IsEnabled){throw 'Event log is disabled; coverage is unavailable.'}
  $filter=@{LogName=$Log;StartTime=(Get-Date).AddDays(-7)};if($Ids.Count){$filter.Id=$Ids}
  $errors=@();$items=@(Get-WinEvent -FilterHashtable $filter -MaxEvents 100 -ErrorAction SilentlyContinue -ErrorVariable errors)
  if($items.Count -eq 0){if(@($errors | Where-Object {$_.FullyQualifiedErrorId -notlike 'NoMatchingEventsFound*'}).Count){throw 'Event query failed.'};FT-Item "Events|$Log" 'Observed' 'No matching events returned in the last seven days. This is not proof no attack occurred.'}
  foreach($event in $items){$message=[string]$event.Message;if($message.Length -gt 3000){$message=$message.Substring(0,3000)+' [truncated]'};FT-Item "Event|$Log|$($event.RecordId)" 'Observed' ("$($event.TimeCreated.ToUniversalTime().ToString('o')) | $($event.ProviderName) | event $($event.Id)`n$message")}
  if($items.Count -eq 100){FT-Item "Event limit|$Log" 'Unknown' 'PARTIAL: latest 100 matching events only.'}
 }catch{FT-Item "Events|$Log" 'Unknown' $_.Exception.Message}
}
function FT-ControlValue($Request){
 $p=Get-MpPreference
 if($Request.Control -eq 'CFA'){if($null -eq $p.EnableControlledFolderAccess){throw 'CFA preference is unavailable.'};return [int]$p.EnableControlledFolderAccess}
 if($Request.Control -ne 'ASR' -or $Request.RuleId -notin @('d4f940ab-401b-4efc-aadc-ad5f3c50688a','3b576869-a4ec-4529-8536-b80a7769e899','9e6c4e1f-7d60-472f-ba1a-a39ef669e4b2','d3e037e1-3eb8-44c8-a917-57927947596d','56a863a9-875e-4185-98a7-b882c64b5ce5','75668c1f-73b5-4cf0-bb93-3ecf5cb7cc84','be9ba2d9-53ea-4cdc-84e5-9b1eeee46550','e6db77e5-3df2-4cf1-b95a-636979351e5b')){throw 'Unsupported protection control or rule.'}
 $ids=@($p.AttackSurfaceReductionRules_Ids | Where-Object {$null -ne $_});$actions=@($p.AttackSurfaceReductionRules_Actions | Where-Object {$null -ne $_})
 if($ids.Count -ne $actions.Count){throw 'ASR identifiers and actions are inconsistent.'}
 for($i=0;$i -lt $ids.Count;$i++){if([string]$ids[$i] -eq $Request.RuleId){return [int]$actions[$i]}}
 return 0
}
function FT-Prerequisites($Request){
 $os=Get-CimInstance Win32_OperatingSystem
 if([int]$os.ProductType -ne 1 -or [int]$os.BuildNumber -lt 19041){throw 'These local changes target Windows 10/11 client builds 19041 or later. This compatibility check does not establish servicing support.'}
 $status=Get-MpComputerStatus
 if($status.AMRunningMode -ne 'Normal' -or $status.AntivirusEnabled -ne $true -or $status.RealTimeProtectionEnabled -ne $true){throw 'Microsoft Defender must be the active antivirus with real-time protection running.'}
 $computer=Get-CimInstance Win32_ComputerSystem
 if($null -eq $computer.PartOfDomain){throw 'Device management state could not be determined.'}
 if($computer.PartOfDomain){throw 'Domain-managed device: use your administrator management policy.'}
 if(Test-Path 'HKLM:\SOFTWARE\Microsoft\Enrollments'){
  if(@(Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Enrollments' | Where-Object {$_.PSChildName -match '^[a-fA-F0-9-]{36}$'}).Count){throw 'Management enrollment was found. Review protection changes with the device administrator.'}
 }
 $key=if($Request.Control -eq 'ASR'){'HKLM:\SOFTWARE\Policies\Microsoft\Windows Defender\Windows Defender Exploit Guard\ASR\Rules'}else{'HKLM:\SOFTWARE\Policies\Microsoft\Windows Defender\Windows Defender Exploit Guard\Controlled Folder Access'}
 if(Test-Path $key){$policy=Get-ItemProperty -LiteralPath $key;if($Request.Control -eq 'ASR'){$value=$policy.($Request.RuleId)}else{$value=$policy.EnableControlledFolderAccess};if($null -ne $value){throw 'This setting has an administrator policy. FishTank will not override it.'}}
 if($Request.Control -eq 'CFA'){$p=Get-MpPreference;if($null -eq $p.MAPSReporting -or [int]$p.MAPSReporting -eq 0){throw 'CFA setup requires cloud-delivered Defender protection. Review Windows Security first.'}}
}
function Invoke-FTCoverage($Request){
 switch([string]$Request.Mode){
  'ControlState' {$value=FT-ControlValue $Request;$ready=$true;$reason='Compatibility observations passed; confirm Windows servicing support and policy before changing protection.';try{FT-Prerequisites $Request}catch{$ready=$false;$reason=$_.Exception.Message};return [pscustomobject]@{Value=$value;Ready=$ready;Reason=$reason}}
  'ApplyControl' {
   FT-Prerequisites $Request
   $old=FT-ControlValue $Request
   if($null -eq $Request.Expected -or $old -ne [int]$Request.Expected){throw 'Setting changed since the preview. Prepare a fresh plan.'}
   if([int]$Request.NewValue -notin @(1,2)){throw 'Only audit and block are supported.'}
   if([int]$Request.NewValue -eq 1 -and ($old -ne 2 -or $Request.AuditReviewed -ne $true)){throw 'Enable audit first and explicitly review compatibility before blocking.'}
   if($old -notin @(0,1,2,6)){throw 'Unknown current mode; no change made.'}
   if($old -in @(1,6) -and [int]$Request.NewValue -eq 2){throw 'An enforced control is not weakened to audit by this tool.'}
   if($Request.Control -eq 'CFA'){Set-MpPreference -EnableControlledFolderAccess ([int]$Request.NewValue)}
   else{Add-MpPreference -AttackSurfaceReductionRules_Ids ([string]$Request.RuleId) -AttackSurfaceReductionRules_Actions ([int]$Request.NewValue)}
   $after=FT-ControlValue $Request
   if($after -ne [int]$Request.NewValue){throw 'Readback did not match. Policy or tamper protection may have prevented the change. Verify Windows Security.'}
   return [pscustomobject]@{Before=$old;After=$after;Verified=$true;Note='Preference readback verified. No attack simulation or protection guarantee is implied.'}
  }
  'Ransomware' {
   FT-Status 'CFA mode and configured folders' {$p=Get-MpPreference;if($null -eq $p.EnableControlledFolderAccess){throw 'CFA mode unavailable.'};$mode=[int]$p.EnableControlledFolderAccess;@{State=$(if($mode -eq 1){'Observed'}elseif($mode -in @(0,2)){'Review'}else{'Unknown'});Value="Mode: $mode (0 disabled, 1 block, 2 audit; other values require review). Audit does not block writes.`nAdditional protected folders: $(@($p.ControlledFolderAccessProtectedFolders) -join '; ')`nAllowed applications: $(@($p.ControlledFolderAccessAllowedApplications) -join '; ')`nDefault Windows protected folders may not be enumerated here. Exceptions require review; none are added by FishTank."}}
   FT-Read 'Defender runtime prerequisites' {Get-MpComputerStatus | Select-Object AMRunningMode,AntivirusEnabled,RealTimeProtectionEnabled,IsTamperProtected | ConvertTo-Json -Compress}
   FT-Events 'Microsoft-Windows-Windows Defender/Operational' @(1123,1124,1127,1128)
  }
  'ASR' {
   FT-Read 'ASR runtime prerequisites' {Get-MpComputerStatus | Select-Object AMRunningMode,AntivirusEnabled,RealTimeProtectionEnabled,IsTamperProtected | ConvertTo-Json -Compress}
   try{$p=Get-MpPreference;$ids=@($p.AttackSurfaceReductionRules_Ids | Where-Object {$null -ne $_});$actions=@($p.AttackSurfaceReductionRules_Actions | Where-Object {$null -ne $_});if($ids.Count -ne $actions.Count){throw 'Mismatched ASR arrays.'};if(!$ids.Count){FT-Item 'ASR configured rules' 'Observed' 'No explicit ASR rules were returned; not a protection verdict.'};for($i=0;$i -lt $ids.Count;$i++){FT-Item "ASR|$($ids[$i])" 'Observed' "Mode: $($actions[$i]) (0 disabled, 1 block, 2 audit, 6 warn; audit does not block)."};FT-Item 'ASR exclusions' 'Observed' (@($p.AttackSurfaceReductionOnlyExclusions) -join '; ')}catch{FT-Item 'ASR rules' 'Unknown' $_.Exception.Message}
   FT-Events 'Microsoft-Windows-Windows Defender/Operational' @(1121,1122)
  }
  'AppControl' {
   FT-Status 'Smart App Control configured state' {$p=Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy';if($null -eq $p.VerifiedAndReputablePolicyState){throw 'Smart App Control state is not exposed on this system.'};$mode=[int]$p.VerifiedAndReputablePolicyState;@{State=$(if($mode -eq 1){'Observed'}elseif($mode -in @(0,2)){'Review'}else{'Unknown'});Value="Configured state: $mode (0 off, 1 enforcement, 2 evaluation). Availability and effective policy depend on Windows. FishTank does not rewrite this policy."}}
   FT-Read 'Code integrity policy runtime' {Get-CimInstance -Namespace root\Microsoft\Windows\DeviceGuard -ClassName Win32_DeviceGuard | Select-Object CodeIntegrityPolicyEnforcementStatus,UsermodeCodeIntegrityPolicyEnforcementStatus | ConvertTo-Json -Compress}
   FT-Events 'Microsoft-Windows-CodeIntegrity/Operational' @(3076,3077,3089)
   FT-Events 'Microsoft-Windows-AppLocker/EXE and DLL' @(8003,8004)
  }
  'Hardware' {
   FT-Read 'Secure Boot runtime' {"Secure Boot reported enabled: $(Confirm-SecureBootUEFI). Unsupported firmware or missing permissions remains unknown."}
   FT-Read 'TPM readiness' {Get-Tpm | Select-Object TpmPresent,TpmReady,TpmEnabled,TpmActivated | ConvertTo-Json -Compress}
   FT-Read 'VBS and memory integrity runtime' {$d=Get-CimInstance -Namespace root\Microsoft\Windows\DeviceGuard -ClassName Win32_DeviceGuard;if($null -eq $d.VirtualizationBasedSecurityStatus){throw 'VBS runtime unavailable.'};$d | Select-Object VirtualizationBasedSecurityStatus,SecurityServicesConfigured,SecurityServicesRunning,AvailableSecurityProperties,RequiredSecurityProperties | ConvertTo-Json -Compress;'Security service value 2 represents memory integrity. Configured and running are separate observations; reboot and driver compatibility can affect activation.'}
   FT-Read 'Vulnerable driver blocklist preference' {$p=Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Config';if($null -eq $p.VulnerableDriverBlocklistEnable){throw 'No explicit driver-blocklist preference. Defaults and other enforcement mechanisms are not inferred.'};"Explicit preference: $($p.VulnerableDriverBlocklistEnable). This alone does not establish effective driver coverage."}
   FT-Events 'Microsoft-Windows-CodeIntegrity/Operational' @(3077)
  }
  'Software' {
   foreach($root in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*','HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*')){
    try{$apps=@(Get-ItemProperty $root | Where-Object {$_.DisplayName} | Sort-Object PSPath);if($apps.Count -gt 400){FT-Item "Software limit|$root" 'Unknown' 'PARTIAL: first 400 records in this uninstall view.'};foreach($app in ($apps | Select-Object -First 400)){FT-Item "Software|$(FT-Hash $app.PSPath)|$($app.DisplayName)" 'Observed' "Installed version: $($app.DisplayVersion); Publisher: $($app.Publisher). Registry inventory may omit portable apps or contain stale records; no vulnerability verdict is inferred."}}catch{FT-Item "Software view|$root" 'Unknown' $_.Exception.Message}
   }
   FT-Read 'Packaged application inventory' {$packages=@(Get-AppxPackage | Sort-Object PackageFamilyName);$packages | Select-Object -First 200 Name,Version,Publisher | ConvertTo-Json -Compress;if($packages.Count -gt 200){'PARTIAL: first 200 current-user packages.'}}
  }
  'Updates' {
   FT-Read 'Available updates from configured WinGet sources' {
    $package=Get-AppxPackage -Name Microsoft.DesktopAppInstaller | Select-Object -First 1
    if(!$package){throw 'WinGet is unavailable. Use Windows Update or the application vendor.'}
    $exe=Join-Path $package.InstallLocation 'winget.exe';$sig=Get-AuthenticodeSignature -LiteralPath $exe
    if($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch '(?:^|,\s*)O=Microsoft Corporation(?:,|$)'){throw 'The packaged WinGet executable could not be verified as Microsoft-signed. No tool was run.'}
    $p=New-Object Diagnostics.Process;$p.StartInfo=New-Object Diagnostics.ProcessStartInfo;$p.StartInfo.FileName=$exe;$p.StartInfo.Arguments='upgrade --disable-interactivity';$p.StartInfo.UseShellExecute=$false;$p.StartInfo.CreateNoWindow=$true;$p.StartInfo.RedirectStandardOutput=$true;$p.StartInfo.RedirectStandardError=$true
    try{$null=$p.Start();$output=$p.StandardOutput.ReadToEndAsync();$errorOutput=$p.StandardError.ReadToEndAsync();if(!$p.WaitForExit(45000)){try{$p.Kill()}catch{};throw 'WinGet query timed out. No install was requested.'};$text=$output.Result;$errors=$errorOutput.Result;if($p.ExitCode -ne 0){throw "WinGet query did not succeed (exit $($p.ExitCode)). Review sources/agreements manually; no agreement was automatically accepted. $errors"};if($text.Length -gt 100000){throw 'WinGet output exceeds the review limit.'};$text;'Source-reported candidates, not guaranteed coverage or a CVE verdict. No installer was started; available-version data can change.'}finally{$p.Dispose()}
   }
  }
  'Persistence' {
   foreach($spec in @(@{Class='Win32_StartupCommand';Kind='Startup'},@{Class='Win32_Service';Kind='Service'})){
    try{$items=@(Get-CimInstance -ClassName $spec.Class | Sort-Object Name);if($items.Count -gt 500){FT-Item "$($spec.Kind) coverage" 'Unknown' 'PARTIAL: first 500 records.'};foreach($item in ($items | Select-Object -First 500)){if($spec.Kind -eq 'Startup'){$id="$($item.Location)|$($item.User)|$($item.Name)";$value="Command hash: $(FT-Hash ([string]$item.Command))"}else{$id=$item.Name;$value="Start mode: $($item.StartMode); account: $($item.StartName); command hash: $(FT-Hash ([string]$item.PathName))"};FT-Item "$($spec.Kind)|$id" 'Observed' $value}}catch{FT-Item "$($spec.Kind) coverage" 'Unknown' $_.Exception.Message}
   }
   try{$tasks=@(Get-ScheduledTask | Sort-Object TaskPath,TaskName);if($tasks.Count -gt 500){FT-Item 'Task coverage' 'Unknown' 'PARTIAL: first 500 tasks.'};foreach($task in ($tasks | Select-Object -First 500)){$definition=[ordered]@{Actions=@($task.Actions | Select-Object Execute,Arguments,WorkingDirectory,ClassId);Triggers=@($task.Triggers | Select-Object Enabled,StartBoundary,EndBoundary,DaysInterval,WeeksInterval,DaysOfWeek,UserId);Principal=($task.Principal | Select-Object UserId,GroupId,RunLevel,LogonType);Enabled=$task.Settings.Enabled} | ConvertTo-Json -Compress -Depth 6;FT-Item "Task|$($task.TaskPath)$($task.TaskName)" 'Observed' "Definition hash: $(FT-Hash $definition)"}}catch{FT-Item 'Task coverage' 'Unknown' $_.Exception.Message}
   foreach($class in @('__EventFilter','CommandLineEventConsumer','ActiveScriptEventConsumer','__FilterToConsumerBinding')){
    try{$items=@(Get-CimInstance -Namespace root\subscription -ClassName $class | Sort-Object Name,Consumer,Filter);if($items.Count -gt 100){FT-Item "WMI coverage|$class" 'Unknown' 'PARTIAL: first 100 WMI subscriptions.'};foreach($item in ($items | Select-Object -First 100)){$value=$item | Select-Object Name,Query,EventNamespace,CommandLineTemplate,ExecutablePath,ScriptText,Consumer,Filter | ConvertTo-Json -Compress;FT-Item "WMI|$class|$($item.Name)|$($item.Consumer)|$($item.Filter)" 'Observed' "Definition hash: $(FT-Hash $value)"}}catch{FT-Item "WMI coverage|$class" 'Unknown' $_.Exception.Message}
   }
   FT-Item 'Persistence interpretation' 'Observed' 'Changes are review signals, not proof of malware. No startup item, service, task or subscription was disabled.'
  }
  'Network' {
   foreach($protocol in @('TCP','UDP')){
    try{$items=if($protocol -eq 'TCP'){@(Get-NetTCPConnection -State Listen | Sort-Object LocalAddress,LocalPort,OwningProcess)}else{@(Get-NetUDPEndpoint | Sort-Object LocalAddress,LocalPort,OwningProcess)};if($items.Count -gt 500){FT-Item "$protocol coverage" 'Unknown' 'PARTIAL: first 500 endpoints.'};foreach($endpoint in ($items | Select-Object -First 500)){try{$p=Get-Process -Id $endpoint.OwningProcess;$hash=if($p.Path){FT-Hash $p.Path}else{'Unavailable'};FT-Item "$protocol|$($endpoint.LocalAddress)|$($endpoint.LocalPort)|$($p.ProcessName)" 'Observed' "Process path hash: $hash. Binding is not proof of remote reachability."}catch{FT-Item "$protocol|$($endpoint.LocalAddress)|$($endpoint.LocalPort)|Unknown" 'Unknown' 'Owning process could not be identified.'}}}catch{FT-Item "$protocol coverage" 'Unknown' $_.Exception.Message}
   }
   try{$rules=@(Get-NetFirewallRule -PolicyStore ActiveStore | Sort-Object Name);if($rules.Count -gt 500){FT-Item 'Firewall rule coverage' 'Unknown' 'PARTIAL: first 500 effective firewall rules.'};foreach($rule in ($rules | Select-Object -First 500)){$filters=[ordered]@{Ports=@($rule | Get-NetFirewallPortFilter | Select-Object Protocol,LocalPort,RemotePort);Addresses=@($rule | Get-NetFirewallAddressFilter | Select-Object LocalAddress,RemoteAddress);Applications=@($rule | Get-NetFirewallApplicationFilter | Select-Object Program,Package)} | ConvertTo-Json -Compress -Depth 5;FT-Item "Firewall|$($rule.Name)" 'Observed' "Enabled: $($rule.Enabled); Direction: $($rule.Direction); Action: $($rule.Action); Profile: $($rule.Profile); filters hash: $(FT-Hash $filters)"}}catch{FT-Item 'Firewall rule coverage' 'Unknown' $_.Exception.Message}
   FT-Read 'Remote Desktop configuration' {$p=Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server';if($null -eq $p.fDenyTSConnections){throw 'RDP preference unavailable.'};"Remote Desktop enabled: $($p.fDenyTSConnections -eq 0). This preference alone does not prove remote reachability."}
   FT-Read 'Remote-management service configuration' {Get-CimInstance Win32_Service -Filter "Name='WinRM' OR Name='RemoteRegistry' OR Name='TermService'" | Select-Object Name,StartMode | Sort-Object Name | ConvertTo-Json -Compress}
   FT-Item 'Reachability interpretation' 'Observed' 'No remote port scan or firewall change occurred. Listeners, rules and remote-access preferences require review; upstream/VPN rules and interfaces determine actual reachability. Process restarts and legitimate software updates can change observations.'
  }
  default {throw 'Unsupported coverage request.'}
 }
}
$request=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($RequestBase64)) | ConvertFrom-Json
if($request.Mode -in @('ControlState','ApplyControl')){$json=Invoke-FTCoverage $request | ConvertTo-Json -Compress -Depth 8}
else{$json=ConvertTo-Json -InputObject @(Invoke-FTCoverage $request) -Compress -Depth 8}
# Keep JSON lossless even when the Windows PowerShell child's console uses an OEM codepage.
[regex]::Replace($json,'[^\x00-\x7F]',{param($match) '\u{0:x4}' -f [int][char]$match.Value})
