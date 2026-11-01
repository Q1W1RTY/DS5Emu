function Invoke-DSEmu([ValidateSet('hello','getState','enable','pause','reconnect','disconnect','getDiagnostics')][string]$Command='getState') {
 $pipe=[IO.Pipes.NamedPipeClientStream]::new('.','DSEmu.Control.v1',[IO.Pipes.PipeDirection]::InOut,[IO.Pipes.PipeOptions]::Asynchronous)
 try {
  $pipe.Connect(4000)
  $id=[Guid]::NewGuid().ToString('N')
  $request=@{protocolVersion=1;id=$id;command=$Command}|ConvertTo-Json -Compress
  $bytes=[Text.Encoding]::UTF8.GetBytes($request+"`n");$pipe.Write($bytes,0,$bytes.Length);$pipe.Flush()
  $reader=[IO.StreamReader]::new($pipe,[Text.Encoding]::UTF8)
  $task=$reader.ReadLineAsync();if(!$task.Wait(5000)){throw 'DSEmu control timeout'}
  $reply=$task.Result|ConvertFrom-Json
  if(!$reply.ok -or $reply.id -ne $id){throw ('DSEmu control failed: '+$reply.error)}
  return $reply.payload
 } finally {$pipe.Dispose()}
}
function Wait-DSEmuState([string]$State,[int]$Seconds=20) {
 $until=[DateTime]::UtcNow.AddSeconds($Seconds)
 do {$current=Invoke-DSEmu;if($current.state -eq $State){return $current};Start-Sleep -Milliseconds 200}while([DateTime]::UtcNow -lt $until)
 throw "Expected $State, received $($current.state): $($current.error)"
}
