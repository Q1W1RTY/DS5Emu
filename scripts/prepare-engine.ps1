$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$engine=Join-Path $root 'engine\vds'
$patch=Join-Path $root 'patches\vds\0001-dsemu-bootstrap.patch'
if (!(Test-Path (Join-Path $engine 'CMakeLists.txt'))) {
    git -C $root submodule update --init engine/vds
    if ($LASTEXITCODE) {throw 'Submodule initialization failed'}
}
$revision=git -C $engine rev-parse HEAD
if ($revision -ne 'ec531599dfdd02e14ce68efd8c0d2dcb439894a5') {throw 'Unexpected vDS revision'}
git -C $engine apply --reverse --check $patch 2>$null
if ($LASTEXITCODE -eq 0) {return}
git -C $engine apply --check $patch
if ($LASTEXITCODE) {throw 'Patch conflicts with local engine changes; nothing overwritten'}
git -C $engine apply $patch
if ($LASTEXITCODE) {throw 'Failed to apply DSEmu engine patch'}
