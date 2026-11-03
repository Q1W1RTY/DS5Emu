using System;
using System.IO;
using System.Threading.Tasks;

namespace DS5Link;
internal sealed record BridgeShutdownResult(bool Released,string? Error=null);
internal static class BridgeShutdown {
 internal static async Task<BridgeShutdownResult> AttemptAsync(Func<string,Task<BridgeState>> send,Func<bool> stopped,TimeSpan? wait=null) {
  try {
   var state=await send("pause");
   var deadline=DateTime.UtcNow+(wait??TimeSpan.FromSeconds(10));
   while(state.State!="Paused") {
    if(state.State=="Error")throw new IOException(string.IsNullOrWhiteSpace(state.Error)?"Служба не подтвердила освобождение контроллера.":state.Error);
    if(DateTime.UtcNow>=deadline)throw new TimeoutException("Служба не подтвердила освобождение контроллера.");
    await Task.Delay(100);
    state=await send("getState");
   }
   return new(true);
  }catch(Exception error){return new(stopped(),error.Message);}
 }
}
