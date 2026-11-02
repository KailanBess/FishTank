$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('FishTank-update-test-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
$rsa=[Security.Cryptography.RSA]::Create();$rsa.KeySize=2048
$request=[Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=FishTank fixture only',$rsa,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
$certificate=$request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1),[DateTimeOffset]::UtcNow.AddDays(1))
$global:FishTankFixtureCertificate=$certificate
$global:FishTankFixtureFailurePath=''
$global:FishTankFixtureRestarts=0
function Get-AuthenticodeSignature{param($LiteralPath);[pscustomobject]@{Status=$(if($LiteralPath -eq $global:FishTankFixtureFailurePath){'HashMismatch'}else{'Valid'});SignerCertificate=$global:FishTankFixtureCertificate}}
function Start-Process{param($FilePath,$WorkingDirectory,$ArgumentList);$global:FishTankFixtureRestarts++}
$checks=0
function Assert($Condition,[string]$Message){if(!$Condition){throw $Message};$script:checks++}
try{
 $old=Join-Path $root 'tests/UpdateFixture/bin/Release/net8.0/FishTank.dll'
 $newDesktop=Join-Path $root 'desktop/bin/Release/net48/FishTank.exe'
 $newCli=Join-Path $root 'desktop/bin/Release/cli/net48/FishTank.Cli.exe'
 $version=[Reflection.AssemblyName]::GetAssemblyName($newDesktop).Version.ToString()
 $oldVersion=[Reflection.AssemblyName]::GetAssemblyName($old).Version.ToString()
 $sha=[Security.Cryptography.SHA256]::Create()
 try{$pin=([BitConverter]::ToString($sha.ComputeHash($certificate.RawData))).Replace('-','')}finally{$sha.Dispose()}
 foreach($case in @('success','rollback','locked-cli','tampered-stage')){
  $testRoot=Join-Path $temporary $case;$target=Join-Path $testRoot 'installed';$stage=Join-Path $testRoot 'staged'
  New-Item -ItemType Directory -Path $target,$stage | Out-Null
  $files=@()
  foreach($name in @('FishTank.exe','FishTank.Cli.exe')){
   $destination=Join-Path $target $name;Copy-Item -LiteralPath $old -Destination $destination
   $source=Join-Path $stage $name;Copy-Item -LiteralPath $(if($name -eq 'FishTank.exe'){$newDesktop}else{$newCli}) -Destination $source
   $files+=[pscustomobject]@{Name=$name;Path=$source;Version=$version;Sha256=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash;PublisherPin=$pin;InstalledVersion=$oldVersion}
  }
  $result=Join-Path $testRoot 'result.json'
  $plan=@{Version=$version;Files=$files;TargetDirectory=$target;StageRoot=$stage;ParentPid=[int]::MaxValue;ParentStartTicks=0;ResultPath=$result;Portable=$false}
  $global:FishTankFixtureFailurePath=$(if($case -eq 'rollback'){Join-Path $target 'FishTank.Cli.exe'}else{''})
  $encoded=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($plan | ConvertTo-Json -Depth 8 -Compress)))
  if($case -eq 'tampered-stage'){[IO.File]::AppendAllText((Join-Path $stage 'FishTank.Cli.exe'),'tampered fixture')}
  $held=$null
  if($case -eq 'locked-cli'){$held=[IO.File]::Open((Join-Path $target 'FishTank.Cli.exe'),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)}
  try { & (Join-Path $root 'desktop/FishTank.ApplyUpdate.ps1') -PlanBase64 $encoded }
  finally { if($null -ne $held){$held.Dispose()} }
  $resultData=Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
  if($case -eq 'success'){
   Assert ($LASTEXITCODE -eq 0 -and $resultData.Status -eq 'Updated') 'Helper did not report successful replacement'
   foreach($file in $files){Assert ((Get-FileHash -LiteralPath (Join-Path $target $file.Name)).Hash -eq $file.Sha256) 'Installed executable bytes differ from verified stage'}
   Assert ($global:FishTankFixtureRestarts -eq 1) 'Successful update did not request exactly one restart'
   Assert (@(Get-ChildItem -LiteralPath $target -Directory | Where-Object Name -like 'FishTank-update-backup-*').Count -eq 1) 'Old executable backups were not retained'
  }else{
   Assert ($LASTEXITCODE -ne 0 -and $resultData.Status -eq 'Failed') "Injected verification failure did not fail cleanly: $($resultData.Status), code $LASTEXITCODE, $($resultData.Message)"
   foreach($name in @('FishTank.exe','FishTank.Cli.exe')){Assert ((Get-FileHash -LiteralPath (Join-Path $target $name)).Hash -eq (Get-FileHash -LiteralPath $old).Hash) 'Rollback did not restore previous executable bytes'}
   Assert ($global:FishTankFixtureRestarts -eq 1) 'Failed update restarted unverified binaries'
   if($case -eq 'tampered-stage'){Assert ($resultData.Message -like '*checksum mismatch*') 'Stage tampering was not reported'}
   else{Assert ($resultData.Message -like '*rolled back*') 'Rollback outcome missing from result'}
  }
 }
 Write-Host "$checks update-helper fixture checks passed. Signatures and restart were mocked; no certificate store or real installation was changed."
}finally{
 $global:FishTankFixtureCertificate=$null;$certificate.Dispose();$rsa.Dispose()
 $resolved=[IO.Path]::GetFullPath($temporary)
 $expectedParent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
 if(!$resolved.StartsWith($expectedParent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^FishTank-update-test-[0-9a-f]{32}$'){throw 'Refusing cleanup outside the generated update-test directory.'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
exit 0
