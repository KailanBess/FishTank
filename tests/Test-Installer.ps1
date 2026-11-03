$ErrorActionPreference='Stop'
$repository=Split-Path -Parent $PSScriptRoot
$parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixture=Join-Path $parent ('FishTank-installer-test-'+[Guid]::NewGuid().ToString('N'))
$previous=$env:LOCALAPPDATA
New-Item -ItemType Directory -Path $fixture | Out-Null
try{
 $source=Join-Path $fixture 'package';New-Item -ItemType Directory -Path $source | Out-Null
 Copy-Item -LiteralPath (Join-Path $repository 'desktop/bin/Release/net48/FishTank.exe') -Destination $source
 Copy-Item -LiteralPath (Join-Path $repository 'desktop/bin/Release/cli/net48/FishTank.Cli.exe') -Destination $source
 foreach($name in @('Install-FishTank.ps1','Uninstall-FishTank.ps1')){Copy-Item -LiteralPath (Join-Path $repository ('scripts/'+$name)) -Destination $source}
 $checks=Get-ChildItem -LiteralPath $source -File | ForEach-Object{(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash+'  '+$_.Name}
 $checks | Set-Content -LiteralPath (Join-Path $source 'SHA256SUMS.txt') -Encoding ASCII
 $env:LOCALAPPDATA=Join-Path $fixture 'local'
 $profile=Join-Path $env:LOCALAPPDATA 'FishTank';New-Item -ItemType Directory -Path $profile -Force | Out-Null
 'fixture vault retained' | Set-Content -LiteralPath (Join-Path $profile 'passwords.ftvault')
 & (Join-Path $source 'Install-FishTank.ps1') -AllowUnsignedPreview -NoRegistration
 $installed=Join-Path $env:LOCALAPPDATA 'Programs/FishTank'
 if(!(Test-Path -LiteralPath (Join-Path $installed 'FishTank-install.json'))){throw 'Install ownership marker missing.'}
 'unrelated fixture' | Set-Content -LiteralPath (Join-Path $installed 'keep-me.txt')
 & (Join-Path $installed 'Uninstall-FishTank.ps1') -FilesOnly
 if(Test-Path -LiteralPath (Join-Path $installed 'FishTank.exe')){throw 'Owned program file retained.'}
 if(!(Test-Path -LiteralPath (Join-Path $installed 'keep-me.txt')) -or !(Test-Path -LiteralPath (Join-Path $profile 'passwords.ftvault'))){throw 'Uninstall deleted a profile or unrelated file.'}
 Write-Host 'Installer and uninstaller file operations passed in an isolated directory. Registry registration and publisher signing were not exercised.'
}finally{
 $env:LOCALAPPDATA=$previous
 $resolved=[IO.Path]::GetFullPath($fixture)
 if(!$resolved.StartsWith($parent.TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'FishTank-installer-test-*'){throw 'Refused unsafe fixture cleanup.'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
