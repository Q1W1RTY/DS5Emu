[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$OwnerSid)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$log=Join-Path $root 'artifacts\installation.log'
$result=Join-Path $root 'artifacts\installation-result.json'
Start-Transcript -Path $log -Force | Out-Null
try {
 $package=(Get-Content (Join-Path $root 'artifacts\packages\LATEST.txt') -Raw).Trim() -replace '\.zip$',''
 & (Join-Path $package 'Install.ps1') -OwnerSid $OwnerSid
 @{ok=$true;finished=(Get-Date).ToString('o')} | ConvertTo-Json | Set-Content $result -Encoding utf8
} catch {
 @{ok=$false;error=$_.Exception.Message;finished=(Get-Date).ToString('o')} | ConvertTo-Json | Set-Content $result -Encoding utf8
 $_ | Format-List * -Force | Out-String | Write-Output
 Stop-Transcript | Out-Null
 exit 1
}
Stop-Transcript | Out-Null
