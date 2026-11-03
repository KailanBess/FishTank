param([string]$Archive=(Join-Path $PSScriptRoot '../packages/FishTank-desktop-preview.zip'))
$ErrorActionPreference='Stop'
$scriptPath=Join-Path $PSScriptRoot '../scripts/Publish-Preview.ps1'
$createdToken=!$env:GH_TOKEN
if($createdToken){$env:GH_TOKEN='fixture'}
$checks=0
function Assert($Condition,[string]$Name){if(!$Condition){throw $Name};$script:checks++}
# This simple function captures every gh call. No native CLI or network call is made.
function gh {
 $arguments=@($args);$state=$global:FishTankPublisherFixture;$global:LASTEXITCODE=0
 if($arguments[0] -eq 'run' -and $arguments[1] -eq 'download'){
  $destination=$arguments[[Array]::IndexOf($arguments,'--dir')+1]
  New-Item -ItemType Directory -Path $destination | Out-Null
  Copy-Item -LiteralPath $state.Archive -Destination (Join-Path $destination 'FishTank-desktop-preview.zip')
  return
 }
 if($arguments[0] -eq 'release' -and $arguments[1] -eq 'create'){
  $assets=@()
  foreach($path in $arguments[3..6]){$assets+=[pscustomobject]@{name=[IO.Path]::GetFileName($path);digest=('sha256:'+(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant())}}
  $state.Created++
  $state.Release=[pscustomobject]@{id=1234;tag_name=$arguments[2];draft=$true;prerelease=$true;author=[pscustomobject]@{login='github-actions[bot]'};body='Unsigned FishTank development preview fixture';assets=$assets}
  return
 }
 if($arguments[0] -eq 'api'){
  $method='GET';if($arguments -contains '--method'){$method=$arguments[[Array]::IndexOf($arguments,'--method')+1]}
  $endpoint=@($arguments | Where-Object {$_ -is [string] -and $_.StartsWith('repos/')})[0]
  if($method -eq 'DELETE'){
   if(!$state.Release.draft -or $state.Release.author.login -ne 'github-actions[bot]'){throw 'Fixture caught deletion of an unrelated release'}
   $state.Deleted++;$state.Release=$null;return
  }
  if($method -eq 'PATCH'){
   if($endpoint -notmatch '/releases/[0-9]+$'){throw 'Draft publication must use release ID'}
   $inputPath=$arguments[[Array]::IndexOf($arguments,'--input')+1];$payload=Get-Content -LiteralPath $inputPath -Raw | ConvertFrom-Json
   if($payload.draft -ne $false -or $payload.prerelease -ne $true -or $payload.target_commitish -ne ('a'*40)){throw 'Invalid draft publication payload'}
   $state.Published++;$state.Release.draft=$false;return
  }
  if($endpoint -like '*/releases?per_page=100'){
   if($null -eq $state.Release){return '[]'}
   return (ConvertTo-Json -InputObject @($state.Release) -Depth 12 -Compress)
  }
  if($endpoint -match '/releases/[0-9]+$'){
   $copy=$state.Release | ConvertTo-Json -Depth 12 | ConvertFrom-Json
   if($state.Case -eq 'bad-digest'){$copy.assets[0].digest='sha256:'+('0'*64)}
   return ($copy | ConvertTo-Json -Depth 12 -Compress)
  }
  throw "Unexpected fixture API path: $endpoint"
 }
 throw 'Unexpected publishing CLI invocation'
}
try{
 foreach($case in @('new','retry-owned-draft','existing-public','owner-draft','bad-digest')){
  $state=@{Case=$case;Archive=[IO.Path]::GetFullPath($Archive);Release=$null;Created=0;Deleted=0;Published=0}
  if($case -in @('retry-owned-draft','existing-public','owner-draft')){
   # Read only metadata from the actual archive to select this version's tag.
   Add-Type -AssemblyName System.IO.Compression.FileSystem
   $zip=[IO.Compression.ZipFile]::OpenRead($state.Archive)
   try{$readme=$zip.GetEntry('README.txt');$reader=New-Object IO.StreamReader($readme.Open());try{$text=$reader.ReadToEnd()}finally{$reader.Dispose()}}finally{$zip.Dispose()}
   # Package README formats differ between local/direct and SDK builders; use the matching assembly source version.
   $source=Get-Content -LiteralPath (Join-Path $PSScriptRoot '../desktop/Properties/AssemblyInfo.cs') -Raw
   if($source -notmatch 'AssemblyVersion\("([0-9.]+)"\)'){throw 'Fixture source version missing'}
   $version=[Version]$Matches[1];$tag='preview-v'+$(if($version.Revision -eq 0){$version.ToString(3)}else{$version.ToString()})
   $state.Release=[pscustomobject]@{id=999;tag_name=$tag;draft=($case -ne 'existing-public');prerelease=$true;author=[pscustomobject]@{login=$(if($case -eq 'owner-draft'){'KailanBess'}else{'github-actions[bot]'})};body='Unsigned FishTank development preview fixture';assets=@()}
  }
  $global:FishTankPublisherFixture=$state;$failed=$false
  try{& $scriptPath -Repository KailanBess/FishTank -Commit ('a'*40) -RunId '123' | Out-Null}catch{$failed=$true}
  if($case -in @('new','retry-owned-draft')){
   Assert (!$failed -and $state.Created -eq 1 -and $state.Published -eq 1) "Valid preview publisher failed: $case"
   Assert ($state.Deleted -eq $(if($case -eq 'retry-owned-draft'){1}else{0})) 'Publisher touched unexpected drafts'
  }elseif($case -eq 'existing-public'){
   Assert (!$failed -and $state.Created -eq 0 -and $state.Deleted -eq 0 -and $state.Published -eq 0) 'Published release was modified'
  }elseif($case -eq 'owner-draft'){
   Assert ($failed -and $state.Deleted -eq 0 -and $state.Published -eq 0) 'Owner draft was overwritten'
  }else{
   Assert ($failed -and $state.Published -eq 0 -and $state.Release.draft) 'Bad digest was publicly published'
  }
 }
 Write-Host "$checks preview-publisher fixture checks passed; every GitHub CLI call was mocked."
}finally{$global:FishTankPublisherFixture=$null;if($createdToken){Remove-Item Env:GH_TOKEN}}
exit 0
