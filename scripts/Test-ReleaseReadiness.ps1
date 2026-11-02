param([Parameter(Mandatory=$true)][string]$Tag,[switch]$RequireSigningConfiguration)
$ErrorActionPreference='Stop'
if($Tag -notmatch '^v[0-9]+\.[0-9]+(?:\.[0-9]+){0,2}$'){throw 'Use a stable numeric version tag.'}
$version=[Version]$Tag.Substring(1)
$normalized=[Version]::new($version.Major,$version.Minor,[Math]::Max(0,$version.Build),[Math]::Max(0,$version.Revision))
$root=Split-Path -Parent $PSScriptRoot
$source=Get-Content -LiteralPath (Join-Path $root 'desktop/Properties/AssemblyInfo.cs') -Raw
if($source -notmatch 'AssemblyVersion\("([0-9.]+)"\)' -or [Version]$Matches[1] -ne $normalized){throw 'Release tag must match the source AssemblyVersion.'}
if($RequireSigningConfiguration){
 $missing=@()
 if(!$env:FISHTANK_SIGNING_PFX_BASE64){$missing+='WINDOWS_SIGNING_PFX_BASE64 secret'}
 if(!$env:FISHTANK_SIGNING_PASSWORD){$missing+='WINDOWS_SIGNING_PASSWORD secret'}
 if(!$env:FISHTANK_TIMESTAMP_URL){$missing+='WINDOWS_TIMESTAMP_URL variable'}
 if($missing.Count){throw ('Release environment is missing: '+($missing -join ', ')+'. Configure these in GitHub encrypted environment settings; do not commit credentials.')}
 $timestamp=[Uri]$env:FISHTANK_TIMESTAMP_URL
 if(!$timestamp.IsAbsoluteUri -or $timestamp.Scheme -ne 'https' -or $timestamp.UserInfo -ne '' -or $timestamp.Query -ne '' -or $timestamp.Fragment -ne ''){throw 'Use a direct HTTPS timestamp endpoint without credentials, query or fragment.'}
}
Write-Host "Release source is ready for $Tag. No signing credentials were printed."
