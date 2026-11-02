$ErrorActionPreference='Stop'
$files=Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '..\desktop') -Filter '*.ps1'
$failed=$false
foreach($file in $files){
 $tokens=$null;$parseErrors=$null
 [void][Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$parseErrors)
 if($parseErrors.Count -gt 0){$parseErrors | ForEach-Object {Write-Host "$($file.Name): $($_.Message)"};$failed=$true}
}
if($failed){throw 'PowerShell resource syntax check failed.'}
Write-Host "Parsed $($files.Count) PowerShell resources successfully. No collector was executed."
