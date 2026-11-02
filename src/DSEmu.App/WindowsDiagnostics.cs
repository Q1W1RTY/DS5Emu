using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Win32;

namespace DSEmu;
internal static class WindowsDiagnostics {
 static readonly string Data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"DSEmu");
 internal static IEnumerable<DiagnosticSource> Sources() {
  yield return new("service.json",()=>DiagnosticArchive.Json(ServiceState.Read()));
  yield return new("installation.json",Installation);
  yield return new("drivers.json",Drivers);
  yield return new("reboot.json",Reboot);
  yield return new("logs/setup.log",()=>DiagnosticArchive.ReadTail(Path.Combine(Data,"setup.log")));
  yield return new("logs/bridge.log",()=>DiagnosticArchive.ReadTail(Path.Combine(Data,"bridge.log")));
  yield return new("logs/previous-bridge.log",()=>DiagnosticArchive.ReadTail(Path.Combine(Data,"bridge.log.1"),128*1024));
  yield return new("logs/ui.log",()=>DiagnosticArchive.ReadTail(Path.Combine(Settings.DirectoryPath,"ui.log")));
  yield return new("logs/bootstrap.log",()=>DiagnosticArchive.ReadTail(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DS5Emu","Setup","last-setup.log")));
  yield return new("logs/elevated-bootstrap.log",BootstrapLogs);
  yield return new("events/service.json",()=>Events("System",new[]{"Service Control Manager"}));
  yield return new("events/application.json",()=>Events("Application",new[]{"Application Error","Application Hang",".NET Runtime","Windows Error Reporting"}));
 }
 static RegistryKey Machine()=>RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64);
 static string InstallDirectory() {
  using var machine=Machine();using var key=machine.OpenSubKey(@"SOFTWARE\DSEmu");
  return key?.GetValue("InstallLocation") as string??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"DS5Emu");
 }
 static object FileInfo(string path) {
  try{File.GetAttributes(path);}
  catch(Exception error)when(error is FileNotFoundException or DirectoryNotFoundException){return new {path,exists=false};}
  catch(UnauthorizedAccessException error){return new {path,exists=(bool?)null,error=error.Message};}
  var file=new FileInfo(path);
  try {var version=FileVersionInfo.GetVersionInfo(path);return new {path,exists=true,length=file.Length,lastWriteUtc=file.LastWriteTimeUtc,fileVersion=version.FileVersion,productVersion=version.ProductVersion};}
  catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException){return new {path,exists=true,error=error.Message};}
 }
 static string Installation() {
  using var machine=Machine();using var key=machine.OpenSubKey(@"SOFTWARE\DSEmu");
  using var uninstall=machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DSEmu");
  string? owner=key?.GetValue("OwnerSid") as string;using var identity=WindowsIdentity.GetCurrent();
  var directory=InstallDirectory();
  var files=new[]{"DS5Emu.exe","DS5Emu.dll","engine/vdsd.exe","engine/opus.dll","engine/vcruntime140.dll","engine/vcruntime140_1.dll","engine/msvcp140.dll"}.Select(name=>FileInfo(Path.Combine(directory,name))).ToArray();
  return DiagnosticArchive.Json(new {registered=key!=null,installDirectory=directory,registeredVersion=uninstall?.GetValue("DisplayVersion"),ownerRegistered=owner!=null,ownerMatchesCurrentUser=owner==null?(bool?)null:owner==identity.User?.Value,isElevated=new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator),is64BitProcess=Environment.Is64BitProcess,files});
 }
 static string DriverPath(string path) {
  path=Environment.ExpandEnvironmentVariables(path).Trim('"');
  if(path.StartsWith(@"\??\",StringComparison.Ordinal))path=path[4..];
  var windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows);
  if(path.StartsWith(@"\SystemRoot\",StringComparison.OrdinalIgnoreCase))path=Path.Combine(windows,path[12..]);
  if(!Path.IsPathRooted(path))path=Path.Combine(windows,path);
  return path;
 }
 static string Drivers() {
  using var machine=Machine();
  var drivers=new[]{"usbip2_ude","usbip2_filter","HidHide","ViGEmBus"}.Select(name=> {
   using var key=machine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+name);
   var image=key?.GetValue("ImagePath") as string;
   return new {name,registered=key!=null,service=ServiceState.Read(name),file=image==null?null:FileInfo(DriverPath(image))};
  }).ToArray();
  var programFiles=Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
  return DiagnosticArchive.Json(new {drivers,utilities=new[]{FileInfo(Path.Combine(programFiles,"USBip","usbip.exe")),FileInfo(Path.Combine(programFiles,"Nefarius Software Solutions","HidHide","x64","HidHideCLI.exe"))}});
 }
 static string Reboot() {
  var bootEstimate=DateTime.UtcNow-TimeSpan.FromMilliseconds(Environment.TickCount64);
  var marker=new FileInfo(Path.Combine(Data,"reboot-required.txt"));
  bool markerExists;
  try{File.GetAttributes(marker.FullName);markerExists=true;}
  catch(Exception error)when(error is FileNotFoundException or DirectoryNotFoundException){markerExists=false;}
  using var machine=Machine();
  using var servicing=machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
  using var update=machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
  using var session=machine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager");
  return DiagnosticArchive.Json(new {estimatedBootUtc=bootEstimate,uptimeSeconds=Environment.TickCount64/1000,applicationMarkerExists=markerExists,applicationMarkerUtc=markerExists?(DateTime?)marker.LastWriteTimeUtc:null,applicationRestartPending=markerExists && marker.LastWriteTimeUtc>bootEstimate,windowsServicingPending=servicing!=null,windowsUpdatePending=update!=null,pendingFileOperations=session?.GetValue("PendingFileRenameOperations")!=null});
 }
 static string BootstrapLogs() {
  var logs=Directory.GetFiles(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"DS5Emu-Setup-*.log",SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc).Take(3).ToArray();
  if(logs.Length==0)throw new FileNotFoundException("No retained elevated bootstrap logs");
  return string.Join("\n\n",logs.Select(path=> {
   try{return "--- "+Path.GetFileName(path)+" ---\n"+DiagnosticArchive.ReadTail(path,64*1024);}
   catch(Exception error)when(error is IOException or UnauthorizedAccessException){return "--- "+Path.GetFileName(path)+" ---\n"+error.GetType().Name+": "+error.Message;}
  }));
 }
 static string Events(string log,string[] providers) {
  var query="*[System[("+string.Join(" or ",providers.Select(provider=>"Provider[@Name='"+provider+"']"))+") and TimeCreated[timediff(@SystemTime) <= 604800000]]]";
  var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"wevtutil.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.Unicode,StandardErrorEncoding=Encoding.Unicode};
  foreach(var argument in new[]{"qe",log,"/q:"+query,"/c:200","/rd:true","/f:xml","/e:Events","/uni:true"})info.ArgumentList.Add(argument);
  using var process=Process.Start(info)??throw new IOException("Cannot start Windows event log reader");
  var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
  if(!process.WaitForExit(5000)){try{process.Kill(true);}catch(InvalidOperationException){}throw new TimeoutException("Windows event log query exceeded 5 seconds");}
  var streams=Task.WhenAll(output,errors);if(!streams.Wait(1500))throw new TimeoutException("Windows event log output timed out");
  if(process.ExitCode!=0)throw new IOException("wevtutil exit "+process.ExitCode+": "+errors.Result);
  return FilterEvents(output.Result);
 }
 internal static string FilterEvents(string xml) {
  if(string.IsNullOrWhiteSpace(xml))return DiagnosticArchive.Json(new {events=Array.Empty<object>()});
  var document=XDocument.Parse(xml);XNamespace ns="http://schemas.microsoft.com/win/2004/08/events/event";
  var events=document.Descendants(ns+"Event").Where(node=>new[]{"DS5Emu","DSEmuBridge","DSEmu.exe","vdsd.exe"}.Any(name=>node.Element(ns+"EventData")?.Value.Contains(name,StringComparison.OrdinalIgnoreCase)==true || node.Element(ns+"UserData")?.Value.Contains(name,StringComparison.OrdinalIgnoreCase)==true)).Take(30).Select(node=> {
   var system=node.Element(ns+"System");
   return new {provider=(string?)system?.Element(ns+"Provider")?.Attribute("Name"),eventId=(string?)system?.Element(ns+"EventID"),level=(string?)system?.Element(ns+"Level"),timeUtc=(string?)system?.Element(ns+"TimeCreated")?.Attribute("SystemTime"),data=node.Element(ns+"EventData")?.Elements().Select(item=>item.Value.Length>8192?item.Value[..8192]:item.Value).ToArray(),userData=node.Element(ns+"UserData")?.Value};
  }).ToArray();
  return DiagnosticArchive.Json(new {events});
 }
}
