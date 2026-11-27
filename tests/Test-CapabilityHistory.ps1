$ErrorActionPreference='Stop'
$path=Join-Path $PSScriptRoot '../desktop/FishTank.Collect.ps1'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile([IO.Path]::GetFullPath($path),[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Collector PowerShell syntax errors.'}
$definition=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Read-CapabilityAccessTimes'},$true)
if($null -eq $definition){throw 'Missing privacy history parser.'}
. ([scriptblock]::Create($definition.Extent.Text))
$checks=0
function Assert($value,$name){if(!$value){throw $name};$script:checks++}
$start=[DateTime]::SpecifyKind([DateTime]'2026-01-01T10:00:00',[DateTimeKind]::Utc).ToFileTimeUtc()
$stop=$start+[TimeSpan]::FromMinutes(2).Ticks
$closed=Read-CapabilityAccessTimes ([pscustomobject]@{LastUsedTimeStart=$start;LastUsedTimeStop=$stop})
Assert ($closed.HistoryState -eq 'Closed Windows record') 'Closed interval classification'
Assert ($closed.LastUsedStartUtc -eq '2026-01-01T10:00:00.0000000Z' -and $closed.LastUsedStopUtc -eq '2026-01-01T10:02:00.0000000Z') 'UTC conversion'
$open=Read-CapabilityAccessTimes ([pscustomobject]@{LastUsedTimeStart=$start;LastUsedTimeStop=0})
Assert ($open.HistoryState -eq 'Open Windows record; not live-use proof' -and $null -eq $open.LastUsedStopUtc) 'Open metadata is not live capture'
$empty=Read-CapabilityAccessTimes ([pscustomobject]@{LastUsedTimeStart=0;LastUsedTimeStop=0})
Assert ($empty.HistoryState -eq 'No recorded interval') 'Empty metadata'
foreach($record in @([pscustomobject]@{},[pscustomobject]@{LastUsedTimeStart=-1;LastUsedTimeStop=0},[pscustomobject]@{LastUsedTimeStart=[long]::MaxValue;LastUsedTimeStop=0},[pscustomobject]@{LastUsedTimeStart='invalid';LastUsedTimeStop=0},[pscustomobject]@{LastUsedTimeStart=$null;LastUsedTimeStop=0},[pscustomobject]@{LastUsedTimeStart='0';LastUsedTimeStop='0'})){
 $result=Read-CapabilityAccessTimes $record
 Assert ($result.HistoryState -eq 'Unknown' -and $null -eq $result.LastUsedStartUtc) 'Unavailable metadata must remain unknown'
}
$inconsistent=Read-CapabilityAccessTimes ([pscustomobject]@{LastUsedTimeStart=$stop;LastUsedTimeStop=$start})
Assert ($inconsistent.HistoryState -eq 'Inconsistent record') 'Reversed interval'
Write-Host "$checks capability-history fixture checks passed. No registry, sensor or user data was accessed."
