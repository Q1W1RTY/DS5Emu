[CmdletBinding()]
param([ValidateSet('Release','Debug')][string]$Configuration = 'Release', [string]$VisualStudioPath,[switch]$NativeOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$VisualStudioPath) {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) { $VisualStudioPath = & $vswhere -latest -products '*' -property installationPath }
    if (!$VisualStudioPath) { $VisualStudioPath = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools" }
}
$vcvars = Join-Path $VisualStudioPath 'VC\Auxiliary\Build\vcvarsall.bat'
if (!(Test-Path $vcvars)) { throw 'MSVC x64 toolchain is required.' }
# Import the Developer Command Prompt environment into this process only.
$lines = & $env:ComSpec /d /c "call `"$vcvars`" x64 >nul && set"
if ($LASTEXITCODE) { throw 'vcvarsall failed' }
foreach ($line in $lines) {
    if ($line -match '^([^=]+)=(.*)$') { [Environment]::SetEnvironmentVariable($Matches[1], $Matches[2], 'Process') }
}
$env:VCPKG_VISUAL_STUDIO_PATH = $VisualStudioPath
$cmake = Join-Path $VisualStudioPath 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
$ninja = Join-Path $VisualStudioPath 'Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe'
$toolchain = Join-Path $VisualStudioPath 'VC\vcpkg\scripts\buildsystems\vcpkg.cmake'
foreach ($path in @($cmake,$ninja,$toolchain)) { if (!(Test-Path $path)) { throw "Missing build dependency: $path" } }
$build = Join-Path $root "artifacts\native-$Configuration"
& $cmake -S (Join-Path $root 'engine\vds') -B $build -G Ninja "-DCMAKE_BUILD_TYPE=$Configuration" "-DCMAKE_MAKE_PROGRAM=$ninja" "-DCMAKE_TOOLCHAIN_FILE=$toolchain" -DVCPKG_TARGET_TRIPLET=x64-windows -DINSTALL_SERVICE=OFF
if ($LASTEXITCODE) { throw 'CMake configure failed' }
& $cmake --build $build --parallel 6
if ($LASTEXITCODE) { throw 'Native build failed' }
$opusSource=Join-Path $build 'vcpkg_installed\x64-windows\bin\opus.dll'
$opusTarget=Join-Path $build 'opus.dll'
if (!(Test-Path $opusTarget) -or (Get-FileHash $opusSource).Hash -ne (Get-FileHash $opusTarget).Hash) {
    Copy-Item $opusSource $opusTarget -Force
}
Write-Host "Built: $build (no service or driver installed)"
if(!$NativeOnly){
    & (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe') build (Join-Path $root 'src\DS5Link.Avalonia\DS5Link.Avalonia.csproj') -c $Configuration -p:Platform=AnyCPU
    if($LASTEXITCODE){throw 'Avalonia build failed'}
}
