param([Parameter(Mandatory=$true)][string]$Tag)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Test-ReleaseReadiness.ps1') -Tag $Tag
$stage=Join-Path $root ('packages/signed-'+$Tag)
if(Test-Path -LiteralPath $stage){throw 'Signed staging folder already exists; use a fresh build workspace.'}
New-Item -ItemType Directory -Path $stage | Out-Null
$files=@('desktop/bin/Release/net48/FishTank.exe','desktop/bin/Release/cli/net48/FishTank.Cli.exe')
$version=[Version]$Tag.Substring(1)
$expectedVersion=[Version]::new($version.Major,$version.Minor,[Math]::Max(0,$version.Build),[Math]::Max(0,$version.Revision))
$pin=(Get-Content -LiteralPath (Join-Path $root 'packages/PublisherCertificateSha256.txt') -Raw).Trim()
foreach($relative in $files){
 $file=Join-Path $root $relative
 if([Reflection.AssemblyName]::GetAssemblyName($file).Version -ne $expectedVersion){throw 'Signed executable version does not match the release tag.'}
 $signature=Get-AuthenticodeSignature -LiteralPath $file
 if($signature.Status -ne 'Valid' -or $null -eq $signature.TimeStamperCertificate){throw 'Only valid timestamped executables can enter the signed bundle.'}
 $sha=[Security.Cryptography.SHA256]::Create()
 try{$actual=([BitConverter]::ToString($sha.ComputeHash($signature.SignerCertificate.RawData))).Replace('-','')}finally{$sha.Dispose()}
 if($actual -ne $pin){throw 'Both executables must match the published certificate fingerprint.'}
 Copy-Item -LiteralPath $file -Destination $stage
 Copy-Item -LiteralPath ($file+'.config') -Destination $stage
}
Copy-Item -LiteralPath (Join-Path $root 'packages/PublisherCertificateSha256.txt') -Destination $stage
foreach($name in @('Install-FishTank.ps1','Uninstall-FishTank.ps1')){
 $file=Join-Path $PSScriptRoot $name
 $signature=Get-AuthenticodeSignature -LiteralPath $file
 if($signature.Status -ne 'Valid' -or !$signature.TimeStamperCertificate){throw 'Installer scripts must be signed and timestamped before packaging.'}
 $sha=[Security.Cryptography.SHA256]::Create()
 try{$actual=([BitConverter]::ToString($sha.ComputeHash($signature.SignerCertificate.RawData))).Replace('-','')}finally{$sha.Dispose()}
 if($actual -ne $pin){throw 'Installer publisher pin mismatch.'}
 Copy-Item -LiteralPath $file -Destination $stage
}
Copy-Item -LiteralPath (Join-Path $root 'docs/RELEASE-AND-RECOVERY.md') -Destination $stage
foreach($name in @('APP-EXPANSION.md','EVERYDAY-TOOLS.md','EVERYDAY-VALIDATION.md','SECURITY-COVERAGE.md','SECURITY-COVERAGE-VALIDATION.md','SERVER-EXPANSION.md','EXPANSION-VALIDATION.md')){Copy-Item -LiteralPath (Join-Path $root ('docs/'+$name)) -Destination $stage}
Copy-Item -LiteralPath (Join-Path $root 'packages/FishTank-sbom.json') -Destination $stage
'Extract into a new folder. Verify publisher signatures and SHA256SUMS.txt. Run FishTank.exe. Use --portable for an isolated profile. Keep existing profiles and vault passphrases backed up privately.' | Set-Content -LiteralPath (Join-Path $stage 'README.txt') -Encoding UTF8
$checks=Get-ChildItem -LiteralPath $stage -File | Sort-Object Name | ForEach-Object {((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())+'  '+$_.Name}
$checks | Set-Content -LiteralPath (Join-Path $stage 'SHA256SUMS.txt') -Encoding ASCII
Copy-Item -LiteralPath (Join-Path $stage 'SHA256SUMS.txt') -Destination (Join-Path $root 'packages/SHA256SUMS.txt')
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath (Join-Path $root ('packages/FishTank-'+$Tag+'-signed.zip'))
