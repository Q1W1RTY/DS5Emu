[CmdletBinding()]
param([string]$Engine,[string]$Mock)
$ErrorActionPreference='Stop'
$run=Join-Path ([IO.Path]::GetTempPath()) ('dsemu-ipc-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $run | Out-Null
$database=Join-Path $run 'test.db'
Set-Content ($database+'.paused') 'paused'
Set-Content (Join-Path $run 'mock.txt') "--cloak-off`n--inv-off"
$name='DSEmu.Test.'+[Guid]::NewGuid().ToString('N')
$process=$null
$previousCli=$env:VDS_HIDHIDE_CLI
$previousState=$env:DSEMU_MOCK_HIDHIDE_STATE
function Request([string]$line,[switch]$Fragmented) {
    $pipe=[IO.Pipes.NamedPipeClientStream]::new('.',$name,[IO.Pipes.PipeDirection]::InOut,[IO.Pipes.PipeOptions]::Asynchronous)
    try {
        $pipe.Connect(3000)
        $bytes=[Text.Encoding]::UTF8.GetBytes($line+"`n")
        if($Fragmented) {
            for($i=0;$i -lt $bytes.Length;$i+=7) {$pipe.Write($bytes,$i,[Math]::Min(7,$bytes.Length-$i))}
        } else {$pipe.Write($bytes,0,$bytes.Length)}
        $pipe.Flush()
        $reader=[IO.StreamReader]::new($pipe,[Text.Encoding]::UTF8)
        $task=$reader.ReadLineAsync()
        if(!$task.Wait(4000)){throw 'Control reply timed out'}
        return ($task.Result | ConvertFrom-Json)
    } finally {$pipe.Dispose()}
}
try {
    $env:VDS_HIDHIDE_CLI=$Mock
    $env:DSEMU_MOCK_HIDHIDE_STATE=Join-Path $run 'mock.txt'
    $process=Start-Process $Engine -ArgumentList @('--db-path',('"'+$database+'"'),'--log',('"'+(Join-Path $run 'log.txt')+'"'),'--pipe',('"\\.\pipe\'+$name+'"')) -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $run 'stderr.txt')
    Start-Sleep -Milliseconds 1000
    $reply=Request '{"protocolVersion":1,"id":"one","command":"getState"}' -Fragmented
    if(!$reply.ok -or $reply.id -ne 'one' -or $reply.payload.state -ne 'Paused'){throw 'Invalid initial state'}
    $reply=Request '{"protocolVersion":2,"id":"two","command":"enable"}'
    if($reply.ok -or $reply.error -ne 'PROTOCOL_VERSION_MISMATCH'){throw 'Version mismatch accepted'}
    $reply=Request '{"protocolVersion":1,"id":"three","command":"pause"}'
    if(!$reply.ok -or $reply.payload.enabled){throw 'Pause failed'}
    $reply=Request '{"protocolVersion":1,"id":"four","command":"reconnect"}'
    if($reply.ok -or $reply.error -ne 'BRIDGE_PAUSED'){throw 'Reconnect bypassed pause'}
    $reply=Request '{"protocolVersion":1,"id":"five","command":"missing"}'
    if($reply.ok){throw 'Unknown command accepted'}
    $reply=Request '{bad json}'
    if($reply.OK -ne $false){throw 'Invalid JSON accepted'}
    $reply=Request '{"protocolVersion":1,"id":"disconnect-no-target","command":"disconnect"}'
    if($reply.ok -or $reply.error -ne 'CONTROLLER_NOT_CONFIGURED'){throw 'Disconnect accepted without an owned configured target'}
    $reply=Request '{"protocolVersion":1,"id":"after-disconnect-rejected","command":"getState"}'
    if(!$reply.ok -or $reply.payload.state -ne 'Paused' -or $reply.payload.bluetoothDisconnected){throw 'Rejected disconnect changed state'}
    if(Test-Path $database) {
        if((Get-Item $database).Length -gt 0){throw 'Read-only/paused tests captured a controller'}
    }
    Write-Host 'Control protocol tests passed (mock HidHide, no hardware capture)'
} finally {
    if($process -and !$process.HasExited){Stop-Process -Id $process.Id;$process.WaitForExit()}
    $env:VDS_HIDHIDE_CLI=$previousCli
    $env:DSEMU_MOCK_HIDHIDE_STATE=$previousState
}
