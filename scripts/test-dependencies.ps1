$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\installer\Dependencies.ps1')
# Dot-sourced script parameters must not constrain the installer's destination variable.
$installName='DS5Link'
$installName='DS5Link'
if($installName -ne 'DS5Link'){throw 'Dependency helper polluted installation variables'}
$script:drivers=@{};$script:versions=@{};$script:utilityPresent=$false
function Get-CimInstance {param($ClassName,$Filter);$name=($Filter -split "'")[1];$script:drivers[$name]}
function Test-Path {param($LiteralPath);if($LiteralPath -like '*.sys'){return $script:versions.ContainsKey($LiteralPath)};return $script:utilityPresent}
function Get-Item {param($LiteralPath);[pscustomobject]@{VersionInfo=[pscustomobject]@{FileVersion=$(if($LiteralPath -like '*usbip.exe'){'0.9.8.1'}elseif($LiteralPath -like '*HidHideCLI.exe'){'1.5.230.0'}else{$script:versions[$LiteralPath]})}}}
function Assert-State($dependency,$expected){$actual=Get-DS5DependencyStatus $dependency;if($actual.State -ne $expected){throw "$($dependency.name): expected $expected, got $($actual.State)"}}
$usb=@{name='usbip-win2';version='0.9.8.1'};$hid=@{name='HidHide';version='1.5.230.0'};$xbox=@{name='ViGEmBus';version='1.22.0.0'}
Assert-State $usb Missing;Assert-State $hid Missing;Assert-State $xbox Missing
$statuses=@(foreach($dep in @($usb,$hid,$xbox)){Get-DS5DependencyStatus ([pscustomobject]$dep)})
if($statuses.Count -ne 3){throw 'Pipeline status report incomplete'}
foreach($name in @('usbip2_ude','usbip2_filter','HidHide','ViGEmBus')){
 $script:drivers[$name]=[pscustomobject]@{PathName="C:\Windows\System32\drivers\$name.sys";State='Running'}
 $script:versions["C:\Windows\System32\drivers\$name.sys"]=switch($name){'HidHide' {'1.5.230.0'};'ViGEmBus' {'1.22.0.0'};default {'0.9.8.1'}}
}
Assert-State $usb Repair;Assert-State $hid Repair;Assert-State $xbox Ready
$script:utilityPresent=$true
Assert-State $usb Ready;Assert-State $hid Ready
# Regression: setup 1.22.0 ships driver 1.21.442.0, not driver 1.22.0.
$script:versions['C:\Windows\System32\drivers\ViGEmBus.sys']='1.21.442.0';Assert-State $xbox Ready
$script:versions['C:\Windows\System32\drivers\ViGEmBus.sys']='1.23.0.0';Assert-State $xbox Ready
function Start-Process {throw 'An installed dependency must never run its installer'}
if((Install-DS5Dependency $xbox) -ne 0){throw 'Already-installed dependency was not skipped'}
$script:drivers['ViGEmBus'].State='Stopped';Assert-State $xbox Reboot
if((Install-DS5Dependency $xbox) -ne 3010){throw 'Reboot dependency was not skipped'}
$script:versions['C:\Windows\System32\drivers\ViGEmBus.sys']='1.16.0.0';Assert-State $xbox Repair
$script:versions.Remove('C:\Windows\System32\drivers\usbip2_filter.sys');Assert-State $usb Repair
Write-Host 'Dependency detection: missing, partial, ready, newer, old, reboot and skip-install cases passed (mock driver inventory)'
