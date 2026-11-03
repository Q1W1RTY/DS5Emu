param()
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$before=(Get-CimInstance Win32_Service -Filter "Name='DS5LinkBridge'").ProcessId
$removed=@()
foreach($report in Get-ChildItem (Join-Path $root 'artifacts') -Directory -Filter 'install-path-qa-*'){
 $file=Join-Path $report.FullName 'result.json';if(!(Test-Path -LiteralPath $file)){continue}
 $record=Get-Content -LiteralPath $file -Raw|ConvertFrom-Json
 $targetRoot=[IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($record.target))
 if($targetRoot -notmatch '^[A-Za-z]:\\(DS5InstallQa[a-f0-9]{32})$'){throw 'Unexpected fixture root'}
 $id=$Matches[1]
 if(Get-Service ($id+'Bridge') -ErrorAction SilentlyContinue){throw 'Fixture still running; nothing removed for it'}
 foreach($path in @($targetRoot,(Join-Path $env:ProgramData $id))){
  $absolute=[IO.Path]::GetFullPath($path)
  if([IO.Path]::GetFileName($absolute) -ne $id){throw 'Cleanup escaped fixture directory'}
  if(Test-Path -LiteralPath $absolute){
   $items=@(Get-Item -LiteralPath $absolute -Force)+@(Get-ChildItem -LiteralPath $absolute -Recurse -Force)
   if($items|Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}){throw 'Fixture contains a link; cleanup refused'}
   Remove-Item -LiteralPath $absolute -Recurse -Force
   $removed+=$absolute
  }
 }
 foreach($key in @(('HKLM:\SOFTWARE\'+$id),('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\'+$id))){
  if(Test-Path -LiteralPath $key){Remove-Item -LiteralPath $key -Recurse}
 }
}
if((Get-CimInstance Win32_Service -Filter "Name='DS5LinkBridge'").ProcessId -ne $before){throw 'Working service PID changed'}
@{ok=$true;removed=$removed;workingServicePidPreserved=$true}|ConvertTo-Json -Depth 4|Set-Content (Join-Path $root 'artifacts\fixture-cleanup.json') -Encoding utf8
