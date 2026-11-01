$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$directory=Join-Path $root 'artifacts\downloads';New-Item -ItemType Directory $directory -Force | Out-Null
foreach($item in @(
 @{File='USBip-0.9.8.1-x64.exe';Url='https://github.com/vadimgrn/usbip-win2/releases/download/v.0.9.8.1/USBip-0.9.8.1-x64.exe';Hash='38cad6d4432b52d5bb9409d9ad03b72fdffc4ada4cd3a48fbeca1a2752a8518a'},
 @{File='HidHide_1.5.230_x64.exe';Url='https://github.com/nefarius/HidHide/releases/download/v1.5.230.0/HidHide_1.5.230_x64.exe';Hash='f4bbbcb82e6258641b887c74bc81c4c5f66e4aa811808dfc304347687b7605f6'}
)){
 $path=Join-Path $directory $item.File
 if(!(Test-Path $path)){Invoke-WebRequest $item.Url -OutFile $path}
 if((Get-FileHash $path -Algorithm SHA256).Hash -ne $item.Hash){throw "Dependency hash mismatch: $($item.File)"}
 if((Get-AuthenticodeSignature $path).Status -ne 'Valid'){throw "Dependency signature invalid: $($item.File)"}
 Write-Host "Verified: $($item.File)"
}
