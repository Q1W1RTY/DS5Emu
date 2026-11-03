param([Parameter(Mandatory)][string]$OwnerSid,[string]$TestDrive=$env:SystemDrive)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$run=Join-Path (Join-Path $project 'artifacts') ('install-path-qa-'+(Get-Date -Format yyyyMMdd-HHmmss))
New-Item -ItemType Directory $run|Out-Null
$id='DS5InstallQa'+[guid]::NewGuid().ToString('N')
$serviceName=$id+'Bridge';$pipeName=$id+'.Control.v1'
$targetRoot=Join-Path ($TestDrive+'\') $id
$target=Join-Path $targetRoot 'Приложение [тест] с пробелами'
$package=Join-Path $run 'package';New-Item -ItemType Directory (Join-Path $package 'app\engine') -Force|Out-Null
$realServiceName=if(Get-Service DS5LinkBridge -ErrorAction SilentlyContinue){'DS5LinkBridge'}else{'DSEmuBridge'}
$realRegistration=if($realServiceName -eq 'DS5LinkBridge'){'DS5Link'}else{'DSEmu'}
$realService=Get-CimInstance Win32_Service -Filter "Name='$realServiceName'"
$realPid=$realService.ProcessId
$beforeRegistry=(Get-ItemProperty ('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\'+$realRegistration)).InstallLocation
$steps=@();$ok=$false
function Invoke-Installer([string]$Script,[string]$Phase,[string[]]$Extra=@(),[int]$Expected=0){
 $arguments=@('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$Script+'"'))+$Extra
 $p=Start-Process powershell.exe -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput (Join-Path $run ($Phase+'.out')) -RedirectStandardError (Join-Path $run ($Phase+'.err'))
 if($p.ExitCode -ne $Expected){throw "$Phase returned $($p.ExitCode), expected $Expected"}
 $script:steps+=@{phase=$Phase;exitCode=$p.ExitCode}
}
try{
 if(!([Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Administrator required for the isolated service fixture'}
 foreach($file in @('Install.ps1','Uninstall.ps1','InstallPaths.ps1','OwnedFiles.ps1','Dependencies.ps1')){
  $text=[IO.File]::ReadAllText((Join-Path $project ('installer\'+$file))).Replace('DS5Link',$id).Replace(($id+'.exe'),'DS5Link.exe')
  [IO.File]::WriteAllText((Join-Path $package $file),$text,[Text.UTF8Encoding]::new($true))
 }
 '[]'|Set-Content (Join-Path $package 'dependencies.json') -Encoding utf8
 $source=@'
using System;
using System.IO;
using System.IO.Pipes;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
public class FixtureService:ServiceBase {
 volatile bool stopped;NamedPipeServerStream active;
 public FixtureService(){ServiceName="__SERVICE__";}
 public static void Main(string[] args){if(Array.IndexOf(args,"--repair-only")>=0)return;ServiceBase.Run(new FixtureService());}
 protected override void OnStart(string[] args){new Thread(Run){IsBackground=true}.Start();}
 protected override void OnStop(){stopped=true;try{if(active!=null)active.Dispose();}catch{}}
 void Run(){while(!stopped){try{using(var pipe=new NamedPipeServerStream("__PIPE__",PipeDirection.InOut,1,PipeTransmissionMode.Byte)){active=pipe;pipe.WaitForConnection();var reader=new StreamReader(pipe,Encoding.UTF8,false,1024,true);var writer=new StreamWriter(pipe,new UTF8Encoding(false),1024,true){AutoFlush=true};var json=new JavaScriptSerializer();var request=json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(reader.ReadLine());writer.WriteLine(json.Serialize(new{protocolVersion=1,id=request["id"],ok=true,payload=new{state="Paused"}}));}}catch{if(!stopped)Thread.Sleep(10);}}}
}
'@
 $source=$source.Replace('__SERVICE__',$serviceName).Replace('__PIPE__',$pipeName)
 $cs=Join-Path $run 'Service.cs';[IO.File]::WriteAllText($cs,$source,[Text.UTF8Encoding]::new($false))
 $binary=Join-Path $package 'app\engine\vdsd.exe'
 & "$env:windir\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe (('/out:')+$binary) /reference:System.ServiceProcess.dll /reference:System.Web.Extensions.dll $cs
 if($LASTEXITCODE){throw 'Fixture compile failed'}
 Copy-Item $binary (Join-Path $package 'app\DS5Link.exe')
 $setup=Join-Path $package 'Install.ps1'
 $freshLate=Join-Path $run 'fresh-late-failure';Copy-Item -LiteralPath $package -Destination $freshLate -Recurse
 $freshScript=Join-Path $freshLate 'Install.ps1'
 $freshSource=[IO.File]::ReadAllText($freshScript).Replace('$shell=New-Object -ComObject WScript.Shell',"throw 'Injected fresh install failure after registration'`r`n"+'$shell=New-Object -ComObject WScript.Shell')
 [IO.File]::WriteAllText($freshScript,$freshSource,[Text.UTF8Encoding]::new($true))
 Invoke-Installer $freshScript 'rollback-fresh-late-registration' @('-OwnerSid',$OwnerSid,'-InstallDir',('"'+$target+'"')) 1
 if((Test-Path ('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\'+$id)) -or (Test-Path ('HKLM:\SOFTWARE\'+$id)) -or (Test-Path -LiteralPath $target) -or (Get-Service $serviceName -ErrorAction SilentlyContinue)){throw 'Fresh failed install left registration, files or service'}
 $bootstrapPayload=Join-Path $run 'bootstrap-payload';Copy-Item -LiteralPath $package -Destination $bootstrapPayload -Recurse
 $bootstrapScript=Join-Path $bootstrapPayload 'Install.ps1'
 $guard='$parent=[IO.Path]::GetDirectoryName($PSScriptRoot);if(!$parent.Equals($env:ProgramFiles,[StringComparison]::OrdinalIgnoreCase)){throw "Not protected extraction"};$acl=Get-Acl -LiteralPath $PSScriptRoot;if(!$acl.AreAccessRulesProtected){throw "Inherited ACL"}'
 $guarded=[IO.File]::ReadAllText($bootstrapScript).Replace('[Console]::OutputEncoding=',($guard+"`r`n"+'[Console]::OutputEncoding='))
 [IO.File]::WriteAllText($bootstrapScript,$guarded,[Text.UTF8Encoding]::new($true))
 Get-ChildItem $bootstrapPayload -File -Recurse | Sort-Object FullName | ForEach-Object {'{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName).Hash,$_.FullName.Substring($bootstrapPayload.Length+1)} | Set-Content (Join-Path $bootstrapPayload 'files.sha256') -Encoding utf8
 $bootstrapZip=Join-Path $run 'bootstrap.zip';Compress-Archive (Join-Path $bootstrapPayload '*') $bootstrapZip
 $bootstrapExe=Join-Path $run 'BootstrapFixture.exe'
 & "$env:windir\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 (('/out:')+$bootstrapExe) /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll (('/resource:')+$bootstrapZip+',DS5Link.Payload') (Join-Path $project 'installer\Setup.cs') (Join-Path $project 'installer\SetupStyle.cs')
 if($LASTEXITCODE){throw 'Bootstrap fixture compile failed'}
 [IO.File]::WriteAllText($bootstrapScript,"throw 'UNTRUSTED COPY WAS EXECUTED'",[Text.UTF8Encoding]::new($true))
 $bootstrap=Start-Process $bootstrapExe -ArgumentList @('--install-elevated',('"'+$target+'"'),$OwnerSid) -PassThru -Wait
 if($bootstrap.ExitCode -ne 0){throw ('Protected bootstrap failed: '+$bootstrap.ExitCode)}
 $steps+=@{phase='protected-bootstrap-ignores-mutated-external-copy';exitCode=$bootstrap.ExitCode}

 $actual=(Get-ItemProperty ('HKLM:\SOFTWARE\'+$id)).InstallLocation
 if($actual -ne $target){throw 'Selected path not persisted'}
 $service=Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
 if($service.State -ne 'Running' -or !$service.PathName.StartsWith(('"'+$target+'\engine\vdsd.exe"'))){throw 'Custom service path incorrect'}
 $shell=New-Object -ComObject WScript.Shell
 $link=Join-Path ([Environment]::GetFolderPath('CommonPrograms')) ($id+'.lnk')
 if($shell.CreateShortcut($link).TargetPath -ne (Join-Path $target 'DS5Link.exe')){throw 'Custom shortcut target incorrect'}
 $uninstallKey='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\'+$id
 if(!((Get-ItemProperty $uninstallKey).UninstallString).EndsWith(('"'+$target+'\Uninstall.ps1"'),[StringComparison]::OrdinalIgnoreCase)){throw 'Custom uninstall path incorrect'}
 $fixtureRun='Registry::HKEY_USERS\'+$OwnerSid+'\Software\Microsoft\Windows\CurrentVersion\Run'
 New-Item $fixtureRun -Force|Out-Null
 New-ItemProperty $fixtureRun -Name $id -Value ('"'+(Join-Path $target 'DS5Link.exe')+'" --background') -PropertyType String -Force|Out-Null
 Invoke-Installer $setup 'upgrade-detects-path' @('-OwnerSid',$OwnerSid)
 if((Get-ItemProperty $fixtureRun -Name $id).$id -ne ('"'+(Join-Path $target 'DS5Link.exe')+'" --background')){throw 'Upgrade changed autostart target'}
 $mismatch=Join-Path $targetRoot 'Другой каталог'
 Invoke-Installer $setup 'reject-relocation' @('-OwnerSid',$OwnerSid,'-InstallDir',('"'+$mismatch+'"')) 1
 if(Test-Path -LiteralPath $mismatch){throw 'Rejected relocation changed filesystem'}
 # Hide only fixture registration while checking new-install rejection paths.
 Remove-ItemProperty ('HKLM:\SOFTWARE\'+$id) -Name InstallLocation
 Remove-ItemProperty $uninstallKey -Name InstallLocation
 try{
  $foreign=Join-Path $targetRoot 'NotEmpty';New-Item -ItemType Directory $foreign|Out-Null
  Set-Content (Join-Path $foreign 'keep.txt') 'preserve me'
  Invoke-Installer $setup 'reject-nonempty' @('-ValidateOnly','-InstallDir',('"'+$foreign+'"')) 1
  foreach($bad in @('D:\','\\server\share\DS5Link',$env:windir,$env:ProgramFiles)){
   $label='reject-path-'+$steps.Count
   Invoke-Installer $setup $label @('-ValidateOnly','-InstallDir',('"'+$bad.TrimEnd('\')+'"')) 1
  }
  if((Get-Content (Join-Path $foreign 'keep.txt') -Raw).Trim() -ne 'preserve me'){throw 'Unrelated content changed'}
 }finally{
  New-ItemProperty ('HKLM:\SOFTWARE\'+$id) -Name InstallLocation -Value $target -PropertyType String -Force|Out-Null
  New-ItemProperty $uninstallKey -Name InstallLocation -Value $target -PropertyType String -Force|Out-Null
 }
 # A late failure must restore registration and shortcuts, not just files.
 $beforeLate=(Get-ItemProperty $uninstallKey|Select-Object DisplayName,DisplayVersion,InstallLocation,DisplayIcon,UninstallString|ConvertTo-Json -Compress)
 $late=Join-Path $run 'late-failure';Copy-Item -LiteralPath $package -Destination $late -Recurse
 $lateScript=Join-Path $late 'Install.ps1'
 $lateSource=[IO.File]::ReadAllText($lateScript).Replace('$shell=New-Object -ComObject WScript.Shell',"Set-ItemProperty `$uninstallKey -Name DisplayName -Value BROKEN`r`nthrow 'Injected failure after uninstall registration'`r`n"+'$shell=New-Object -ComObject WScript.Shell')
 [IO.File]::WriteAllText($lateScript,$lateSource,[Text.UTF8Encoding]::new($true))
 Invoke-Installer $lateScript 'rollback-late-registration' @('-OwnerSid',$OwnerSid) 1
 $afterLate=(Get-ItemProperty $uninstallKey|Select-Object DisplayName,DisplayVersion,InstallLocation,DisplayIcon,UninstallString|ConvertTo-Json -Compress)
 if($beforeLate -ne $afterLate -or (Get-Service $serviceName).Status -ne 'Running'){throw 'Late rollback did not restore registration and service'}
 $expected=(Get-FileHash (Join-Path $target 'engine\vdsd.exe')).Hash
 $corrupt=Join-Path $run 'corrupt';Copy-Item -LiteralPath $package -Destination $corrupt -Recurse
 [IO.File]::WriteAllBytes((Join-Path $corrupt 'app\engine\vdsd.exe'),[byte[]](0,1,2,3))
 Invoke-Installer (Join-Path $corrupt 'Install.ps1') 'rollback-corrupt-candidate' @('-OwnerSid',$OwnerSid) 1
 if((Get-FileHash (Join-Path $target 'engine\vdsd.exe')).Hash -ne $expected -or (Get-Service $serviceName).Status -ne 'Running'){throw 'Rollback did not restore files and service'}
 Set-Content -LiteralPath (Join-Path $target 'personal-sentinel.txt') 'foreign must survive'
 Invoke-Installer (Join-Path $target 'Uninstall.ps1') 'uninstall-custom-path'
 if((Test-Path -LiteralPath (Join-Path $target 'engine\vdsd.exe')) -or (Get-Service $serviceName -ErrorAction SilentlyContinue) -or (Test-Path $uninstallKey)){throw 'Custom install not fully removed'}
 if((Get-Content -LiteralPath (Join-Path $target 'personal-sentinel.txt') -Raw).Trim() -ne 'foreign must survive'){throw 'Uninstall touched a foreign file'}
 if((Get-ItemProperty $fixtureRun -Name $id -ErrorAction SilentlyContinue).$id){throw 'Fixture autostart entry remained'}
 if((Get-CimInstance Win32_Service -Filter "Name='$realServiceName'").ProcessId -ne $realPid){throw 'Real service was restarted unexpectedly'}
 if((Get-ItemProperty ('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\'+$realRegistration)).InstallLocation -ne $beforeRegistry){throw 'Real registration changed'}
 $ok=$true
}catch{$errorText=$_.Exception.ToString();throw}
finally{
 $fixture=Get-Service $serviceName -ErrorAction SilentlyContinue
 if($fixture){Stop-Service $serviceName -ErrorAction SilentlyContinue;& sc.exe delete $serviceName|Out-Null}
 @{ok=$ok;error=$errorText;steps=$steps;target=$target;realServicePidPreserved=((Get-CimInstance Win32_Service -Filter "Name='$realServiceName'").ProcessId -eq $realPid)}|ConvertTo-Json -Depth 6|Set-Content (Join-Path $run 'result.json') -Encoding utf8
 Set-Content (Join-Path (Join-Path $project 'artifacts') 'install-path-qa-latest.txt') $run -Encoding utf8
}
