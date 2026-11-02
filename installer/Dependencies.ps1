[CmdletBinding()]
param([Alias("ReportPath")][string]$DS5DependencyReportPath,[ValidateSet('usbip-win2','HidHide','ViGEmBus')][Alias("InstallName")][string]$DS5DependencyToInstall)
$ErrorActionPreference='Stop'

function Get-DS5DependencyStatus($Dependency) {
 $utility=switch($Dependency.name){
  'usbip-win2' {"$env:ProgramFiles\USBip\usbip.exe"}
  'HidHide' {"$env:ProgramFiles\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe"}
  'ViGEmBus' {$null}
  default {throw 'Unknown dependency'}
 }
 $names=switch($Dependency.name){'usbip-win2' {@('usbip2_ude','usbip2_filter')};'HidHide' {@('HidHide')};'ViGEmBus' {@('ViGEmBus')}}
 # Package 1.22.0 intentionally contains the unchanged 1.21.442.0 driver.
 $driverVersion=if($Dependency.name -eq 'ViGEmBus'){'1.21.442.0'}else{$Dependency.version}
 $found=0;$running=$true;$compatible=$true
 foreach($name in $names){
  $driver=Get-CimInstance Win32_SystemDriver -Filter "Name='$name'"
  if(!$driver){continue}
  $path=[Environment]::ExpandEnvironmentVariables($driver.PathName.Trim('"')) -replace '^\\SystemRoot', $env:windir -replace '^\\\?\?\\',''
  if(!(Test-Path -LiteralPath $path)){continue}
  ++$found
  $version=(Get-Item -LiteralPath $path).VersionInfo.FileVersion
  # USBip drivers have no file version; HidHide driver and CLI use different version numbers.
  if(!$utility -and ($version -notmatch '\d+\.\d+\.\d+(?:\.\d+)?' -or [version]$Matches[0] -lt [version]$driverVersion)){$compatible=$false}
  if($driver.State -ne 'Running'){$running=$false}
 }
 $utilityPresent=$true
 if(![string]::IsNullOrEmpty($utility)){$utilityPresent=Test-Path -LiteralPath $utility}
 if($utility -and $utilityPresent){$version=(Get-Item -LiteralPath $utility).VersionInfo.FileVersion;if($version -notmatch '\d+\.\d+\.\d+(?:\.\d+)?' -or [version]$Matches[0] -lt [version]$Dependency.version){$compatible=$false}}
 $state=if($found -eq $names.Count -and $utilityPresent -and $compatible){if($running){'Ready'}else{'Reboot'}}elseif($found -gt 0 -or ($utility -and $utilityPresent)){'Repair'}else{'Missing'}
 $description=switch($state){'Ready' {'Установлен — повторная установка не нужна'};'Reboot' {'Установлен — требуется перезагрузка Windows'};'Repair' {'Неполная или устаревшая установка — нужно восстановление'};default {'Не установлен'}}
 [pscustomobject]@{Name=$Dependency.name;State=$state;Description=$description}
}

function Install-DS5Dependency($Dependency) {
 $status=Get-DS5DependencyStatus $Dependency
 if($status.State -in @('Ready','Reboot')){return $(if($status.State -eq 'Reboot'){3010}else{0})}
 $admin=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
 if(!$admin){throw 'Administrator token required'}
 $package=Join-Path $PSScriptRoot ('dependencies\'+$Dependency.file)
 if((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $Dependency.sha256){throw 'Dependency hash mismatch'}
 if((Get-AuthenticodeSignature -LiteralPath $package).Status -ne 'Valid'){throw 'Dependency signature invalid'}
 $arguments=if($Dependency.name -eq 'usbip-win2'){@('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/RESTARTEXITCODE=3010')}else{@('/quiet','/norestart')}
 $result=Start-Process -FilePath $package -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
 if($result.ExitCode -notin @(0,3010)){throw "Dependency installation failed: $($Dependency.name), $($result.ExitCode)"}
 $after=Get-DS5DependencyStatus $Dependency
 if($after.State -in @('Missing','Repair')){throw "Driver installation incomplete: $($Dependency.name)"}
 if($result.ExitCode -eq 3010 -or $after.State -eq 'Reboot'){return 3010}
 return 0
}

if($DS5DependencyReportPath -or $DS5DependencyToInstall){
 $manifest=Get-Content (Join-Path $PSScriptRoot 'dependencies.json') -Raw | ConvertFrom-Json
 if($DS5DependencyToInstall){$dependency=@($manifest | Where-Object name -eq $DS5DependencyToInstall);if($dependency.Count -ne 1){throw 'Dependency missing from package'};exit (Install-DS5Dependency $dependency[0])}
 $rows=foreach($dependency in $manifest){$status=Get-DS5DependencyStatus $dependency;"$($status.Name)`t$($status.State)`t$($status.Description)"}
 [IO.File]::WriteAllLines($DS5DependencyReportPath,[string[]]$rows,[Text.UTF8Encoding]::new($false))
}
