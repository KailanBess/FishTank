param([Parameter(Mandatory=$true)][string]$Tag)
$ErrorActionPreference='Stop'
if($Tag -notmatch '^v[0-9]+\.[0-9]+(?:\.[0-9]+){0,2}$'){throw 'Use a stable numeric vMajor.Minor.Build.Revision tag.'}
if(!$env:FISHTANK_SIGNING_PFX_BASE64 -or !$env:FISHTANK_SIGNING_PASSWORD -or !$env:FISHTANK_TIMESTAMP_URL){throw 'Configure the release environment signing certificate/password secrets and HTTPS timestamp URL before publishing signed updates.'}
$timestamp=[Uri]$env:FISHTANK_TIMESTAMP_URL
if($timestamp.Scheme -ne 'https' -or $timestamp.UserInfo -ne '' -or $timestamp.Query -ne '' -or $timestamp.Fragment -ne ''){throw 'Use the certificate provider supported HTTPS timestamp endpoint without credentials, query parameters or fragments.'}
$root=Split-Path -Parent $PSScriptRoot
$files=@((Join-Path $root 'desktop/bin/Release/net48/FishTank.exe'),(Join-Path $root 'desktop/bin/Release/cli/net48/FishTank.Cli.exe'))
$version=[Version]$Tag.Substring(1)
$normalized=[Version]::new($version.Major,$version.Minor,[Math]::Max(0,$version.Build),[Math]::Max(0,$version.Revision))
foreach($file in $files){if([Reflection.AssemblyName]::GetAssemblyName($file).Version -ne $normalized){throw 'Release tag does not match both executable assembly versions.'}}
$signer=Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" | Sort-Object FullName -Descending | Select-Object -First 1
if(!$signer){throw 'Windows SDK SignTool was not found.'}
$pfx=Join-Path ([IO.Path]::GetTempPath()) ('FishTank-signing-'+[Guid]::NewGuid().ToString('N')+'.pfx')
$storeName='FishTankRelease-'+[Guid]::NewGuid().ToString('N')
$store=[Security.Cryptography.X509Certificates.X509Store]::new($storeName,[Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
$store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite);$store.Close()
$certificate=$null
$imported=@()
try{
 [IO.File]::WriteAllBytes($pfx,[Convert]::FromBase64String($env:FISHTANK_SIGNING_PFX_BASE64))
 $password=ConvertTo-SecureString -String $env:FISHTANK_SIGNING_PASSWORD -AsPlainText -Force
 $imported=@(Import-PfxCertificate -FilePath $pfx -CertStoreLocation ('Cert:\CurrentUser\'+$storeName) -Password $password)
 $private=@($imported | Where-Object HasPrivateKey)
 if($private.Count -ne 1){throw 'Use one release-signing certificate with its private key.'}
 $certificate=$private[0]
 foreach($file in $files){
  & $signer.FullName sign /s $storeName /sha1 $certificate.Thumbprint /fd SHA256 /tr $timestamp.AbsoluteUri /td SHA256 $file
  if($LASTEXITCODE -ne 0){throw 'Authenticode signing failed.'}
  & $signer.FullName verify /pa /all /v $file
  if($LASTEXITCODE -ne 0){throw 'Signed release verification failed.'}
  $signature=Get-AuthenticodeSignature -LiteralPath $file
  if($signature.Status -ne 'Valid' -or $null -eq $signature.TimeStamperCertificate){throw 'Release requires valid, timestamped publisher signatures.'}
 }
 $sha=[Security.Cryptography.SHA256]::Create()
 try{$pin=([BitConverter]::ToString($sha.ComputeHash($certificate.RawData))).Replace('-','')}finally{$sha.Dispose()}
 $pin | Set-Content -LiteralPath (Join-Path $root 'packages/PublisherCertificateSha256.txt') -Encoding ASCII
 Write-Host 'Both release executables are signed and verified. Only the public certificate fingerprint is included in release artifacts.'
}finally{
 if(Test-Path -LiteralPath $pfx){Remove-Item -LiteralPath $pfx -Force -ErrorAction SilentlyContinue}
 Get-ChildItem -LiteralPath ('Cert:\CurrentUser\'+$storeName) -ErrorAction SilentlyContinue | ForEach-Object {Remove-Item -LiteralPath $_.PSPath -DeleteKey -Force -Confirm:$false -ErrorAction SilentlyContinue}
 Remove-Item -LiteralPath ('Cert:\CurrentUser\'+$storeName) -Force -Confirm:$false -ErrorAction SilentlyContinue
 $password=$null
}
