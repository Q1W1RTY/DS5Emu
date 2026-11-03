[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'InstallPaths.ps1')
$registered=Get-DS5RegisteredInstall
if(!$registered){throw 'Не найдена зарегистрированная папка DS5Link. Файлы не удалены.'}
$install=Resolve-DS5InstallPath $registered
if(!([IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')).Equals($install,[StringComparison]::OrdinalIgnoreCase)){throw 'Запускайте удаление из зарегистрированной папки DS5Link. Файлы не удалены.'}
Assert-DS5InstallTarget $install
. (Join-Path $PSScriptRoot 'OwnedFiles.ps1')
$removalPlan=Get-AppOwnedRemovalPlan $install
$admin=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if(!$admin){$result=Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$PSCommandPath+'"')) -Wait -PassThru;exit $result.ExitCode}
$data=Join-Path $env:ProgramData 'DS5Link'
$transactionLock=[Threading.Mutex]::new($false,'Global\DS5Link.Install.Transaction')
try{$ownsTransaction=$transactionLock.WaitOne(0)}catch [Threading.AbandonedMutexException]{$ownsTransaction=$true}
if(!$ownsTransaction){throw 'Another DS5Link installation or removal is running.'}
$service=Get-Service DS5LinkBridge -ErrorAction SilentlyContinue
if($service){
 $serviceInfo=Get-CimInstance Win32_Service -Filter "Name='DS5LinkBridge'"
 if(!$serviceInfo.PathName.StartsWith(('"'+(Join-Path $install 'engine\vdsd.exe')+'"'),[StringComparison]::OrdinalIgnoreCase)){throw 'Путь службы DS5Link не совпадает с папкой установки. Файлы не удалены.'}
 $serviceProcess=if($serviceInfo.ProcessId){Get-Process -Id $serviceInfo.ProcessId -ErrorAction SilentlyContinue}else{$null}
 try{
  Stop-Service DS5LinkBridge;$service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))
  if($serviceProcess -and !$serviceProcess.WaitForExit(15000)){throw 'Service process did not release its files'}
 }finally{if($serviceProcess){$serviceProcess.Dispose()}}
}
foreach($uiProcess in @(Get-Process DS5Link,DS5Link -ErrorAction SilentlyContinue | Where-Object {$_.Path -and $_.Path.StartsWith($install+'\',[StringComparison]::OrdinalIgnoreCase)})){
 try{Stop-Process -InputObject $uiProcess -Force;if(!$uiProcess.WaitForExit(15000)){throw 'UI process did not release its files'}}finally{$uiProcess.Dispose()}
}
$env:VDS_HIDHIDE_CLI=$null
$repair=Join-Path $install 'engine\vdsd.exe'
if(Test-Path -LiteralPath $repair){& $repair --repair-only --db-path (Join-Path $data 'bridge.db') --log (Join-Path $data 'bridge.log');if($LASTEXITCODE){throw 'HidHide recovery failed. Application files and recovery data have been retained.'}}
if($service){$service.Dispose();& sc.exe delete DS5LinkBridge | Out-Null;if($LASTEXITCODE){throw 'Cannot remove DS5Link service'}}
$owner=(Get-ItemProperty 'HKLM:\SOFTWARE\DS5Link' -Name OwnerSid -ErrorAction SilentlyContinue).OwnerSid
if($owner){$run='Registry::HKEY_USERS\'+$owner+'\Software\Microsoft\Windows\CurrentVersion\Run';if(Test-Path $run){Remove-ItemProperty $run -Name DS5Link -ErrorAction SilentlyContinue}}
$shortcut=Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'DS5Link.lnk';if(Test-Path -LiteralPath $shortcut){Remove-Item -LiteralPath $shortcut}
# Both the trusted registration and this installed script must agree before deletion.
$resolved=[IO.Path]::GetFullPath($install)
if(!$resolved.Equals($registered,[StringComparison]::OrdinalIgnoreCase) -or !$resolved.Equals([IO.Path]::GetFullPath($PSScriptRoot),[StringComparison]::OrdinalIgnoreCase)){throw 'Unexpected uninstall root'}
$removal=Remove-AppOwnedFiles $removalPlan
if($removal.rootRemaining){Write-Host "Папка оставлена: в ней есть посторонние или изменённые файлы. $resolved"}
Remove-Item 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DS5Link' -Recurse -ErrorAction SilentlyContinue
Remove-Item 'HKLM:\SOFTWARE\DS5Link' -Recurse -ErrorAction SilentlyContinue
Write-Host 'DS5Link removed. Shared USB/IP and HidHide drivers and local diagnostic data were retained.'
