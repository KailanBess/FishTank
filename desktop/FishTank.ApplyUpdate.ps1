param([string]$PlanBase64)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$plan=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($PlanBase64)) | ConvertFrom-Json
$completed=@();$prepared=@();$status='Failed';$message='Update did not complete.'
function Ordinary([string]$Path){
 $full=[IO.Path]::GetFullPath($Path)
 if($full.StartsWith('\\') -or $full.IndexOf(':',3) -ge 0){throw 'Only local paths without alternate streams are supported.'}
 $item=Get-Item -LiteralPath $full
 for($part=$item;$null -ne $part;$part=$(if($part -is [IO.FileInfo]){$part.Directory}else{$part.Parent})){
  if($part.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked update paths are refused.'}
 }
 return $full
}
function Verify([string]$Path,$Expected){
 [void](Ordinary $Path)
 if((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Expected.Sha256){throw 'Update checksum mismatch.'}
 if($plan.TrustMode -ne 'GitHubPreview'){
 $signature=Get-AuthenticodeSignature -LiteralPath $Path
 if($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate){throw 'Update publisher signature is not valid.'}
 $sha=[Security.Cryptography.SHA256]::Create()
 try{$pin=([BitConverter]::ToString($sha.ComputeHash($signature.SignerCertificate.RawData))).Replace('-','')}finally{$sha.Dispose()}
 if($pin -ne $Expected.PublisherPin){throw 'Update publisher certificate pin mismatch.'}
 }
 $identity=[Reflection.AssemblyName]::GetAssemblyName($Path)
 if($identity.Name+'.exe' -ne $Expected.Name -or $identity.Version.ToString() -ne $plan.Version){throw 'Update assembly identity/version mismatch.'}
}
try{
 if($plan.TrustMode -and $plan.TrustMode -notin @('Publisher','GitHubPreview')){throw 'Unknown update verification mode.'}
 if($plan.TrustMode -eq 'GitHubPreview'){
  if($plan.PreviewConsent -ne $true -or $plan.Repository -ne 'KailanBess/FishTank' -or $plan.ReleaseId -le 0 -or $plan.ReleaseTag -notmatch '\A(?:preview-)?v?[0-9]+\.[0-9]+(?:\.[0-9]+){0,2}\z'){throw 'Unsigned preview requires explicit consent and GitHub release provenance.'}
  $numericTag=$plan.ReleaseTag -replace '^preview-','';$tagVersion=[Version]$numericTag.TrimStart('v');$normalized=[Version]::new($tagVersion.Major,$tagVersion.Minor,[Math]::Max(0,$tagVersion.Build),[Math]::Max(0,$tagVersion.Revision))
  if($normalized -ne [Version]$plan.Version){throw 'Preview release tag/version mismatch.'}
 }
 if($plan.ProfileName -and $plan.ProfileName -notmatch '\A[A-Za-z0-9_-]{1,32}\z'){throw 'Invalid update restart profile.'}
 $target=Ordinary ([string]$plan.TargetDirectory)
 $separator=[IO.Path]::DirectorySeparatorChar
 $stageRoot=(Ordinary ([string]$plan.StageRoot)).TrimEnd($separator)+$separator
 if([IO.Path]::GetPathRoot($target).TrimEnd('\') -eq $target.TrimEnd('\')){throw 'Installing into a drive root is refused.'}
 $windows=[Environment]::GetFolderPath('Windows')
 if($windows -ne ''){$system=$windows.TrimEnd($separator)+$separator;if(($target.TrimEnd($separator)+$separator).StartsWith($system,[StringComparison]::OrdinalIgnoreCase)){throw 'Installing into Windows system folders is refused.'}}
 if(@($plan.Files).Count -ne 2 -or (@($plan.Files | ForEach-Object Name | Sort-Object) -join ',') -ne 'FishTank.Cli.exe,FishTank.exe'){throw 'Unexpected update components.'}
 foreach($file in $plan.Files){
  $source=Ordinary ([string]$file.Path)
  if(!$source.StartsWith($stageRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Update source escaped its staging folder.'}
  Verify $source $file
  $destination=Join-Path $target $file.Name
  if(Test-Path -LiteralPath $destination){[void](Ordinary $destination);$old=[Reflection.AssemblyName]::GetAssemblyName($destination).Version;if($old.ToString() -ne $file.InstalledVersion -or ([Version]$plan.Version) -le $old){throw 'Installed files changed or update would not be newer.'}}
  elseif($file.InstalledVersion -ne ''){throw 'An installed component disappeared.'}
  $temporary=Join-Path $target ($file.Name+'.update-'+[Guid]::NewGuid().ToString('N')+'.exe')
  [IO.File]::Copy($source,$temporary,$false);$durable=[IO.File]::Open($temporary,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::Read);try{$durable.Flush($true)}finally{$durable.Dispose()};Verify $temporary $file
  $prepared+= [pscustomobject]@{File=$file;Temporary=$temporary;Destination=$destination;Existed=(Test-Path -LiteralPath $destination);Backup=$null}
 }
 $parent=Get-Process -Id ([int]$plan.ParentPid) -ErrorAction SilentlyContinue
 if($null -ne $parent -and $parent.StartTime.ToUniversalTime().Ticks -eq [long]$plan.ParentStartTicks){if(!$parent.WaitForExit(60000)){throw 'FishTank did not close. No executables were replaced.'}}
 $backupDirectory=Join-Path $target ('FishTank-update-backup-'+[Guid]::NewGuid().ToString('N'))
 New-Item -ItemType Directory -Path $backupDirectory | Out-Null
 foreach($entry in $prepared){
  Verify $entry.Temporary $entry.File
  if($entry.Existed){
   [void](Ordinary $entry.Destination)
   if([Reflection.AssemblyName]::GetAssemblyName($entry.Destination).Version.ToString() -ne $entry.File.InstalledVersion){throw 'Installed component changed during update.'}
   $entry.Backup=Join-Path $backupDirectory $entry.File.Name
   [IO.File]::Replace($entry.Temporary,$entry.Destination,$entry.Backup)
  }else{[IO.File]::Move($entry.Temporary,$entry.Destination)}
  $completed+=$entry
  Verify $entry.Destination $entry.File
 }
 $status='Updated';$message=$(if($plan.TrustMode -eq 'GitHubPreview'){'Unsigned GitHub preview installed; Windows publisher was not verified. '}else{''})+'Both executables verified and installed. Previous executables are retained in '+$backupDirectory+'. User data and existing runtime configuration were not replaced.'
}catch{
 $message='Update failed: '+$_.Exception.Message
 $rollbackFailed=$false
 for($index=$completed.Count-1;$index -ge 0;$index--){
  $entry=$completed[$index]
  try{
   if($entry.Existed){$restore=$entry.Destination+'.rollback-'+[Guid]::NewGuid().ToString('N');[IO.File]::Copy($entry.Backup,$restore,$false);$failedCopy=Join-Path $backupDirectory ($entry.File.Name+'.failed-new-'+[Guid]::NewGuid().ToString('N'));[IO.File]::Replace($restore,$entry.Destination,$failedCopy)}
   else{[IO.File]::Delete($entry.Destination)}
  }catch{$rollbackFailed=$true}
 }
 if($rollbackFailed){$status='RecoveryRequired';$message+=' Automatic rollback was incomplete. Use the retained executable backups before running FishTank again.'}
 elseif($completed.Count -gt 0){$message+=' Executable replacements were rolled back.'}
}finally{
 foreach($entry in $prepared){try{if(Test-Path -LiteralPath $entry.Temporary){Remove-Item -LiteralPath $entry.Temporary -Force}}catch{$message+=' A temporary verified executable could not be removed.'}}
 try{[pscustomobject]@{Status=$status;Message=$message;Version=$plan.Version;TimeUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json -Compress | Set-Content -LiteralPath $plan.ResultPath -Encoding UTF8}catch{}
}
if($status -eq 'Updated'){
 $desktop=Join-Path $plan.TargetDirectory 'FishTank.exe'
 try{Verify $desktop ($plan.Files | Where-Object Name -eq 'FishTank.exe');if($plan.ProfileName){Start-Process -FilePath $desktop -WorkingDirectory $plan.TargetDirectory -ArgumentList @('--profile',$plan.ProfileName)}elseif($plan.Portable){Start-Process -FilePath $desktop -WorkingDirectory $plan.TargetDirectory -ArgumentList '--portable'}else{Start-Process -FilePath $desktop -WorkingDirectory $plan.TargetDirectory}}catch{}
 exit 0
}
exit 1
