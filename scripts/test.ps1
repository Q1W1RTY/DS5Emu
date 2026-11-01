[CmdletBinding()]
param([ValidateSet('Release','Debug')][string]$Configuration='Release')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$ctest="${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\ctest.exe"
& $ctest --test-dir (Join-Path $root "artifacts\native-$Configuration") --output-on-failure
if ($LASTEXITCODE) { throw 'Native tests failed' }
& (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe') run --project (Join-Path $root 'tests\DSEmu.Tests\DSEmu.Tests.csproj') -c $Configuration -p:Platform=AnyCPU
if($LASTEXITCODE){throw 'Managed protocol and diagnostics tests failed'}
