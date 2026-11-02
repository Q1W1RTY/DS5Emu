using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DSEmu;
internal sealed record BridgeState {
 public string State {get;init;}="Waiting";
 public string Mode {get;init;}="ps5";
 public bool Enabled {get;init;}
 public bool BluetoothDisconnected {get;init;}
 public string Transport {get;init;}="Bluetooth";
 public int? BatteryPercent {get;init;}
 public bool Charging {get;init;}
 public bool Muted {get;init;}
 public bool Headphones {get;init;}
 public bool AudioActive {get;init;}
 public bool MicrophoneActive {get;init;}
 public double PacketsPerSecond {get;init;}
 public long GapsOver10ms {get;init;}
 public double MaxGapMs {get;init;}
 public double ObservedSeconds {get;init;}
 public int? Rssi {get;init;}
 public string Error {get;init;}="";
 public string[] LogLines {get;init;}=Array.Empty<string>();
}
internal static class BridgeClient {
 internal static readonly JsonSerializerOptions JsonOptions=new(){PropertyNameCaseInsensitive=true,WriteIndented=true};
 public static async Task<BridgeState> SendAsync(string command,string pipeName="DSEmu.Control.v1") {
   using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(4));
   using var pipe=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
   await pipe.ConnectAsync(timeout.Token);
   var id=Guid.NewGuid().ToString("N");
   var request=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {protocolVersion=1,id,command})+"\n");
   await pipe.WriteAsync(request,timeout.Token);await pipe.FlushAsync(timeout.Token);
   using var data=new MemoryStream();var buffer=new byte[2048];bool finished=false;
   while(!finished && data.Length<=65536) {
     var count=await pipe.ReadAsync(buffer,timeout.Token);
     if(count==0)throw new IOException("Служба закрыла соединение без ответа.");
     for(int i=0;i<count;++i) {if(buffer[i]==10){finished=true;break;}data.WriteByte(buffer[i]);}
   }
   if(data.Length>65536)throw new IOException("Ответ службы превышает допустимый размер.");
   using var json=JsonDocument.Parse(data.ToArray());var root=json.RootElement;
   if(root.GetProperty("protocolVersion").GetInt32()!=1 || root.GetProperty("id").GetString()!=id)throw new IOException("Несовместимый ответ службы.");
   if(!root.GetProperty("ok").GetBoolean())throw new IOException(root.GetProperty("error").GetString());
   return root.GetProperty("payload").Deserialize<BridgeState>(JsonOptions) ?? throw new IOException("Пустое состояние службы.");
 }
}
