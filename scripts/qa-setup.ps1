param([Parameter(Mandatory)][string]$ReleaseDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$ReleaseDirectory=[IO.Path]::GetFullPath($ReleaseDirectory)
$exe=Join-Path $ReleaseDirectory 'DS5Link-Setup.exe'
$qa=Join-Path $ReleaseDirectory 'qa';New-Item -ItemType Directory $qa -Force|Out-Null
function Verify-Exe([string]$Path,[string]$Report,[int]$ExpectedCode){
 $child=Start-Process $Path -ArgumentList @('--verify-only',('"'+$Report+'"')) -WindowStyle Hidden -PassThru -Wait
 if($child.ExitCode -ne $ExpectedCode){throw "Unexpected exit code: $($child.ExitCode)"}
 $result=Get-Content $Report -Raw|ConvertFrom-Json
 if($result.ok -ne ($ExpectedCode -eq 0)){throw 'Verification status does not match exit code'}
}
Verify-Exe $exe (Join-Path $qa 'valid.json') 0
$payload=Join-Path $ReleaseDirectory 'payload'
$dependencyReport=Join-Path $qa 'dependencies.tsv'
$probe=Start-Process powershell.exe -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+(Join-Path $payload 'Dependencies.ps1')+'"'),'-ReportPath',('"'+$dependencyReport+'"')) -WindowStyle Hidden -Wait -PassThru
if($probe.ExitCode -ne 0){throw 'Dependency reporting failed under Windows PowerShell'}
$rows=@(Get-Content $dependencyReport)
if($rows.Count -ne 3){throw 'Expected three independent dependency reports'}
foreach($name in @('usbip-win2','HidHide','ViGEmBus')){
 if(@($rows | Where-Object {($_ -split "`t")[0] -eq $name}).Count -ne 1){throw "Missing dependency report: $name"}
}
foreach($file in Get-ChildItem $payload -File -Recurse){
 if($file.Name -match '^bridge\.(db|log)' -or $file.Name -eq 'settings.json'){throw 'User configuration must not be shipped'}
}
$release=Get-Content (Join-Path $payload 'release.json') -Raw|ConvertFrom-Json
$nativeHash=(Get-FileHash (Join-Path $payload 'app\engine\vdsd.exe')).Hash
if($nativeHash -ne $release.nativeSha256){throw 'Native differs from release manifest'}
if(!$release.candidate -and $nativeHash -ne 'F22337DD797FA8303E4474B833F471B0DA4EA6D4C0C90BF1B8B1823F518524ED'){throw 'Wrong accepted native baseline'}
if($release.candidate -and $release.hardwareAccepted){throw 'Candidate cannot claim hardware acceptance'}
$fixture=Join-Path $qa 'corrupt-payload';New-Item -ItemType Directory $fixture -Force|Out-Null
Set-Content (Join-Path $fixture 'example.txt') 'tampered'
(('0'*64)+'  example.txt')|Set-Content (Join-Path $fixture 'files.sha256') -Encoding ascii
$zip=Join-Path $qa 'corrupt.zip';Compress-Archive (Join-Path $fixture '*') $zip -Force
$fixtureExe=Join-Path $qa 'Corrupt-Setup.exe'
& "$env:windir\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 "/out:$fixtureExe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll "/resource:$zip,DS5Link.Payload" (Join-Path $root 'installer\Setup.cs') (Join-Path $root 'installer\SetupStyle.cs')
if($LASTEXITCODE){throw 'Fixture compilation failed'}
Verify-Exe $fixtureExe (Join-Path $qa 'corrupt.json') 1
Remove-Item -LiteralPath $fixtureExe,$zip -Force
foreach($script in @('Install.ps1','Uninstall.ps1','InstallPaths.ps1','OwnedFiles.ps1','Dependencies.ps1')){
 $tokens=$null;$errors=$null
 [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $payload $script),[ref]$tokens,[ref]$errors)|Out-Null
 if($errors){throw "PowerShell parser errors: $script"}
}
@{ok=$true;checks=@('embedded payload extraction and all hashes','corrupt payload rejected with exit 1','no controller database/log/settings in payload','native manifest hash; candidate acceptance flag checked','installer PowerShell syntax','three independent dependency reports under Windows PowerShell 5.1');installed=$false}|ConvertTo-Json -Depth 4|Set-Content (Join-Path $qa 'result.json') -Encoding utf8
Write-Host 'Setup QA passed. Drivers/service were not changed.'
# Keep the current verified build and one rollback build after successful local QA.
if([IO.Path]::GetDirectoryName($ReleaseDirectory) -eq (Join-Path $root 'artifacts\releases')){
 & (Join-Path $PSScriptRoot 'prune-releases.ps1') -Apply
}
