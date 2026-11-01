[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$destination = Join-Path $root ('artifacts\baseline\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $destination -Force | Out-Null
# Raw hardware identifiers stay in ignored local artifacts, never in exported docs.
$cli = "$env:ProgramFiles\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe"
if (Test-Path $cli) {
    $state = & $cli --cloak-state --inv-state --app-list --dev-list
    if ($LASTEXITCODE) { throw 'Cannot capture HidHide baseline' }
    $state | Set-Content (Join-Path $destination 'hidhide.txt') -Encoding utf8
}
Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,BuildNumber,OSArchitecture | ConvertTo-Json | Set-Content (Join-Path $destination 'windows.json')
Get-PnpDevice | Where-Object { $_.InstanceId -match '054C|USBIP' -or $_.FriendlyName -match 'Bluetooth|HidHide' } | Select-Object Class,FriendlyName,InstanceId,Status | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $destination 'devices.json')
Get-CimInstance Win32_PnPSignedDriver | Where-Object { $_.DeviceName -match 'Bluetooth|HidHide|USB/IP' } | Select-Object DeviceName,DriverVersion,InfName,IsSigned,Signer | ConvertTo-Json | Set-Content (Join-Path $destination 'drivers.json')
Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio' -Recurse | ForEach-Object {
    $values = @{}
    foreach ($name in $_.GetValueNames()) { $values[$name] = $_.GetValue($name) }
    [pscustomobject]@{Path=$_.Name;Values=$values}
} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $destination 'audio-registry.json')
Write-Host "Baseline saved locally: $destination"
