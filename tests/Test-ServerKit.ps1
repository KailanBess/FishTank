$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root=Split-Path -Parent $PSScriptRoot
$temp=Join-Path ([IO.Path]::GetTempPath()) ('FishTank-server-kit-test-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
 $desktop=Join-Path $root 'packages/FishTank-desktop-preview.zip'
 $kit=Join-Path $root 'packages/FishTank-server-kit.zip'
 $publisher=Join-Path $root 'scripts/Publish-Preview.ps1'
 $result=(& $publisher -PrepareOnly -Archive $desktop -IncludeServer -ServerArchive $kit -OutputDirectory (Join-Path $temp 'valid')) | ConvertFrom-Json
 if($result.Files.Count -ne 5 -or $result.Files -notcontains 'FishTank-server-kit.zip'){throw 'Server asset missing from complete release.'}
 $checks=1
 foreach($case in @('unsafe','missing','tampered')){
  $copy=Join-Path $temp ($case+'.zip');Copy-Item -LiteralPath $kit -Destination $copy
  $zip=[IO.Compression.ZipFile]::Open($copy,[IO.Compression.ZipArchiveMode]::Update)
  try {
   if($case -eq 'missing'){$zip.GetEntry('server/install-debian.sh').Delete()}
   else {
    if($case -eq 'tampered'){$zip.GetEntry('server/server.py').Delete();$entry=$zip.CreateEntry('server/server.py')}
    else{$entry=$zip.CreateEntry('../environment')}
    $writer=New-Object IO.StreamWriter($entry.Open());try{$writer.Write('inert fixture')}finally{$writer.Dispose()}
   }
  }finally{$zip.Dispose()}
  $rejected=$false
  try{& $publisher -PrepareOnly -Archive $desktop -IncludeServer -ServerArchive $copy -OutputDirectory (Join-Path $temp $case) | Out-Null}catch{$rejected=$true}
  if(!$rejected){throw "Malformed server kit accepted: $case"};$checks++
 }
 Write-Host "$checks complete-release server-kit checks passed; nothing was deployed."
}finally{
 $full=[IO.Path]::GetFullPath($temp);$parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
 if(!$full.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($full) -notmatch '^FishTank-server-kit-test-[0-9a-f]{32}$'){throw 'Unsafe test cleanup path.'}
 Remove-Item -LiteralPath $temp -Recurse -Force
}
