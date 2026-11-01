param([ValidateRange(1,100)][int]$Cycles=10,[Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'qa-common.ps1')
function Read-Devices {
 @(Get-PnpDevice -PresentOnly | Where-Object {
  $_.InstanceId -like '*DSEmu-DualSense-Port-0*' -or
  ($_.Class -eq 'AudioEndpoint' -and $_.FriendlyName -like '*DualSense*')
 } | Select-Object Class,InstanceId,Status)
}
function Wait-Devices([bool]$Absent) {
 $until=[DateTime]::UtcNow.AddSeconds(15)
 do {
  $current=Read-Devices
  if($Absent -and !$current.Count){return @()}
  if(!$Absent -and $current.Count -eq $baseline.Count){
   $difference=@(Compare-Object @($baseline.InstanceId|Sort-Object) @($current.InstanceId|Sort-Object))
   if(!$difference.Count -and !@($current|Where-Object Status -ne 'OK').Count){return $current}
  }
  Start-Sleep -Milliseconds 300
 } while([DateTime]::UtcNow -lt $until)
 throw "Virtual device topology did not settle (absent=$Absent)"
}
$null=Wait-DSEmuState Active
$baseline=Read-Devices
$results=@()
try {
 for($i=1;$i -le $Cycles;++$i){
  $start=[DateTime]::UtcNow
  $null=Invoke-DSEmu pause;$null=Wait-DSEmuState Paused
  $null=Wait-Devices $true
  if(Test-Path "$env:ProgramData\DSEmu\bridge.db.hidhide-recovery.json"){throw "Cycle $i retained HidHide recovery journal"}
  $null=Invoke-DSEmu enable;$state=Wait-DSEmuState Active
  $devices=Wait-Devices $false
  $results+= [pscustomobject]@{cycle=$i;at=$start.ToString('o');elapsedSeconds=([DateTime]::UtcNow-$start).TotalSeconds;state=$state.state;devices=$devices}
  [pscustomobject]@{completed=$false;baseline=$baseline;cycles=$results}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $Output -Encoding utf8
 }
 [pscustomobject]@{completed=$true;baseline=$baseline;cycles=$results}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $Output -Encoding utf8
} catch {
 [pscustomobject]@{completed=$false;error=$_.Exception.Message;baseline=$baseline;cycles=$results}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $Output -Encoding utf8
 throw
}
