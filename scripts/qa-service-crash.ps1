param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'qa-common.ps1')
$result=Join-Path $OutputDirectory 'service-crash.json'
try {
 $principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
 if(!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Administrative token required'}
 $before=Wait-DSEmuState Active
 $baseline=@(Get-PnpDevice -PresentOnly | Where-Object {$_.InstanceId -like '*DSEmu-DualSense-Port-0*' -or ($_.Class -eq 'AudioEndpoint' -and $_.FriendlyName -like '*DualSense*')}|Select-Object Class,InstanceId,Status)
 if(@($baseline|Where-Object Class -eq 'USB').Count -ne 1 -or @($baseline|Where-Object Class -eq 'AudioEndpoint').Count -ne 2){throw 'Unexpected baseline virtual topology'}
 $service=Get-CimInstance Win32_Service -Filter "Name='DSEmuBridge'"
 $process=Get-CimInstance Win32_Process -Filter "ProcessId=$($service.ProcessId)"
 $expected=Join-Path $env:ProgramFiles 'DSEmu\engine\vdsd.exe'
 if($service.State -ne 'Running' -or $process.ExecutablePath -ne $expected -or $process.SessionId -ne 0){throw 'Unexpected service process; nothing terminated'}
 $oldId=$service.ProcessId
 $journal=Join-Path $env:ProgramData 'DSEmu\bridge.db.hidhide-recovery.json'
 if(!(Test-Path $journal)){throw 'Active bridge must have a recovery journal'}
 $started=[DateTime]::UtcNow
 Stop-Process -Id $oldId -Force
 $journalAfterCrash=Test-Path $journal
 $deadline=$started.AddSeconds(90);$newId=0
 do {
  Start-Sleep -Milliseconds 250
  $current=Get-CimInstance Win32_Service -Filter "Name='DSEmuBridge'"
  if($current.State -eq 'Running' -and $current.ProcessId -ne $oldId){$newId=$current.ProcessId;break}
 } while([DateTime]::UtcNow -lt $deadline)
 if(!$newId){throw 'SCM did not restart the service'}
 $after=Wait-DSEmuState Active -Seconds 45
 $pnpDeadline=[DateTime]::UtcNow.AddSeconds(15);$settled=$false
 do {
  $devices=@(Get-PnpDevice -PresentOnly | Where-Object {$_.InstanceId -like '*DSEmu-DualSense-Port-0*' -or ($_.Class -eq 'AudioEndpoint' -and $_.FriendlyName -like '*DualSense*')}|Select-Object Class,InstanceId,Status)
  $difference=@(Compare-Object @($baseline.InstanceId|Sort-Object) @($devices.InstanceId|Sort-Object))
  if(!$difference.Count -and !@($devices|Where-Object Status -ne 'OK').Count){$settled=$true;break}
  Start-Sleep -Milliseconds 300
 } while([DateTime]::UtcNow -lt $pnpDeadline)
 if(!$settled){throw 'Unexpected recovered virtual topology after PnP settle timeout'}
 [pscustomobject]@{ok=$true;at=$started.ToString('o');oldPid=$oldId;newPid=$newId;elapsedSeconds=([DateTime]::UtcNow-$started).TotalSeconds;journalSurvivedCrash=$journalAfterCrash;journalPresentAfterRecovery=(Test-Path $journal);before=$before;after=$after;devices=$devices}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $result -Encoding utf8
} catch {
 [pscustomobject]@{ok=$false;error=$_.Exception.Message;at=[DateTime]::UtcNow.ToString('o');oldPid=$oldId;newPid=$newId;devices=$devices}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $result -Encoding utf8
 exit 1
}
