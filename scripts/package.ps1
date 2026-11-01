[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$native=Join-Path $root 'artifacts\native-Release'
foreach($file in @('vdsd.exe','vdsctl.exe','opus.dll')){if(!(Test-Path (Join-Path $native $file))){throw 'Build the native engine first'}}
$stamp=Get-Date -Format yyyyMMdd-HHmmss
$output=Join-Path $root ('artifacts\packages\DS5Emu-0.1.1-'+$stamp)
$app=Join-Path $output 'app';New-Item -ItemType Directory $app -Force | Out-Null
$dotnet=Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
& $dotnet publish (Join-Path $root 'src\DSEmu.App\DSEmu.App.csproj') -c Release -r win-x64 --self-contained true -p:Platform=AnyCPU -o $app
if($LASTEXITCODE){throw 'WPF self-contained publish failed'}
$engine=Join-Path $app 'engine';New-Item -ItemType Directory $engine | Out-Null
foreach($file in @('vdsd.exe','vdsctl.exe','opus.dll')){Copy-Item (Join-Path $native $file) $engine}
$crt=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\2022\BuildTools\VC\Redist\MSVC\14.44.35112\x64\Microsoft.VC143.CRT'
if(!(Test-Path $crt)){throw 'Pinned MSVC runtime not found'}
Copy-Item (Join-Path $crt '*.dll') $engine
$licenses=Join-Path $app 'licenses';New-Item -ItemType Directory $licenses | Out-Null
Copy-Item (Join-Path $root 'engine\vds\LICENSE') (Join-Path $licenses 'vDS-MIT.txt')
foreach($dependency in @('opus','nlohmann-json')){Copy-Item (Join-Path $native ('vcpkg_installed\x64-windows\share\'+$dependency+'\copyright')) (Join-Path $licenses ($dependency+'.txt'))}
Copy-Item (Join-Path $env:ProgramFiles 'dotnet\LICENSE.txt') (Join-Path $licenses 'dotnet-LICENSE.txt')
Copy-Item (Join-Path $env:ProgramFiles 'dotnet\ThirdPartyNotices.txt') (Join-Path $licenses 'dotnet-ThirdPartyNotices.txt')
Copy-Item (Join-Path $root 'installer\*') $output
$dependencies=Join-Path $output 'dependencies';New-Item -ItemType Directory $dependencies | Out-Null
$manifest=foreach($item in @(
 @{name='usbip-win2';version='0.9.8.1';file='USBip-0.9.8.1-x64.exe';sha256='38cad6d4432b52d5bb9409d9ad03b72fdffc4ada4cd3a48fbeca1a2752a8518a'},
 @{name='HidHide';version='1.5.230.0';file='HidHide_1.5.230_x64.exe';sha256='f4bbbcb82e6258641b887c74bc81c4c5f66e4aa811808dfc304347687b7605f6'}
)){
 $source=Join-Path $root ('artifacts\downloads\'+$item.file)
 $hash=(Get-FileHash $source -Algorithm SHA256).Hash.ToLowerInvariant()
 if($item.sha256 -and $hash -ne $item.sha256){throw 'Dependency hash mismatch'}
 $signature=Get-AuthenticodeSignature $source;if($signature.Status -ne 'Valid'){throw "Invalid dependency signature: $($item.file)"}
 Copy-Item $source $dependencies
 [pscustomobject]@{name=$item.name;version=$item.version;file=$item.file;sha256=$hash;signer=$signature.SignerCertificate.Subject}
}
$manifest | ConvertTo-Json | Set-Content (Join-Path $output 'dependencies.json') -Encoding utf8
Copy-Item (Join-Path $root 'docs\STATUS.md') (Join-Path $output 'STATUS.md')
Copy-Item (Join-Path $root 'docs\user\INSTALL.md') (Join-Path $output 'README-RU.md')
$buildInfo=[ordered]@{version='0.1.1';builtAt=(Get-Date).ToString('o');upstream=(git -C (Join-Path $root 'engine\vds') rev-parse HEAD);patchSha256=(Get-FileHash (Join-Path $root 'patches\vds\0001-dsemu-bootstrap.patch')).Hash;runtime=(Get-Content (Join-Path $app 'DS5Emu.runtimeconfig.json') -Raw | ConvertFrom-Json)}
$buildInfo | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output 'build.json') -Encoding utf8
$hashes=Get-ChildItem $output -File -Recurse | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash,$_.FullName.Substring($output.Length+1) }
$hashes | Set-Content (Join-Path $output 'files.sha256') -Encoding utf8
$zip=$output+'.zip';Compress-Archive -Path ($output+'\*') -DestinationPath $zip
Set-Content (Join-Path $root 'artifacts\packages\LATEST.txt') $zip -Encoding utf8
Write-Host "Package: $zip"
