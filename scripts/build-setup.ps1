[CmdletBinding()]
param([string]$PackageDirectory,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$PackageDirectory){$PackageDirectory=(Get-Content (Join-Path $root 'artifacts\packages\LATEST.txt') -Raw).Trim() -replace '\.zip$',''}
$PackageDirectory=[IO.Path]::GetFullPath($PackageDirectory)
if(!$OutputDirectory){$OutputDirectory=Join-Path $root ('artifacts\releases\DS5Emu-0.1.2-'+(Get-Date -Format yyyyMMdd-HHmmss))}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path $OutputDirectory){throw 'Use a new output directory; release files are not overwritten.'}
# Release the hardware-tested native baseline, never the experimental build directory.
$expectedNative='F22337DD797FA8303E4474B833F471B0DA4EA6D4C0C90BF1B8B1823F518524ED'
if((Get-FileHash (Join-Path $PackageDirectory 'app\engine\vdsd.exe')).Hash -ne $expectedNative){throw 'This release profile only accepts the hardware-tested 064430 native baseline.'}
foreach($line in Get-Content (Join-Path $PackageDirectory 'files.sha256')){
 if(!$line.Trim()){continue}
 if($line -notmatch '^([A-Fa-f0-9]{64})  (.+)$'){throw 'Malformed baseline hash manifest'}
 $expected=$Matches[1];$path=[IO.Path]::GetFullPath((Join-Path $PackageDirectory $Matches[2]))
 if(!$path.StartsWith($PackageDirectory+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid baseline manifest path'}
 if((Get-FileHash -LiteralPath $path).Hash -ne $expected){throw "Baseline modified: $path"}
}
New-Item -ItemType Directory $OutputDirectory | Out-Null
$payload=Join-Path $OutputDirectory 'payload';New-Item -ItemType Directory $payload | Out-Null
Copy-Item (Join-Path $PackageDirectory 'app') $payload -Recurse
# Publish only the UI; the hardware-tested native files above are retained.
& (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe') publish (Join-Path $root 'src\DSEmu.App\DSEmu.App.csproj') -c Release -r win-x64 --self-contained true -p:Platform=AnyCPU -o (Join-Path $payload 'app')
if($LASTEXITCODE){throw 'UI publish failed'}
foreach($legacyFile in @('DSEmu.exe','DSEmu.dll','DSEmu.deps.json','DSEmu.runtimeconfig.json','DSEmu.pdb')){
 $legacyPath=Join-Path $payload ('app\'+$legacyFile)
 if(Test-Path -LiteralPath $legacyPath){Remove-Item -LiteralPath $legacyPath}
}
if((Get-FileHash (Join-Path $payload 'app\engine\vdsd.exe')).Hash -ne $expectedNative){throw 'Native baseline changed during UI publish'}
Copy-Item (Join-Path $PackageDirectory 'dependencies') $payload -Recurse
Copy-Item (Join-Path $PackageDirectory 'dependencies.json') $payload
Copy-Item (Join-Path $PackageDirectory 'build.json') (Join-Path $payload 'engine-build.json')
Copy-Item (Join-Path $root 'installer\Install.ps1'),(Join-Path $root 'installer\Uninstall.ps1') $payload
Copy-Item (Join-Path $root 'docs\user\INSTALL.md') (Join-Path $payload 'README-RU.md')
Copy-Item (Join-Path $root 'docs\user\INSTALL.md') (Join-Path $OutputDirectory 'README-RU.md')
foreach($dependency in (Get-Content (Join-Path $payload 'dependencies.json') -Raw | ConvertFrom-Json)){
 $file=Join-Path $payload ('dependencies\'+$dependency.file)
 if((Get-FileHash $file).Hash -ne $dependency.sha256){throw 'Dependency hash mismatch'}
 if((Get-AuthenticodeSignature $file).Status -ne 'Valid'){throw 'Dependency signature invalid'}
}
$release=[ordered]@{version='0.1.2';builtAt=(Get-Date).ToString('o');enginePackage=(Split-Path $PackageDirectory -Leaf);nativeSha256=$expectedNative;uiSha256=(Get-FileHash (Join-Path $payload 'app\DS5Emu.dll')).Hash;iconSha256=(Get-FileHash (Join-Path $root 'src\DSEmu.App\Assets\DS5Emu.ico')).Hash;experimentalQueue=$false;bootstrapSourceSha256=(Get-FileHash (Join-Path $root 'installer\Setup.cs')).Hash;installerSourceSha256=(Get-FileHash (Join-Path $root 'installer\Install.ps1')).Hash}
$release|ConvertTo-Json|Set-Content (Join-Path $payload 'release.json') -Encoding utf8
Copy-Item (Join-Path $payload 'release.json') $OutputDirectory
Get-ChildItem $payload -File -Recurse | Sort-Object FullName | ForEach-Object {'{0}  {1}' -f (Get-FileHash $_.FullName).Hash,$_.FullName.Substring($payload.Length+1)} | Set-Content (Join-Path $payload 'files.sha256') -Encoding utf8
$zip=Join-Path $OutputDirectory 'payload.zip'
Compress-Archive -Path (Join-Path $payload '*') -DestinationPath $zip -CompressionLevel Optimal
$compiler=Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe=Join-Path $OutputDirectory 'DS5Emu-Setup.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/out:$exe" "/win32icon:$root\src\DSEmu.App\Assets\DS5Emu.ico" "/win32manifest:$root\installer\Setup.manifest" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "/resource:$zip,DS5Emu.Payload" (Join-Path $root 'installer\Setup.cs')
if($LASTEXITCODE){throw 'Bootstrap compilation failed'}
$verification=Join-Path $OutputDirectory 'verification.json'
$process=Start-Process $exe -ArgumentList @('--verify-only',('"'+$verification+'"')) -PassThru -Wait
if($process.ExitCode -ne 0 -or !(Get-Content $verification -Raw|ConvertFrom-Json).ok){throw 'Embedded payload verification failed'}
'{0}  DS5Emu-Setup.exe' -f (Get-FileHash $exe).Hash | Set-Content (Join-Path $OutputDirectory 'SHA256.txt') -Encoding ascii
Write-Host "Release: $exe"
Write-Host 'Payload verified without installing or changing the running app.'
