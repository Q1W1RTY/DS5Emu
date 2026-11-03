param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'qa-common.ps1')
$results=@();$silent=$null;$drip=$null;$service=$null
function Query-Usbip {
 $client=[Net.Sockets.TcpClient]::new();try{
  $client.Connect('127.0.0.1',3240);$stream=$client.GetStream();$stream.ReadTimeout=4000
  $header=[byte[]](1,17,128,5,0,0,0,0);$stream.Write($header,0,8)
  $reply=[byte[]]::new(8);$offset=0;while($offset -lt 8){$got=$stream.Read($reply,$offset,8-$offset);if(!$got){throw 'USB/IP closed valid query'};$offset+=$got}
  if($reply[0] -ne 1 -or $reply[1] -ne 17 -or $reply[2] -ne 0 -or $reply[3] -ne 5){throw 'Invalid USB/IP devlist reply'}
 }finally{$client.Dispose()}
}
try{
 $null=Wait-DS5LinkState Active
 $silent=[Net.Sockets.TcpClient]::new();$silent.Connect('127.0.0.1',3240)
 $timer=[Diagnostics.Stopwatch]::StartNew();Query-Usbip
 if($timer.Elapsed.TotalSeconds -gt 3){throw 'Silent client blocked valid client'}
 $results+=@{phase='silent-client-followed-by-valid-query';seconds=$timer.Elapsed.TotalSeconds};$silent.Dispose();$silent=$null
 $drip=[Net.Sockets.TcpClient]::new();$drip.Connect('127.0.0.1',3240);$stream=$drip.GetStream()
 foreach($value in [byte[]](1,17,128)){$stream.WriteByte($value);Start-Sleep -Milliseconds 350}
 Query-Usbip;$results+=@{phase='partial-drip-followed-by-valid-query';ok=$true};$drip.Dispose();$drip=$null
 $silent=[Net.Sockets.TcpClient]::new();$silent.Connect('127.0.0.1',3240);Start-Sleep -Milliseconds 50
 $service=Get-Service DS5LinkBridge;$timer.Restart();$service.Stop();$service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(10))
 $results+=@{phase='normal-service-stop-during-handshake';seconds=$timer.Elapsed.TotalSeconds}
 if(Test-Path "$env:ProgramData\DS5Link\bridge.db.hidhide-recovery.json"){throw 'Normal stop retained recovery journal'}
 $silent.Dispose();$silent=$null;Start-Service DS5LinkBridge;$null=Wait-DS5LinkState Active -Seconds 30;Query-Usbip
 @{ok=$true;steps=$results}|ConvertTo-Json -Depth 6|Set-Content (Join-Path $OutputDirectory 'usbip-handshake.json') -Encoding utf8
}catch{
 @{ok=$false;error=$_.Exception.Message;steps=$results}|ConvertTo-Json -Depth 6|Set-Content (Join-Path $OutputDirectory 'usbip-handshake.json') -Encoding utf8
 throw
}finally{
 if($silent){$silent.Dispose()};if($drip){$drip.Dispose()}
 if($service){$service.Refresh();if($service.Status -eq 'Stopped'){Start-Service DS5LinkBridge};$service.Dispose()}
}
