param([string]$Repository='KailanBess/FishTank',[string]$Commit=$env:GITHUB_SHA,[string]$RunId=$env:GITHUB_RUN_ID,[string]$Archive,[switch]$PrepareOnly,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Prepare([string]$Zip,[string]$Output){
 if(Test-Path -LiteralPath $Output){if(@(Get-ChildItem -LiteralPath $Output -Force).Count){throw 'Preview output must be empty.'}}else{New-Item -ItemType Directory -Path $Output | Out-Null}
 $archiveFile=[IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($Zip))
 try{
  $entries=@($archiveFile.Entries)
  if($entries.Count -lt 5 -or $entries.Count -gt 100){throw 'Unexpected preview archive entry count.'}
  $seen=@{};$total=0L
  foreach($entry in $entries){
   if($entry.FullName -notmatch '^[A-Za-z0-9][A-Za-z0-9_. -]*$' -or $entry.FullName.Contains('..') -or $seen.ContainsKey($entry.FullName)){throw 'Preview archive has duplicate or unsafe paths.'}
   if($entry.Length -gt 128MB){throw 'Preview archive entry exceeds its limit.'}
   $seen[$entry.FullName]=$entry;$total+=$entry.Length
  }
  if($total -gt 384MB -or !$seen.ContainsKey('SHA256SUMS.txt')){throw 'Preview archive size/manifest is invalid.'}
  $stream=$seen['SHA256SUMS.txt'].Open();$reader=New-Object IO.StreamReader($stream)
  try{$manifest=$reader.ReadToEnd()}finally{$reader.Dispose()}
  if($manifest.Length -gt 20000){throw 'Preview checksum manifest is oversized.'}
  $hashes=@{}
  foreach($line in ($manifest -split '\r?\n')){
   if(!$line.Trim()){continue}
   if($line -notmatch '^([0-9a-fA-F]{64})  ([A-Za-z0-9][A-Za-z0-9_. -]*)$' -or $hashes.ContainsKey($Matches[2])){throw 'Malformed preview checksum manifest.'}
   $hashes[$Matches[2]]=$Matches[1]
  }
  if($hashes.Count -ne $entries.Count-1){throw 'Preview checksums do not cover every packaged file.'}
  foreach($entry in $entries){
   if($entry.FullName -eq 'SHA256SUMS.txt'){continue}
   if(!$hashes.ContainsKey($entry.FullName)){throw 'Preview file lacks a checksum.'}
   $sha=[Security.Cryptography.SHA256]::Create();$inputStream=$entry.Open()
   try{$actual=([BitConverter]::ToString($sha.ComputeHash($inputStream))).Replace('-','')}finally{$inputStream.Dispose();$sha.Dispose()}
   if($actual -ne $hashes[$entry.FullName]){throw 'Preview archive checksum mismatch.'}
  }
  foreach($name in @('FishTank.exe','FishTank.Cli.exe')){
   if(!$seen.ContainsKey($name)){throw 'Both update executables are required.'}
   $inputStream=$seen[$name].Open();$outputStream=[IO.File]::Open((Join-Path $Output $name),[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
   try{$inputStream.CopyTo($outputStream);$outputStream.Flush($true)}finally{$outputStream.Dispose();$inputStream.Dispose()}
  }
 }finally{$archiveFile.Dispose()}
 $desktop=[Reflection.AssemblyName]::GetAssemblyName((Join-Path $Output 'FishTank.exe'))
 $cli=[Reflection.AssemblyName]::GetAssemblyName((Join-Path $Output 'FishTank.Cli.exe'))
 if($desktop.Name -ne 'FishTank' -or $cli.Name -ne 'FishTank.Cli' -or $desktop.Version -ne $cli.Version){throw 'Preview executable identities/versions differ.'}
 $version=$desktop.Version.ToString();$tag='preview-v'+$(if($desktop.Version.Revision -eq 0){$desktop.Version.ToString(3)}else{$version})
 Copy-Item -LiteralPath $Zip -Destination (Join-Path $Output 'FishTank-desktop-preview.zip')
 $lines=@('FishTank.exe','FishTank.Cli.exe','FishTank-desktop-preview.zip') | ForEach-Object {((Get-FileHash -LiteralPath (Join-Path $Output $_) -Algorithm SHA256).Hash.ToLowerInvariant())+'  '+$_}
 $lines | Set-Content -LiteralPath (Join-Path $Output 'SHA256SUMS.txt') -Encoding ASCII
 return [pscustomobject]@{Version=$version;Tag=$tag;Files=@('FishTank.exe','FishTank.Cli.exe','FishTank-desktop-preview.zip','SHA256SUMS.txt')}
}
if($PrepareOnly){if(!$Archive -or !$OutputDirectory){throw 'PrepareOnly requires archive and empty output directory.'};Prepare $Archive $OutputDirectory | ConvertTo-Json -Compress;exit 0}
if($Repository -ne 'KailanBess/FishTank' -or $Commit -notmatch '^[0-9a-f]{40}$' -or $RunId -notmatch '^[0-9]+$' -or !$env:GH_TOKEN){throw 'Publishing requires the expected repository, tested commit, run identity and scoped GitHub token.'}
$parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
$temporary=Join-Path $parent ('FishTank-preview-publish-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try{
 $download=Join-Path $temporary 'artifact';$output=Join-Path $temporary 'release'
 & gh run download $RunId --repo $Repository --name FishTank-unsigned-development --dir $download
 if($LASTEXITCODE -ne 0){throw 'Tested preview artifact download failed.'}
 $metadata=Prepare (Join-Path $download 'FishTank-desktop-preview.zip') $output
 $source=Get-Content -LiteralPath (Join-Path $PSScriptRoot '../desktop/Properties/AssemblyInfo.cs') -Raw
 if($source -notmatch 'AssemblyVersion\("([0-9.]+)"\)' -or [Version]$Matches[1] -ne [Version]$metadata.Version){throw 'Tested artifact does not match the checked-out source version.'}
 $list=& gh api "repos/$Repository/releases?per_page=100"
 if($LASTEXITCODE -ne 0){throw 'Existing release metadata could not be checked.'}
 $previous=@(($list | ConvertFrom-Json) | Where-Object tag_name -eq $metadata.Tag)
 if($previous.Count -gt 1){throw 'Ambiguous existing preview release.'}
 if($previous.Count -eq 1){
  if(!$previous[0].draft){
   Write-Host "Release $($metadata.Tag) already exists. Published assets were not changed; bump the app version for another release."
   exit 0
  }
  # Only recreate an unpublished draft produced by this publishing job. Never alter a public release or an owner's draft.
  if($previous[0].author.login -ne 'github-actions[bot]' -or !$previous[0].prerelease -or $previous[0].body -notlike 'Unsigned FishTank development preview*'){throw 'Existing draft is not a failed preview-publisher draft; review it manually.'}
  & gh api --method DELETE "repos/$Repository/releases/$($previous[0].id)"
  if($LASTEXITCODE -ne 0){throw 'Failed preview draft could not be recreated.'}
 }
 $notes=@"
Unsigned FishTank development preview $($metadata.Version).

Tested source commit: $Commit
Checks and provenance: https://github.com/$Repository/actions/runs/$RunId

For first installation, extract FishTank-desktop-preview.zip into a new folder. In Settings > GitHub updates, choose Use preview updates once; then check, download/verify, and apply newer releases. Preview updates verify GitHub SHA-256 digests, component identity and version. They do not verify a Windows publisher signature. Applying remains a separate confirmation, preserves user data/runtime configuration, and retains executable rollback copies.

Stable signed updates retain their separate publisher-verification requirements. No private app data or signing credentials are included.
"@
 $notesPath=Join-Path $temporary 'notes.txt';$notes | Set-Content -LiteralPath $notesPath -Encoding UTF8
 $assets=@($metadata.Files | ForEach-Object {Join-Path $output $_})
 & gh release create $metadata.Tag @assets --repo $Repository --target $Commit --draft --prerelease --title "FishTank $($metadata.Tag) preview" --notes-file $notesPath
 if($LASTEXITCODE -ne 0){throw 'Preview release draft creation failed.'}
 # A draft's tag may not have a Git ref yet; retrieve it by release ID rather than /releases/tags.
 $list=& gh api "repos/$Repository/releases?per_page=100"
 if($LASTEXITCODE -ne 0){throw 'Created draft metadata could not be read.'}
 $drafts=@(($list | ConvertFrom-Json) | Where-Object { $_.tag_name -eq $metadata.Tag -and $_.draft })
 if($drafts.Count -ne 1){throw 'Created preview draft identity is ambiguous.'}
 $releaseId=$drafts[0].id
 $ready=$false
 for($attempt=0;$attempt -lt 20;$attempt++){
  $json=& gh api "repos/$Repository/releases/$releaseId"
  if($LASTEXITCODE -ne 0){throw 'Preview asset metadata could not be checked.'}
  $release=$json | ConvertFrom-Json;$ready=$true
  foreach($name in $metadata.Files){
   $asset=@($release.assets | Where-Object name -eq $name)
   if($asset.Count -ne 1){throw 'Preview release asset identity is incomplete.'}
   if(!$asset[0].digest){$ready=$false;continue}
   $expected='sha256:'+(Get-FileHash -LiteralPath (Join-Path $output $name) -Algorithm SHA256).Hash.ToLowerInvariant()
   if($asset[0].digest -ne $expected){throw 'GitHub asset digest differs from the tested artifact.'}
  }
  if($ready){break};Start-Sleep -Seconds 3
 }
 if(!$ready){throw 'GitHub digest metadata did not become available; the release remains a draft.'}
 $publishPath=Join-Path $temporary 'publish.json'
 @{draft=$false;prerelease=$true;make_latest='false';tag_name=$metadata.Tag;target_commitish=$Commit} | ConvertTo-Json -Compress | Set-Content -LiteralPath $publishPath -Encoding UTF8
 & gh api --method PATCH "repos/$Repository/releases/$releaseId" --input $publishPath | Out-Null
 if($LASTEXITCODE -ne 0){throw 'Preview release publication failed.'}
 Write-Host "Published $($metadata.Tag) from tested commit $Commit with checked GitHub SHA-256 digests."
}finally{
 $full=[IO.Path]::GetFullPath($temporary)
 if(!$full.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($full) -notmatch '^FishTank-preview-publish-[0-9a-f]{32}$'){throw 'Refusing cleanup outside the generated publishing directory.'}
 Remove-Item -LiteralPath $full -Recurse -Force
}
