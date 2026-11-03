param([string]$PublisherCertificateSha256,[switch]$AllowUnsignedPreview,[switch]$NoRegistration)
$ErrorActionPreference='Stop'
$source=$PSScriptRoot
$destination=Join-Path $env:LOCALAPPDATA 'Programs/FishTank'
$parent=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs'))
if([IO.Path]::GetFullPath($destination) -ne (Join-Path $parent 'FishTank')){throw 'Unexpected install destination.'}
if(Test-Path -LiteralPath $destination){throw 'An installation already exists. Use verified in-app updates, or uninstall it first. Profiles are retained.'}
$key='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\FishTank'
if(!$NoRegistration -and (Test-Path -LiteralPath $key)){throw 'A FishTank uninstall entry already exists. Review the existing installation first.'}
$manifest=Join-Path $source 'SHA256SUMS.txt'
if(!(Test-Path -LiteralPath $manifest)){throw 'Extract the complete package including SHA256SUMS.txt before installing.'}
$files=@{}
foreach($line in Get-Content -LiteralPath $manifest){
 if($line -notmatch '^([a-fA-F0-9]{64})  ([A-Za-z0-9][A-Za-z0-9._-]{0,120})$'){throw 'Unsafe package manifest entry.'}
 $name=$Matches[2];if($files.ContainsKey($name)){throw 'Duplicate package entry.'}
 $path=Join-Path $source $name
 if((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Package links are refused.'}
 if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $Matches[1]){throw ('Package checksum mismatch: '+$name)}
 $files[$name]=$Matches[1]
}
foreach($name in @('FishTank.exe','FishTank.Cli.exe','Uninstall-FishTank.ps1')){if(!$files.ContainsKey($name)){throw ('Missing component: '+$name)}}
if(!$AllowUnsignedPreview){
 if($PublisherCertificateSha256 -notmatch '^[A-Fa-f0-9]{64}$'){throw 'Supply the independently verified publisher certificate SHA-256 pin, or explicitly choose -AllowUnsignedPreview for development testing.'}
 foreach($name in @('FishTank.exe','FishTank.Cli.exe','Install-FishTank.ps1','Uninstall-FishTank.ps1')){
  $sig=Get-AuthenticodeSignature -LiteralPath (Join-Path $source $name)
  if($sig.Status -ne 'Valid' -or !$sig.TimeStamperCertificate){throw 'A valid timestamped publisher signature is required.'}
  $sha=[Security.Cryptography.SHA256]::Create()
  try{$pin=([BitConverter]::ToString($sha.ComputeHash($sig.SignerCertificate.RawData))).Replace('-','')}finally{$sha.Dispose()}
  if($pin -ne $PublisherCertificateSha256){throw 'Publisher pin mismatch.'}
 }
}
New-Item -ItemType Directory -Path $parent -Force | Out-Null
$stage=Join-Path $parent ('FishTank-install-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
$installed=$false
try{
 foreach($name in $files.Keys){Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $stage $name)}
 @{Owner='FishTank';Version=1;Files=$files} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $stage 'FishTank-install.json') -Encoding UTF8
 Move-Item -LiteralPath $stage -Destination $destination
 $installed=$true
 if(!$NoRegistration){
 New-Item -Path $key | Out-Null
 New-ItemProperty -LiteralPath $key -Name DisplayName -Value 'FishTank' | Out-Null
 New-ItemProperty -LiteralPath $key -Name InstallLocation -Value $destination | Out-Null
 New-ItemProperty -LiteralPath $key -Name DisplayVersion -Value ([Reflection.AssemblyName]::GetAssemblyName((Join-Path $destination 'FishTank.exe')).Version.ToString()) | Out-Null
 New-ItemProperty -LiteralPath $key -Name UninstallString -Value ('powershell.exe -NoProfile -File "'+(Join-Path $destination 'Uninstall-FishTank.ps1')+'"') | Out-Null
 }
 Write-Host ('Installed at '+$destination+'. App profiles were retained. Open FishTank.exe to begin.')
}finally{
 if(!$installed -and (Test-Path -LiteralPath $stage)){
  $resolved=[IO.Path]::GetFullPath($stage)
  if(!$resolved.StartsWith($parent+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'FishTank-install-*'){throw 'Refused unsafe staging cleanup.'}
  Remove-Item -LiteralPath $resolved -Recurse -Force
 }
}
