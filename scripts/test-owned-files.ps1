param()
$ErrorActionPreference='Stop'
. (Join-Path (Split-Path $PSScriptRoot -Parent) 'installer\OwnedFiles.ps1')
$base=Join-Path ([IO.Path]::GetTempPath()) ('DS5Link-owned-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $base|Out-Null
$checks=0
function Check([bool]$Value,[string]$Message){if(!$Value){throw $Message};$script:checks++}
function Fixture([string]$Name){$folder=Join-Path $base $Name;New-Item -ItemType Directory (Join-Path $folder 'engine') -Force|Out-Null;Set-Content (Join-Path $folder 'app.exe') 'owned';Set-Content (Join-Path $folder 'engine\native.dll') 'owned native';New-AppOwnedManifest $folder;return $folder}
$folder=Fixture 'preserve'
Set-Content (Join-Path $folder 'foreign.txt') 'keep foreign'
New-Item -ItemType Directory (Join-Path $folder 'personal')|Out-Null
Set-Content (Join-Path $folder 'personal\save.dat') 'keep nested'
Set-Content (Join-Path $folder 'engine\native.dll') 'modified by owner'
$plan=Get-AppOwnedRemovalPlan $folder;$result=Remove-AppOwnedFiles $plan
Check (!(Test-Path (Join-Path $folder 'app.exe'))) 'Owned executable remained'
Check ((Get-Content (Join-Path $folder 'foreign.txt') -Raw).Trim() -eq 'keep foreign') 'Foreign file changed'
Check ((Get-Content (Join-Path $folder 'personal\save.dat') -Raw).Trim() -eq 'keep nested') 'Foreign nested file changed'
Check ((Get-Content (Join-Path $folder 'engine\native.dll') -Raw).Trim() -eq 'modified by owner') 'Modified owned file deleted'
Check $result.rootRemaining 'Nonempty root removed'
$clean=Fixture 'clean';$result=Remove-AppOwnedFiles (Get-AppOwnedRemovalPlan $clean);Check (!(Test-Path $clean)) 'Empty application root remained'
foreach($bad in @('..\outside.txt','C:\outside.txt','engine\..\outside.txt','file:stream','engine\\native.dll','engine\native.dll.')){
 $folder=Fixture ('bad-'+$checks);$record=Get-Content (Join-Path $folder 'installed-files.json') -Raw|ConvertFrom-Json;$record.files[0].path=$bad;$record|ConvertTo-Json -Depth 6|Set-Content (Join-Path $folder 'installed-files.json') -Encoding utf8
 $rejected=$false;try{$null=Get-AppOwnedRemovalPlan $folder}catch{$rejected=$true};Check $rejected ('Invalid path accepted: '+$bad);Check (Test-Path (Join-Path $folder 'app.exe')) 'Preflight deleted a file'
}
$folder=Fixture 'duplicate';$record=Get-Content (Join-Path $folder 'installed-files.json') -Raw|ConvertFrom-Json;$record.files+=,$record.files[0];$record|ConvertTo-Json -Depth 6|Set-Content (Join-Path $folder 'installed-files.json') -Encoding utf8
$rejected=$false;try{$null=Get-AppOwnedRemovalPlan $folder}catch{$rejected=$true};Check $rejected 'Duplicate owned entry accepted'
$outside=Join-Path $base 'outside';New-Item -ItemType Directory $outside|Out-Null;Set-Content (Join-Path $outside 'victim.txt') 'outside untouched'
$folder=Fixture 'junction';New-Item -ItemType Junction -Path (Join-Path $folder 'linked') -Value $outside|Out-Null
$record=Get-Content (Join-Path $folder 'installed-files.json') -Raw|ConvertFrom-Json;$record.files+=@{path='linked\victim.txt';sha256=(Get-FileHash (Join-Path $outside 'victim.txt')).Hash};$record|ConvertTo-Json -Depth 6|Set-Content (Join-Path $folder 'installed-files.json') -Encoding utf8
$rejected=$false;try{$null=Get-AppOwnedRemovalPlan $folder}catch{$rejected=$true};Check $rejected 'Junction accepted';Check ((Get-Content (Join-Path $outside 'victim.txt') -Raw).Trim() -eq 'outside untouched') 'Junction target modified'
Write-Output "$checks ownership-removal checks passed; fixtures retained at $base"
