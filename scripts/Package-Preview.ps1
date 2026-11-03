param([switch]$NoBuild)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if(!$NoBuild){
 & dotnet build (Join-Path $root 'desktop/FishTank.csproj') -c Release
 if($LASTEXITCODE -ne 0){throw 'Desktop build failed.'}
 & dotnet build (Join-Path $root 'desktop/FishTank.csproj') -c Release -p:BuildCli=true
 if($LASTEXITCODE -ne 0){throw 'CLI build failed.'}
}
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('FishTank-package-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try{
 $files=@(
  'scripts/Install-FishTank.ps1',
  'scripts/Uninstall-FishTank.ps1',
  'docs/EXPANSION-DESIGN.md',
  'docs/APP-EXPANSION.md',
  'docs/EVERYDAY-TOOLS.md',
  'docs/SECURITY-COVERAGE.md',
  'docs/SECURITY-COVERAGE-VALIDATION.md',
  'docs/EVERYDAY-VALIDATION.md',
  'docs/EXPANSION-VALIDATION.md',
  'docs/SERVER-EXPANSION.md',
  'packages/FishTank-sbom.json',
  'desktop/bin/Release/net48/FishTank.exe',
  'desktop/bin/Release/net48/FishTank.exe.config',
  'desktop/bin/Release/cli/net48/FishTank.Cli.exe',
  'desktop/bin/Release/cli/net48/FishTank.Cli.exe.config',
  'docs/UI-REVIEW.md',
  'docs/APP-INSIGHTS.md',
  'docs/PREVIEW-UPDATES.md',
  'docs/RELEASE-AND-RECOVERY.md',
  'docs/RELEASE-REVIEW.md',
  'docs/BETA-TEST-GUIDE.md',
  'docs/SECURITY-AND-INTEGRITY.md',
  'docs/PROTECTED-FILES-AND-GITHUB-UPDATES.md',
  'docs/DISK-HEALTH-AND-BACKUP-AGE.md',
  'docs/LIVE-DETECTION.md',
  'docs/LIVE-VALIDATION.md',
  'docs/BROWSER-AND-ACCOUNT-ORGANIZATION.md'
 )
 foreach($file in $files){Copy-Item -LiteralPath (Join-Path $root $file) -Destination $temporary}
 $version=[Reflection.AssemblyName]::GetAssemblyName((Join-Path $temporary 'FishTank.exe')).Version
 @"
FishTank $version unsigned development preview

Requires Windows 10/11 and .NET Framework 4.8. Extract all files into a NEW folder.
For isolated testing, run: FishTank.exe --portable
Portable mode uses a data folder beside the executable. Normal mode uses your existing Windows FishTank profile; back it up privately before testing.

Protected Files adds independent passphrase-encrypted containers for selected files and folders. Read PROTECTED-FILES-AND-GITHUB-UPDATES.md before moving originals or exporting plaintext.

Security Center > Security and integrity tools contains protection reviews, startup and folder integrity, permission/network reviews, backup verification and isolated restore drills, locking/reminder preferences, publisher checks and verified-update staging.
Disk health and free space adds read-only device/capacity warnings; Backup and recovery shows recorded copy age separately from verification age. Read DISK-HEALTH-AND-BACKUP-AGE.md for thresholds, defaults and unknown-data limits.
Monitoring adds optional live Defender event alerts, encrypted local history and a separate keep-in-tray option. Both monitoring and tray mode default off. Read LIVE-DETECTION.md for coverage and privacy limits.
Settings > Open browser dashboard adds a local-only paired browser interface. Passwords now groups email accounts and connected services with separate usernames/passwords. Read BROWSER-AND-ACCOUNT-ORGANIZATION.md for the one-time Windows URL permission (if needed), session controls, and vault migration before editing an existing vault.
Dashboard adds attention items and clear protection status. Antivirus adds optional scan scheduling. Settings adds folder backup reminders, settings search and an editable support report. Activity timeline combines local security events. Read APP-INSIGHTS.md for scope, defaults and limits.
Simple view is the default. Advanced view adds evidence and controls without changing protection.

This is not an audited or production-approved security release. Automated Windows runtime and UI checks are recorded in the validation guides; independent security review and physical-device acceptance remain necessary. See LIVE-VALIDATION.md for live-checking tests and their limits.
The executables are unsigned. Settings > GitHub updates offers opt-in preview updates without a certificate; see PREVIEW-UPDATES.md. These builds cannot pass the separate signed-update gate. GitHub updates have a separate confirmed installer; only signed, pinned, newer releases can be installed. A publisher certificate, GitHub release secrets, signed assets and an independent security audit remain external requirements.

No private configuration, passwords, device tokens or databases are included.
"@ | Set-Content -LiteralPath (Join-Path $temporary 'README.txt') -Encoding UTF8
 $checks=Get-ChildItem -LiteralPath $temporary -File | Sort-Object Name | ForEach-Object {((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())+'  '+$_.Name}
 $checks | Set-Content -LiteralPath (Join-Path $temporary 'SHA256SUMS.txt') -Encoding ASCII
 $destination=Join-Path $root 'packages/FishTank-desktop-preview.zip'
 Compress-Archive -Path (Join-Path $temporary '*') -DestinationPath $destination -Force
 Write-Host "Packaged unsigned preview $version at $destination"
}finally{Remove-Item -LiteralPath $temporary -Recurse -Force}
