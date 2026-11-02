using System.IO.Pipes;
using System.IO;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using DSEmu;
static void Check(bool value,string message){if(!value)throw new Exception(message);}
static async Task TestClient(string scenario) {
 var name="DSEmu.ClientTest."+Guid.NewGuid().ToString("N");
 using var server=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);
 var client=scenario=="options"?BridgeClient.SetInputOptionsAsync(new InputOptions {LeftDeadzone=10},name):BridgeClient.SendAsync("getState",name);
 await server.WaitForConnectionAsync();
 using var reader=new StreamReader(server,Encoding.UTF8,false,1024,true);
 var line=await reader.ReadLineAsync();using var request=JsonDocument.Parse(line!);
 if(scenario=="options")Check(request.RootElement.GetProperty("options").GetProperty("leftDeadzone").GetInt32()==10,"Input options wire casing is incompatible with native service");
 var id=request.RootElement.GetProperty("id").GetString();
 string response=scenario switch {
  "id"=>JsonSerializer.Serialize(new{protocolVersion=1,id="wrong",ok=true,payload=new{state="Active"}}),
  "version"=>JsonSerializer.Serialize(new{protocolVersion=2,id,ok=true,payload=new{state="Active"}}),
  "error"=>JsonSerializer.Serialize(new{protocolVersion=1,id,ok=false,error="BRIDGE_PAUSED"}),
  "large"=>new string('x',66000),
  _=>JsonSerializer.Serialize(new{protocolVersion=1,id,ok=true,payload=new{state="Active",batteryPercent=75}})
 };
 if(scenario=="eof")server.Disconnect();
 else {try{await server.WriteAsync(Encoding.UTF8.GetBytes(response+"\n"));await server.FlushAsync();}catch(IOException)when(scenario=="large") {}}
 try {var state=await client;Check((scenario=="valid" || scenario=="options") && state.State=="Active" && state.BatteryPercent==75,"Invalid reply was accepted");}
 catch(IOException)when(scenario!="valid"){}
}
foreach(var scenario in new[]{"valid","options","id","version","error","large","eof"})await TestClient(scenario);
var masked=Diagnostics.Mask("address=12:34:56:78:9a:bc HID\\VID_054C&PID_0CE6\\123456789ABC");
Check(!masked.Contains("12:34") && !masked.Contains("123456789ABC") && !masked.Contains("VID_054C"),"Private device identity leaked");
var eventInput=new[]{
 "2026-11-02T12:00:00.100+03:00|daemon|INFO|Service started",
 "2026-11-02T12:00:01.100+03:00|bluetooth|WARN|address=12:34:56:78:9a:bc disconnected",
 "2026-11-02T12:00:02.100+03:00|bluetooth|WARN|address=12:34:56:78:9a:bc disconnected",
 "2026-11-02T12:00:03.100+03:00|input-control|DEBUG|packet details",
 "2026-11-02T12:00:04.100+03:00|port|ERROR|"+new string('x',500),
 "Unstructured legacy message"
};
var events=Diagnostics.RecentEvents(eventInput);
Check(events.Length==3 && events[0].Message=="Unstructured legacy message","Recent event order/bound failed");
Check(events[1].Level=="ERROR" && events[1].Time=="12:00:04" && events[1].Message.Length==220 && events[1].Detail.Length>500,"Severity, timestamp or bounded text lost");
Check(events[2].Count==2 && events[2].RepeatLabel=="×2" && !events[2].Detail.Contains("12:34:56") && events[2].Time=="12:00:02","Repeated events or masking failed");
Check(Diagnostics.RecentEvents(new[]{"", " ", "2026-11-02T12:00:00+03:00|daemon|DEBUG|hidden detail"}).Length==0,"Empty/debug-only log must remain empty");
Check(eventInput[1].Contains("12:34:56:78:9a:bc"),"Event presentation mutated original export data");
Check(Diagnostics.RecentEvents(new[]{"legacy|same", "same"}).Length==2,"Different legacy messages were incorrectly merged");
var reportDirectory=Path.Combine(Path.GetTempPath(),"DS5Emu-diagnostic-tests-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(reportDirectory);
try {
 var archive=Path.Combine(reportDirectory,"offline.zip");
 var result=await DiagnosticArchive.SaveAsync(archive,new BridgeState {State="Error",Error="cached error"},()=>Task.FromException<BridgeState>(new TimeoutException("pipe unavailable")),new[]{
  new DiagnosticSource("service.json",()=>DiagnosticArchive.Json(new ServiceSnapshot("DSEmuBridge",true,"Stopped",Win32ExitCode:1067))),
  new DiagnosticSource("logs/setup.log",()=>"Компьютер: FRIEND-PC\nИмя пользователя: FRIEND-PC\\Alice\nOwnerSid S-1-5-21-111-222-333-1001\nC:\\Users\\Alice\\AppData\\Local\\DS5Emu\n12:34:56:78:9a:bc"),
  new DiagnosticSource("logs/bridge.log",()=>throw new FileNotFoundException("No bridge log")),
  new DiagnosticSource("denied.json",()=>throw new UnauthorizedAccessException("No access")),
  new DiagnosticSource("timeout.json",()=>throw new TimeoutException("Reader timeout")),
  new DiagnosticSource("failure.json",()=>throw new IOException("Reader failed"))
 });
 Check(!result.BridgeReachable && result.Collected==2 && result.Unavailable==4 && !File.Exists(archive+".partial"),"Offline collection did not preserve successful sources");
 using(var zip=ZipFile.OpenRead(archive)) {
  string Read(string name){using var reader=new StreamReader(zip.GetEntry(name)!.Open());return reader.ReadToEnd();}
  using var manifest=JsonDocument.Parse(Read("manifest.json"));var statuses=manifest.RootElement.GetProperty("sources").EnumerateArray().Select(v=>v.GetProperty("Status").GetString()).ToArray();
  Check(statuses.SequenceEqual(new[]{"collected","collected","missing","accessDenied","timeout","error"}),"Source failures were not diagnosed independently");
  using var connection=JsonDocument.Parse(Read("connection.json"));Check(!connection.RootElement.GetProperty("reachable").GetBoolean() && connection.RootElement.GetProperty("stateIsCached").GetBoolean() && connection.RootElement.GetProperty("errorType").GetString()!.Contains("TimeoutException"),"IPC failure detail was lost");
  var log=Read("logs/setup.log");Check(!log.Contains("FRIEND-PC") && !log.Contains("Alice") && !log.Contains("S-1-5-21") && !log.Contains("12:34:56"),"Identity leaked in collected log");
  using var service=JsonDocument.Parse(Read("service.json"));Check(service.RootElement.GetProperty("Win32ExitCode").GetInt32()==1067,"Service startup error missing from offline archive");
 }
 var connected=Path.Combine(reportDirectory,"connected.zip");
 var connectedResult=await DiagnosticArchive.SaveAsync(connected,new(),()=>Task.FromResult(new BridgeState {State="Paused",LogLines=new[]{"HID\\VID_054C&PID_0CE6\\123456789ABC"}}),Array.Empty<DiagnosticSource>());
 Check(connectedResult.BridgeReachable,"Reachable bridge marked offline");
 using(var zip=ZipFile.OpenRead(connected)){using var reader=new StreamReader(zip.GetEntry("status.json")!.Open());Check(!reader.ReadToEnd().Contains("VID_054C"),"Identity leaked in status JSON");}
 var collision=Path.Combine(reportDirectory,"collision.zip");File.WriteAllText(collision+".partial","preserve existing file");
 try{await DiagnosticArchive.SaveAsync(collision,new(),()=>Task.FromResult(new BridgeState()),Array.Empty<DiagnosticSource>());throw new Exception("Archive collision accepted");}catch(IOException){}
 Check(File.ReadAllText(collision+".partial")=="preserve existing file" && !File.Exists(collision),"Archive failure deleted an existing temporary file");
 var tail=Path.Combine(reportDirectory,"tail.log");File.WriteAllText(tail,string.Concat(Enumerable.Repeat("old line\n",1000))+"последняя строка\n",Encoding.Unicode);
 var tailText=DiagnosticArchive.ReadTail(tail,128);Check(tailText.Contains("последняя строка") && tailText.Length<160 && tailText.StartsWith("[tail:"),"Bounded UTF-16 log tail failed");
 var xml="<Events><Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='Application Error'/><EventID>1000</EventID><Computer>PRIVATE-PC</Computer></System><EventData><Data>unrelated.exe</Data></EventData></Event><Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='Application Error'/><EventID>1000</EventID><Computer>PRIVATE-PC</Computer></System><EventData><Data>vdsd.exe</Data><Data>C:\\Users\\Alice\\app.exe</Data></EventData></Event></Events>";
 using var filtered=JsonDocument.Parse(WindowsDiagnostics.FilterEvents(xml));Check(filtered.RootElement.GetProperty("events").GetArrayLength()==1 && !filtered.RootElement.ToString().Contains("PRIVATE-PC") && !filtered.RootElement.ToString().Contains("Alice"),"Windows event filtering or privacy failed");
 Check(ServiceState.Read("DSEmu.NonexistentDiagnosticTest."+Guid.NewGuid().ToString("N")).Exists==false,"Missing service was not identified");
}finally{foreach(var file in Directory.GetFiles(reportDirectory))File.Delete(file);Directory.Delete(reportDirectory);}
var exitCommands=new List<string>();
var release=await BridgeShutdown.AttemptAsync(command=>{exitCommands.Add(command);return Task.FromResult(new BridgeState {State=command=="pause"?"Stopping":"Paused"});},()=>false);
Check(release.Released && exitCommands.SequenceEqual(new[]{"pause","getState"}),"Exit did not wait for controller release");
var offlineExit=await BridgeShutdown.AttemptAsync(_=>Task.FromException<BridgeState>(new IOException("pipe down")),()=>false);
Check(!offlineExit.Released && offlineExit.Error=="pipe down","Unavailable service must offer UI-only exit instead of claiming release");
var stoppedExit=await BridgeShutdown.AttemptAsync(_=>Task.FromException<BridgeState>(new IOException("pipe down")),()=>true);
Check(stoppedExit.Released,"Stopped service unnecessarily blocked UI exit");
var pendingExit=await BridgeShutdown.AttemptAsync(_=>Task.FromResult(new BridgeState {State="Error",Error="HidHide recovery pending"}),()=>false);
Check(!pendingExit.Released && pendingExit.Error=="HidHide recovery pending","Recovery failure was treated as controller release");
var slowExit=await BridgeShutdown.AttemptAsync(_=>Task.FromResult(new BridgeState {State="Stopping"}),()=>false,TimeSpan.Zero);
Check(!slowExit.Released,"Exit ignored release timeout");
if(args.Length==2 && args[0]=="--diagnostic-smoke") {
 Directory.CreateDirectory(args[1]);var smoke=Path.Combine(args[1],"diagnostic-offline-smoke.zip");
 var result=await DiagnosticArchive.SaveAsync(smoke,new BridgeState {State="Error"},()=>BridgeClient.SendAsync("getDiagnostics","DSEmu.NonexistentProbe."+Guid.NewGuid().ToString("N")));
 Console.WriteLine("Live read-only diagnostic collection: "+result.Collected+" collected, "+result.Unavailable+" unavailable; IPC reachable="+result.BridgeReachable);
}
Check(new InputProfile().Valid && !new InputProfile().Options.TouchEnabled && new InputProfile().Options.LeftDeadzone==0,"Input customization must default to native passthrough");
Check(!new KeyBinding(0x5B,0).Valid && !new KeyBinding(0x41,8).Valid && !new InputProfile {Options=new(){LeftDeadzone=31}}.Valid,"Unsupported keys/modifiers or deadzones accepted");
Check(!new InputProfile {Options=new(){TouchEnabled=true}}.Valid && new InputProfile {Options=new(){TouchEnabled=true},Bindings=new(){{"tap",new(0x70,2)}}}.Valid,"Empty touch mappings accepted");
var cursor=new InputEventCursor();
TouchInputBatch Batch(string epoch,long seq,bool enabled,params TouchInputEvent[] events)=>new(){Epoch=epoch,Sequence=seq,Enabled=enabled,Events=events};
TouchInputEvent Event(long seq,int age=0)=>new(){Sequence=seq,AgeMs=age,Action="tap"};
Check(cursor.Read(Batch("a",10,true,Event(10))).Length==0,"Initial input event replayed");
Check(cursor.Read(Batch("a",13,true,Event(11),Event(11),Event(12,400),Event(13,-1))).Length==1 && cursor.Sequence==13,"Old, duplicate or future events injected");
Check(cursor.Read(Batch("a",14,false,Event(14))).Length==0 && cursor.Read(Batch("b",1,true,Event(1))).Length==0,"Paused or restarted service replayed input");
cursor.Reset();Check(cursor.Read(Batch("b",2,true,Event(2))).Length==0 && cursor.Read(Batch("",3,true,Event(3))).Length==0,"Reconnection baseline was not reset");
Check(!new InputPreview().Fresh && !new InputPreview {Enabled=true,AgeMs=500}.Fresh && !new InputPreview {Enabled=false,AgeMs=0}.Fresh && new InputPreview {Enabled=true,AgeMs=499}.Fresh,"Preview freshness boundaries are incorrect");
var previewPipe="DSEmu.InputPreview.Test."+Guid.NewGuid().ToString("N");
using(var server=new NamedPipeServerStream(previewPipe,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous)){
 var client=BridgeClient.ReadInputPreviewAsync(previewPipe);await server.WaitForConnectionAsync();
 using var reader=new StreamReader(server,Encoding.UTF8,leaveOpen:true);var request=JsonDocument.Parse((await reader.ReadLineAsync())!);
 Check(request.RootElement.GetProperty("command").GetString()=="getInputPreview","Preview requested a mutating command");
 var response=JsonSerializer.Serialize(new {protocolVersion=1,id=request.RootElement.GetProperty("id").GetString(),ok=true,payload=new {enabled=true,ageMs=10,leftX=140,leftY=128,rightX=255,rightY=0,click=true,touches=new[]{new {active=true,x=1000,y=500}}}});
 await server.WriteAsync(Encoding.UTF8.GetBytes(response+"\n"));await server.FlushAsync();var result=await client;
 Check(result.Fresh && result.LeftX==140 && result.RightY==0 && result.Click && result.Touches.Single().X==1000,"Preview coordinates were not decoded");
}
Console.WriteLine("40 managed protocol/diagnostic/input scenarios passed");
