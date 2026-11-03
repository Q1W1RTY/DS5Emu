[CmdletBinding()]
param([ValidateRange(5,120)][int]$Seconds=20, [switch]$HidTest, [switch]$MicTest)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$bin=Join-Path $root 'artifacts\native-Release'
$run=Join-Path $root ('artifacts\smoke\'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory $run -Force | Out-Null
$cli="$env:ProgramFiles\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe"
$daemon=Join-Path $bin 'vdsd.exe'
$control=Join-Path $bin 'vdsctl.exe'
$process=$null
$address=$null
$probe=Join-Path $bin 'ds5link_hardware_probe.exe'
if(Test-Path $probe) { & $probe | Set-Content (Join-Path $run 'audio-before.txt') }
if (Get-Process vdsd,DSX -ErrorAction SilentlyContinue) { throw 'Close the existing controller owner before the smoke run.' }
$baseline=@(& $cli --cloak-state --inv-state --app-list --dev-list)
if ($LASTEXITCODE) { throw 'Cannot read HidHide baseline' }
$baseline | Set-Content (Join-Path $run 'hidhide-before.txt')
$alreadyRegistered=($baseline -join "`n").Contains('--app-reg "'+$daemon+'"')
try {
    $process=Start-Process $daemon -ArgumentList @('--db-path',('"'+(Join-Path $run 'vdsd.db')+'"'),'--log',('"'+(Join-Path $run 'vdsd.log')+'"')) -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $run 'stderr.txt')
    Start-Sleep 2
    if ($process.HasExited) { throw 'Daemon exited; inspect stderr/log.' }
    $targets=& $control list-targets
    $targets | Set-Content (Join-Path $run 'targets.jsonl')
    # Device address comes from the connected physical PnP instance, not a user constant.
    $device=Get-PnpDevice -PresentOnly | Where-Object { $_.InstanceId -match 'BTHENUM.*VID&0002054C_PID&0CE6.*&([0-9A-F]{12})_' } | Select-Object -First 1
    if (!$device -or $device.InstanceId -notmatch '&([0-9A-F]{12})_') { throw 'No connected ordinary DualSense found.' }
    $address=([regex]::Matches($Matches[1],'..') | ForEach-Object {$_.Value}) -join ':'
    $reply=& $control attach $address --profile ds5 --ports 0
    $reply | Set-Content (Join-Path $run 'attach.jsonl')
    if ($LASTEXITCODE) { throw 'Attach request failed.' }
    if (!(($reply | ConvertFrom-Json).OK)) { throw 'Attach was rejected by daemon.' }
    Start-Sleep 5
    if(Test-Path $probe) {
        $mode=if($MicTest){'--mic-test'}else{'--audio-test'}
        & $probe $mode 2>&1 | Set-Content (Join-Path $run 'audio-active.txt')
        if($LASTEXITCODE) {throw 'Audio hardware test failed'}
    }
    if($HidTest) {
        & (Join-Path $bin 'ds5link_hid_probe.exe') 2>&1 | Set-Content (Join-Path $run 'hid-test.txt')
        if($LASTEXITCODE) {throw 'HID hardware test failed'}
    }
    Start-Sleep $Seconds
    & $control list | Set-Content (Join-Path $run 'state.jsonl')
    & "$env:ProgramFiles\USBip\usbip.exe" port | Set-Content (Join-Path $run 'usbip-port.txt')
    Get-PnpDevice -PresentOnly | Where-Object { $_.InstanceId -match '054C' -or $_.Class -eq 'AudioEndpoint' } | Select-Object Class,FriendlyName,InstanceId,Status | ConvertTo-Json | Set-Content (Join-Path $run 'devices-active.json')
} catch {
    $_ | Out-String | Set-Content (Join-Path $run 'failure.txt')
} finally {
    if ($process -and !$process.HasExited) {
        if ($address) { & $control detach $address | Set-Content (Join-Path $run 'detach.jsonl'); Start-Sleep 3 }
        # This initial smoke harness stops only its own daemon after detach.
        Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
        $process.WaitForExit(5000) | Out-Null
    }
    if (!$alreadyRegistered) { & $cli --app-unreg $daemon }
    if ($baseline -contains '--cloak-off') { & $cli --cloak-off }
    & $cli --cloak-state --inv-state --app-list --dev-list | Set-Content (Join-Path $run 'hidhide-after.txt')
    if(Test-Path $probe) { & $probe | Set-Content (Join-Path $run 'audio-after.txt') }
    'finished' | Set-Content (Join-Path $run 'finished.txt')
}
