param([switch]$FilesOnly)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($PSScriptRoot)
$expected=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs/FishTank'))
if($root -ne $expected){throw 'Run this uninstaller only from its managed FishTank installation.'}
$marker=Join-Path $root 'FishTank-install.json'
$record=Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
if($record.Owner -ne 'FishTank' -or $record.Version -ne 1){throw 'Installation ownership marker is invalid.'}
if(Get-Process -Name FishTank,FishTank.Cli -ErrorAction SilentlyContinue){throw 'Close FishTank windows and CLI operations before uninstalling.'}
$owned=@()
foreach($property in $record.Files.PSObject.Properties){
 if($property.Name -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,120}$' -or $property.Value -notmatch '^[A-Fa-f0-9]{64}$'){throw 'Unsafe installed-file record.'}
 $file=Join-Path $root $property.Name
 if(Test-Path -LiteralPath $file){
  if((Get-Item -LiteralPath $file).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Installed links require manual review.'}
  # In-app updates replace binaries. Verify ownership by exact filename; preserve other changed files for review.
  if($property.Name -notin @('FishTank.exe','FishTank.Cli.exe') -and (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $property.Value){throw ('Changed installed file requires manual review: '+$property.Name)}
  $owned+=$file
 }
}
foreach($file in $owned){Remove-Item -LiteralPath $file -Force}
Remove-Item -LiteralPath $marker -Force
$key='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\FishTank'
if(!$FilesOnly){
if((Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue).InstallLocation -eq $root){Remove-Item -LiteralPath $key -Recurse}
foreach($path in @('Software\Classes\*\shell\FishTank.Scan','Software\Classes\Directory\shell\FishTank.Scan')){
 $ownedKey=$false
 $entry=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($path)
 try{if($entry -and $entry.GetValue('Owner') -eq 'FishTank'){$command=$entry.OpenSubKey('command');try{$ownedKey=$command -and ([string]$command.GetValue('')).StartsWith('"'+(Join-Path $root 'FishTank.exe')+'"',[StringComparison]::OrdinalIgnoreCase)}finally{if($command){$command.Dispose()}}}}finally{if($entry){$entry.Dispose()}}
 if($ownedKey){[Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($path,$false)}
}
}
if(@(Get-ChildItem -LiteralPath $root -Force).Count -eq 0){Remove-Item -LiteralPath $root}
Write-Host 'FishTank program files removed. User profiles, vaults, backups and unrelated files were retained.'
