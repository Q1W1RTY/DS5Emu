[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$admin=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if(!$admin){$result=Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$PSCommandPath+'"')) -Wait -PassThru;exit $result.ExitCode}
$install=Join-Path $env:ProgramFiles 'DSEmu';$data=Join-Path $env:ProgramData 'DSEmu'
$service=Get-Service DSEmuBridge -ErrorAction SilentlyContinue
if($service){
 $serviceInfo=Get-CimInstance Win32_Service -Filter "Name='DSEmuBridge'"
 $serviceProcess=if($serviceInfo.ProcessId){Get-Process -Id $serviceInfo.ProcessId -ErrorAction SilentlyContinue}else{$null}
 try{
  Stop-Service DSEmuBridge;$service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))
  if($serviceProcess -and !$serviceProcess.WaitForExit(15000)){throw 'Service process did not release its files'}
 }finally{if($serviceProcess){$serviceProcess.Dispose()}}
}
foreach($uiProcess in @(Get-Process DSEmu -ErrorAction SilentlyContinue | Where-Object {$_.Path -and $_.Path.StartsWith($install+'\',[StringComparison]::OrdinalIgnoreCase)})){
 try{Stop-Process -InputObject $uiProcess -Force;if(!$uiProcess.WaitForExit(15000)){throw 'UI process did not release its files'}}finally{$uiProcess.Dispose()}
}
$env:VDS_HIDHIDE_CLI=$null
$repair=Join-Path $install 'engine\vdsd.exe'
if(Test-Path $repair){& $repair --repair-only --db-path (Join-Path $data 'bridge.db') --log (Join-Path $data 'bridge.log');if($LASTEXITCODE){throw 'HidHide recovery failed. Application files and recovery data have been retained.'}}
if($service){$service.Dispose();& sc.exe delete DSEmuBridge | Out-Null;if($LASTEXITCODE){throw 'Cannot remove DSEmu service'}}
$owner=(Get-ItemProperty 'HKLM:\SOFTWARE\DSEmu' -Name OwnerSid -ErrorAction SilentlyContinue).OwnerSid
if($owner){$run='Registry::HKEY_USERS\'+$owner+'\Software\Microsoft\Windows\CurrentVersion\Run';if(Test-Path $run){Remove-ItemProperty $run -Name DSEmu -ErrorAction SilentlyContinue}}
$shortcut=Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'DSEmu.lnk';if(Test-Path $shortcut){Remove-Item -LiteralPath $shortcut}
# Fixed application-owned roots are checked before recursive deletion. Shared drivers stay installed.
$resolved=[IO.Path]::GetFullPath($install)
if($resolved -ne ([IO.Path]::GetFullPath((Join-Path $env:ProgramFiles 'DSEmu')))){throw 'Unexpected uninstall root'}
if(Test-Path $resolved){
 if((Get-Item -LiteralPath $resolved).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Uninstall root is a reparse point'}
 if(Get-ChildItem -LiteralPath $resolved -Recurse -Force | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}){throw 'Unexpected reparse point inside the installation'}
}
if(Test-Path $resolved){
 $deadline=[DateTime]::UtcNow.AddSeconds(10)
 do {
  try{Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction Stop;break}
  catch [IO.IOException]{if([DateTime]::UtcNow -ge $deadline){throw};Start-Sleep -Milliseconds 200}
  catch [UnauthorizedAccessException]{if([DateTime]::UtcNow -ge $deadline){throw};Start-Sleep -Milliseconds 200}
 }while(Test-Path -LiteralPath $resolved)
}
Remove-Item 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DSEmu' -Recurse -ErrorAction SilentlyContinue
Remove-Item 'HKLM:\SOFTWARE\DSEmu' -Recurse -ErrorAction SilentlyContinue
Write-Host 'DSEmu removed. Shared USB/IP and HidHide drivers and local diagnostic data were retained.'
