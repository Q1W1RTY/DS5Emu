param([Parameter(Mandatory)][string]$ReleaseDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$ReleaseDirectory=[IO.Path]::GetFullPath($ReleaseDirectory)
$exe=Join-Path $ReleaseDirectory 'DS5Emu-Setup.exe'
$qa=Join-Path $ReleaseDirectory 'qa';New-Item -ItemType Directory $qa -Force|Out-Null
function Verify-Exe([string]$Path,[string]$Report,[int]$ExpectedCode){
 $child=Start-Process $Path -ArgumentList @('--verify-only',('"'+$Report+'"')) -PassThru -Wait
 if($child.ExitCode -ne $ExpectedCode){throw "Unexpected exit code: $($child.ExitCode)"}
 $result=Get-Content $Report -Raw|ConvertFrom-Json
 if($result.ok -ne ($ExpectedCode -eq 0)){throw 'Verification status does not match exit code'}
}
Verify-Exe $exe (Join-Path $qa 'valid.json') 0
$payload=Join-Path $ReleaseDirectory 'payload'
foreach($file in Get-ChildItem $payload -File -Recurse){
 if($file.Name -match '^bridge\.(db|log)' -or $file.Name -eq 'settings.json'){throw 'User configuration must not be shipped'}
}
if((Get-FileHash (Join-Path $payload 'app\engine\vdsd.exe')).Hash -ne 'F22337DD797FA8303E4474B833F471B0DA4EA6D4C0C90BF1B8B1823F518524ED'){throw 'Wrong native baseline'}
$fixture=Join-Path $qa 'corrupt-payload';New-Item -ItemType Directory $fixture -Force|Out-Null
Set-Content (Join-Path $fixture 'example.txt') 'tampered'
(('0'*64)+'  example.txt')|Set-Content (Join-Path $fixture 'files.sha256') -Encoding ascii
$zip=Join-Path $qa 'corrupt.zip';Compress-Archive (Join-Path $fixture '*') $zip -Force
$fixtureExe=Join-Path $qa 'Corrupt-Setup.exe'
& "$env:windir\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 "/out:$fixtureExe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll "/resource:$zip,DS5Emu.Payload" (Join-Path $root 'installer\Setup.cs')
if($LASTEXITCODE){throw 'Fixture compilation failed'}
Verify-Exe $fixtureExe (Join-Path $qa 'corrupt.json') 1
foreach($script in @('Install.ps1','Uninstall.ps1')){
 $tokens=$null;$errors=$null
 [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $payload $script),[ref]$tokens,[ref]$errors)|Out-Null
 if($errors){throw "PowerShell parser errors: $script"}
}
@{ok=$true;checks=@('embedded payload extraction and all hashes','corrupt payload rejected with exit 1','no controller database/log/settings in payload','hardware-tested native hash','installer PowerShell syntax');installed=$false}|ConvertTo-Json -Depth 4|Set-Content (Join-Path $qa 'result.json') -Encoding utf8
Write-Host 'Setup QA passed. Drivers/service were not changed.'
