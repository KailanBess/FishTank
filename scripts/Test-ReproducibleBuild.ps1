$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$project=Join-Path $root 'desktop/FishTank.csproj'
$binary=Join-Path $root 'desktop/bin/Release/net48/FishTank.exe'
& dotnet build $project -c Release -t:Rebuild
if($LASTEXITCODE -ne 0){throw 'First deterministic build failed.'}
$first=(Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash
& dotnet build $project -c Release -t:Rebuild
if($LASTEXITCODE -ne 0){throw 'Second deterministic build failed.'}
$second=(Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash
if($first -ne $second){throw 'Repeated unsigned executable builds differ. Do not claim reproducibility.'}
Write-Host ('Repeated unsigned executable SHA-256 matched: '+$first+'. Signing timestamps and ZIP timestamps are excluded.')
