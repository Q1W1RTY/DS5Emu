using System.IO.Pipes;
using System.IO;
using System.Text;
using System.Text.Json;
using DSEmu;
static void Check(bool value,string message){if(!value)throw new Exception(message);}
static async Task TestClient(string scenario) {
 var name="DSEmu.ClientTest."+Guid.NewGuid().ToString("N");
 using var server=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);
 var client=BridgeClient.SendAsync("getState",name);
 await server.WaitForConnectionAsync();
 using var reader=new StreamReader(server,Encoding.UTF8,false,1024,true);
 var line=await reader.ReadLineAsync();using var request=JsonDocument.Parse(line!);
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
 try {var state=await client;Check(scenario=="valid" && state.State=="Active" && state.BatteryPercent==75,"Invalid reply was accepted");}
 catch(IOException)when(scenario!="valid"){}
}
foreach(var scenario in new[]{"valid","id","version","error","large","eof"})await TestClient(scenario);
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
Console.WriteLine("13 managed protocol/diagnostic scenarios passed");
