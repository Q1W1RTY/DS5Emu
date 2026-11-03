param([ValidateRange(1,86400)][int]$Seconds=300,[Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'qa-common.ps1')
$cores=(Get-CimInstance Win32_ComputerSystem).NumberOfLogicalProcessors
$previous=@{};$last=[DateTime]::UtcNow;$deadline=$last.AddSeconds($Seconds);$i=0
do {
 $now=[DateTime]::UtcNow;$elapsed=($now-$last).TotalSeconds;$totalCpu=0;$working=0;$privateBytes=0;$privateWorking=0
 # CIM exposes service CPU counters to the ordinary user as well. Get-Process
 # TotalProcessorTime can be null for LocalSystem, silently undercounting CPU.
 $processes=@(Get-CimInstance Win32_Process -Filter "Name='DS5Link.exe' OR Name='DS5Link.exe' OR Name='vdsd.exe'")
 foreach($process in $processes){
  $cpu=([double]$process.UserModeTime+[double]$process.KernelModeTime)/10000000
  $key=[string]$process.ProcessId+'|'+$process.CreationDate.ToString('o')
  if($previous.ContainsKey($key) -and $elapsed -gt 0){$totalCpu+=100*($cpu-$previous[$key])/$elapsed/$cores}
  $previous[$key]=$cpu;$working+=[long]$process.WorkingSetSize;$privateBytes+=[long]$process.PrivatePageCount
 }
 $ids=@($processes.ProcessId)
 foreach($perf in (Get-CimInstance Win32_PerfFormattedData_PerfProc_Process -Filter "Name='DS5Link' OR Name='vdsd'")){
  if($perf.IDProcess -in $ids){$privateWorking+=$perf.WorkingSetPrivate}
 }
 try{$state=Invoke-DS5Link;$mode=$state.state;$audio=$state.audioActive}catch{$mode='Unavailable';$audio=$false}
 # Explicit invariant decimal points make PowerShell 5/7 and ru-RU CSV agree.
 [pscustomobject]@{at=$now.ToString('o');sample=$i;state=$mode;audio=$audio;cpuPercent=if($i){([double]$totalCpu).ToString('R',[Globalization.CultureInfo]::InvariantCulture)}else{''};privateWorkingSet=$privateWorking;workingSet=$working;privateBytes=$privateBytes;processCount=$processes.Count}|Export-Csv -LiteralPath $Output -Append -NoTypeInformation -Encoding utf8
 $last=$now;++$i;if([DateTime]::UtcNow -lt $deadline){Start-Sleep -Seconds 1}
} while([DateTime]::UtcNow -lt $deadline)
