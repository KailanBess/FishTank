param([string]$Archive=(Join-Path $PSScriptRoot '../packages/FishTank-desktop-preview.zip'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$script=Join-Path $PSScriptRoot '../scripts/Publish-Preview.ps1'
$parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
$temporary=Join-Path $parent ('FishTank-preview-test-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
$checks=0
function Assert($Condition,[string]$Name){if(!$Condition){throw $Name};$script:checks++}
try{
 $before=(Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash
 $output=Join-Path $temporary 'valid'
 $prepared=(& $script -PrepareOnly -Archive $Archive -OutputDirectory $output) | ConvertFrom-Json
 Assert ($prepared.Files.Count -eq 4 -and $prepared.Tag -match '^preview-v[0-9.]+$') 'Preview release metadata missing'
 foreach($name in @('FishTank.exe','FishTank.Cli.exe','FishTank-desktop-preview.zip','SHA256SUMS.txt')){Assert (Test-Path -LiteralPath (Join-Path $output $name)) 'Preview release component missing'}
 foreach($line in (Get-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt'))){$parts=$line -split '  ',2;Assert ((Get-FileHash -LiteralPath (Join-Path $output $parts[1]) -Algorithm SHA256).Hash -eq $parts[0]) 'Preview release digest mismatch'}
 $rejected=$false;try{& $script -PrepareOnly -Archive $Archive -OutputDirectory $output | Out-Null}catch{$rejected=$true};Assert $rejected 'Publishing preparation overwrote existing output'
 foreach($case in @('unsafe-path','duplicate-entry','bad-checksum')){
  $copy=Join-Path $temporary ($case+'.zip');Copy-Item -LiteralPath $Archive -Destination $copy
  $zip=[IO.Compression.ZipFile]::Open($copy,[IO.Compression.ZipArchiveMode]::Update)
  try{
   if($case -eq 'bad-checksum'){$zip.GetEntry('README.txt').Delete();$entry=$zip.CreateEntry('README.txt')}
   elseif($case -eq 'duplicate-entry'){$entry=$zip.CreateEntry('FishTank.exe')}
   else{$entry=$zip.CreateEntry('../escape.txt')}
   $writer=New-Object IO.StreamWriter($entry.Open());try{$writer.Write('Inert malformed archive fixture')}finally{$writer.Dispose()}
  }finally{$zip.Dispose()}
  $rejected=$false;try{& $script -PrepareOnly -Archive $copy -OutputDirectory (Join-Path $temporary $case) | Out-Null}catch{$rejected=$true}
  Assert $rejected "Malformed preview release accepted: $case"
 }
 Assert ((Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash -eq $before) 'Release preparation changed original archive'
 Assert (!(Test-Path -LiteralPath (Join-Path $temporary 'escape.txt'))) 'Archive path escaped its output'
 Write-Host "$checks preview-release preparation checks passed; GitHub was not contacted."
}finally{
 $full=[IO.Path]::GetFullPath($temporary)
 if(!$full.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($full) -notmatch '^FishTank-preview-test-[0-9a-f]{32}$'){throw 'Refusing cleanup outside the generated release-test directory.'}
 Remove-Item -LiteralPath $full -Recurse -Force
}
exit 0
