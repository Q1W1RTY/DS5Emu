param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'qa-common.ps1')
if(Get-Process DSX -ErrorAction SilentlyContinue){throw 'A real DSX process already exists; fixture was not started'}
$fixtureDirectory=Join-Path $OutputDirectory 'fixture-dsx'
New-Item -ItemType Directory $fixtureDirectory -Force|Out-Null
$fixturePath=Join-Path $fixtureDirectory 'DSX.exe'
if(Test-Path $fixturePath){throw 'Use a new fixture directory'}
# Windows PowerShell 5 compiles a .NET Framework EXE, not a .NET apphost.
Add-Type -TypeDefinition 'using System.Threading; public static class DS5LinkDsxNameFixture { public static void Main() { Thread.Sleep(60000); } }' -OutputType ConsoleApplication -OutputAssembly $fixturePath
$cli="$env:ProgramFiles\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe"
$fixture=$null
try {
 $null=Wait-DS5LinkState Active
 $fixture=Start-Process -FilePath $fixturePath -WindowStyle Hidden -PassThru
 $conflict=Wait-DS5LinkState Conflict
 $until=[DateTime]::UtcNow.AddSeconds(15)
 do{
  $virtual=@(Get-PnpDevice -PresentOnly|Where-Object {$_.InstanceId -like '*DS5Link-DualSense-Port-0*'})
  if(!$virtual.Count){break};Start-Sleep -Milliseconds 200
 }while([DateTime]::UtcNow -lt $until)
 if($virtual.Count){throw 'Virtual controller remained during Conflict'}
 if(Test-Path "$env:ProgramData\DS5Link\bridge.db.hidhide-recovery.json"){throw 'HidHide journal remained during Conflict'}
 $fixture.Refresh();if($fixture.HasExited){throw 'Fixture was terminated unexpectedly'}
 $during=@(& $cli --cloak-state --inv-state --app-list --dev-list|Sort-Object)
 $during|Set-Content (Join-Path $OutputDirectory 'dsx-conflict-hidhide.txt')
 Stop-Process -Id $fixture.Id -Force;$null=$fixture.WaitForExit(3000);$fixture.Dispose();$fixture=$null
 $after=Wait-DS5LinkState Active
 [pscustomobject]@{ok=$true;fixtureOnly=$true;actualDsxNotLaunched=$true;fixtureSurvivedConflict=$true;virtualAbsentDuringConflict=$true;journalAbsentDuringConflict=$true;pollMilliseconds=200;conflict=$conflict;after=$after}|ConvertTo-Json -Depth 6|Set-Content (Join-Path $OutputDirectory 'dsx-conflict.json')
} catch {
 [pscustomobject]@{ok=$false;fixtureOnly=$true;error=$_.Exception.Message}|ConvertTo-Json|Set-Content (Join-Path $OutputDirectory 'dsx-conflict.json')
 throw
} finally {
 if($fixture){$fixture.Refresh();if(!$fixture.HasExited){Stop-Process -Id $fixture.Id -Force};$fixture.Dispose()}
}
