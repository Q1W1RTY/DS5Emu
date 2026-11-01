param(
 [Parameter(Mandatory)][string]$PackageDirectory,
 [Parameter(Mandatory)][string]$OwnerSid,
 [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'qa-common.ps1')
$root=Split-Path $PSScriptRoot -Parent
$install=[IO.Path]::GetFullPath((Join-Path $env:ProgramFiles 'DSEmu'))
$data=Join-Path $env:ProgramData 'DSEmu'
$result=Join-Path $OutputDirectory 'install-lifecycle.json'
$phases=@();$restoreError=$null;$completed=$false
function Save-Result([string]$ErrorText='') {
 [pscustomobject]@{ok=$completed;error=$ErrorText;restoreError=$restoreError;at=[DateTime]::UtcNow.ToString('o');package=$PackageDirectory;phases=$phases}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $result -Encoding utf8
}
function Read-HidHide {
 $snapshot=@(& "$env:ProgramFiles\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe" --cloak-state --inv-state --app-list --dev-list)
 if($LASTEXITCODE){throw 'Cannot read HidHide baseline'}
 @($snapshot|Sort-Object)
}
function Invoke-Setup([string]$Script,[string]$Phase,[switch]$WithOwner) {
 $arguments=@('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$Script+'"'))
 if($WithOwner){$arguments+=@('-OwnerSid',$OwnerSid)}
 $child=Start-Process powershell.exe -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput (Join-Path $OutputDirectory ($Phase+'.out')) -RedirectStandardError (Join-Path $OutputDirectory ($Phase+'.err'))
 if($child.ExitCode -ne 0){throw "$Phase exited $($child.ExitCode); see $Phase.err"}
}
function Assert-Paused {
 $null=Wait-DSEmuState Paused -Seconds 30
 if(Test-Path (Join-Path $data 'bridge.db.hidhide-recovery.json')){throw 'Recovery journal remained while paused'}
 $until=[DateTime]::UtcNow.AddSeconds(15)
 do {
  $virtual=@(Get-PnpDevice -PresentOnly|Where-Object {$_.InstanceId -like '*DSEmu-DualSense-Port-0*'})
  if(!$virtual.Count){return}
  Start-Sleep -Milliseconds 300
 }while([DateTime]::UtcNow -lt $until)
 throw 'Virtual controller remained while paused'
}
try {
 if(!([Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Administrator required'}
 $null=[Security.Principal.SecurityIdentifier]::new($OwnerSid)
 if($install -ne 'C:\Program Files\DSEmu'){throw 'Unexpected installation root'}
 $PackageDirectory=[IO.Path]::GetFullPath($PackageDirectory)
 if(!$PackageDirectory.StartsWith((Join-Path $root 'artifacts\packages\'),[StringComparison]::OrdinalIgnoreCase)){throw 'Package must be inside this project artifacts/packages'}
 foreach($path in @($install,$PackageDirectory)){
  if(!(Test-Path -LiteralPath $path) -or ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Missing or redirected lifecycle path'}
 }
 $service=Get-CimInstance Win32_Service -Filter "Name='DSEmuBridge'"
 if(!$service -or !$service.PathName.StartsWith(('"'+$install+'\engine\vdsd.exe"'),[StringComparison]::OrdinalIgnoreCase)){throw 'Unexpected DSEmu service path'}
 Assert-Paused
 $baseline=Read-HidHide
 $configHash=(Get-FileHash (Join-Path $data 'bridge.db')).Hash
 # User profile names can differ from account names; use the registered profile.
 $profile=(Get-ItemProperty ('HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\'+$OwnerSid)).ProfileImagePath
 $settingsPath=Join-Path ([Environment]::ExpandEnvironmentVariables($profile)) 'AppData\Local\DSEmu\settings.json'
 $settingsHash=if(Test-Path $settingsPath){(Get-FileHash $settingsPath).Hash}else{$null}
 $runKey='Registry::HKEY_USERS\'+$OwnerSid+'\Software\Microsoft\Windows\CurrentVersion\Run'
 $autostart=(Get-ItemProperty $runKey -Name DSEmu -ErrorAction SilentlyContinue).DSEmu
 $backup=Join-Path $OutputDirectory 'before-lifecycle'
 New-Item -ItemType Directory $backup -Force|Out-Null
 Copy-Item -LiteralPath (Join-Path $data 'bridge.db') -Destination $backup
 if(Test-Path $settingsPath){Copy-Item -LiteralPath $settingsPath -Destination $backup}
 Get-ChildItem -LiteralPath $install -File -Recurse|ForEach-Object{[pscustomobject]@{path=$_.FullName.Substring($install.Length+1);sha256=(Get-FileHash $_.FullName).Hash}}|ConvertTo-Json|Set-Content (Join-Path $backup 'installed-hashes.json')
 $expectedNative=(Get-FileHash (Join-Path $PackageDirectory 'app\engine\vdsd.exe')).Hash
 Invoke-Setup (Join-Path $PackageDirectory 'Install.ps1') 'upgrade' -WithOwner
 Assert-Paused
 if((Get-FileHash (Join-Path $data 'bridge.db')).Hash -ne $configHash){throw 'Upgrade changed controller configuration'}
 if($settingsHash -and (Get-FileHash $settingsPath).Hash -ne $settingsHash){throw 'Upgrade changed UI settings'}
 if((Get-ItemProperty $runKey -Name DSEmu -ErrorAction SilentlyContinue).DSEmu -ne $autostart){throw 'Upgrade changed autostart preference'}
 $phases+=[pscustomobject]@{phase='upgrade';ok=$true;configPreserved=$true;settingsPreserved=$true;pausePreserved=$true;autostartPreserved=$true};Save-Result

 $fixture=Join-Path $OutputDirectory 'rollback-fixture'
 if(Test-Path -LiteralPath $fixture){throw 'Rollback fixture already exists; choose a new run directory'}
 Copy-Item -LiteralPath $PackageDirectory -Destination $fixture -Recurse
 # Deliberately malformed candidate exercises failure after swap at Start-Service.
 [IO.File]::WriteAllBytes((Join-Path $fixture 'app\engine\vdsd.exe'),[byte[]](0,1,2,3))
 $rejected=$false;$rejection=''
 try{Invoke-Setup (Join-Path $fixture 'Install.ps1') 'rollback' -WithOwner}catch{$rejected=$true;$rejection=$_.Exception.Message}
 if(!$rejected){throw 'Malformed candidate was not rejected'}
 Assert-Paused
 if((Get-FileHash (Join-Path $install 'engine\vdsd.exe')).Hash -ne $expectedNative){throw 'Rollback did not restore the installed engine'}
 $phases+=[pscustomobject]@{phase='rollback-after-swap';ok=$true;failure=$rejection;engineRestored=$true;serviceRunning=$true};Save-Result

 Invoke-Setup (Join-Path $PackageDirectory 'Uninstall.ps1') 'uninstall'
 $until=[DateTime]::UtcNow.AddSeconds(15)
 do{$remaining=Get-Service DSEmuBridge -ErrorAction SilentlyContinue;if(!$remaining){break};Start-Sleep -Milliseconds 300}while([DateTime]::UtcNow -lt $until)
 if($remaining -or (Test-Path -LiteralPath $install)){throw 'Uninstall retained service or application directory'}
 if(Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DSEmu'){throw 'Uninstall registration remained'}
 if(Test-Path 'HKLM:\SOFTWARE\DSEmu'){throw 'Owner registration remained'}
 $afterUninstall=Read-HidHide
 if(@(Compare-Object $baseline $afterUninstall).Count){throw 'Uninstall changed baseline HidHide'}
 $shared=@(Get-CimInstance Win32_SystemDriver -Filter "Name='HidHide' OR Name='usbip2_ude' OR Name='usbip2_filter'"|Select-Object Name,State)
 if($shared.Count -ne 3){throw 'Shared driver registration changed'}
 $phases+=[pscustomobject]@{phase='uninstall';ok=$true;hidhidePreserved=$true;sharedDrivers=$shared;localDataRetained=(Test-Path (Join-Path $data 'bridge.db'))};Save-Result

 Invoke-Setup (Join-Path $PackageDirectory 'Install.ps1') 'reinstall' -WithOwner
 Assert-Paused
 if((Get-FileHash (Join-Path $data 'bridge.db')).Hash -ne $configHash){throw 'Reinstall changed controller configuration'}
 if($settingsHash -and (Get-FileHash $settingsPath).Hash -ne $settingsHash){throw 'Reinstall changed UI settings'}
 if($autostart){New-ItemProperty -Path $runKey -Name DSEmu -Value $autostart -PropertyType String -Force|Out-Null}
 $phases+=[pscustomobject]@{phase='reinstall';ok=$true;configPreserved=$true;settingsPreserved=$true;paused=$true};$completed=$true;Save-Result
} catch {
 $failure=$_.Exception.Message
 try {
  if(!(Test-Path (Join-Path $install 'engine\vdsd.exe')) -or (Get-Service DSEmuBridge -ErrorAction SilentlyContinue).Status -ne 'Running'){
   Invoke-Setup (Join-Path $PackageDirectory 'Install.ps1') 'restore' -WithOwner
  }
 }catch{$restoreError=$_.Exception.Message}
 Save-Result $failure
 exit 1
}
