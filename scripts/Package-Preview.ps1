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
  'desktop/bin/Release/net48/FishTank.exe',
  'desktop/bin/Release/net48/FishTank.exe.config',
  'desktop/bin/Release/cli/net48/FishTank.Cli.exe',
  'desktop/bin/Release/cli/net48/FishTank.Cli.exe.config',
  'docs/UI-REVIEW.md',
  'docs/SECURITY-AND-INTEGRITY.md',
  'docs/PROTECTED-FILES-AND-GITHUB-UPDATES.md'
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
Simple view is the default. Advanced view adds evidence and controls without changing protection.

This is not an audited or production-approved security release. Windows runtime and visual validation remain pending in the Linux build workspace; see SECURITY-AND-INTEGRITY.md for the exact checks and limits.
The executables are unsigned and intentionally cannot pass the signed-update gate. GitHub updates have a separate confirmed installer; only signed, pinned, newer releases can be installed. A publisher certificate, GitHub release secrets, signed assets and an independent security audit remain external requirements.

No private configuration, passwords, device tokens or databases are included.
"@ | Set-Content -LiteralPath (Join-Path $temporary 'README.txt') -Encoding UTF8
 $checks=Get-ChildItem -LiteralPath $temporary -File | Sort-Object Name | ForEach-Object {((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())+'  '+$_.Name}
 $checks | Set-Content -LiteralPath (Join-Path $temporary 'SHA256SUMS.txt') -Encoding ASCII
 $destination=Join-Path $root 'packages/FishTank-desktop-preview.zip'
 Compress-Archive -Path (Join-Path $temporary '*') -DestinationPath $destination -Force
 Write-Host "Packaged unsigned preview $version at $destination"
}finally{Remove-Item -LiteralPath $temporary -Recurse -Force}
