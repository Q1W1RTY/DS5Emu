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
Console.WriteLine("7 managed protocol/diagnostic scenarios passed");
