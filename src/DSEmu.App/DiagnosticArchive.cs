using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace DSEmu;
internal sealed record DiagnosticSource(string Name,Func<string> Read);
internal sealed record DiagnosticSourceStatus(string Name,string Status,string? Error=null);
internal sealed record DiagnosticExportResult(string Path,bool BridgeReachable,int Collected,int Unavailable);
internal static class DiagnosticArchive {
 internal static string Json(object value) {
  var node=JsonSerializer.SerializeToNode(value,BridgeClient.JsonOptions);
  JsonNode? MaskNode(JsonNode? item) {
   if(item is JsonObject obj){foreach(var key in obj.Select(p=>p.Key).ToArray())obj[key]=MaskNode(obj[key]?.DeepClone());}
   else if(item is JsonArray array){for(int i=0;i<array.Count;i++)array[i]=MaskNode(array[i]?.DeepClone());}
   else if(item is JsonValue scalar && scalar.TryGetValue<string>(out var text))return JsonValue.Create(Diagnostics.Mask(text));
   return item;
  }
  return MaskNode(node)?.ToJsonString(BridgeClient.JsonOptions)??"null";
 }
 internal static async Task<DiagnosticExportResult> SaveAsync(string path,BridgeState fallback,Func<Task<BridgeState>> request,IEnumerable<DiagnosticSource>? sources=null) {
  var localTasks=(sources??WindowsDiagnostics.Sources()).Select(source=>Task.Run(()=> {
   try{return (source.Name,Text:source.Read(),Status:new DiagnosticSourceStatus(source.Name,"collected"));}
   catch(Exception error){return (source.Name,Text:(string?)null,Status:new DiagnosticSourceStatus(source.Name,error switch {FileNotFoundException or DirectoryNotFoundException=>"missing",UnauthorizedAccessException=>"accessDenied",TimeoutException=>"timeout",_=>"error"},Diagnostics.Mask(error.GetType().Name+": "+error.Message)));}
  })).ToArray();
  var watch=Stopwatch.StartNew();bool reachable=false;string? bridgeError=null,errorType=null;int? hresult=null;BridgeState state=fallback;
  try {state=await request();reachable=true;}
  catch(Exception error){bridgeError=error.Message;errorType=error.GetType().FullName;hresult=error.HResult;}
  watch.Stop();
  var local=await Task.WhenAll(localTasks);
  var temporary=path+".partial";
  bool created=false;
  try {
   using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
   created=true;
   using(var zip=new ZipArchive(file,ZipArchiveMode.Create,false)) {
    void Add(string name,string content){using var writer=new StreamWriter(zip.CreateEntry(name).Open(),new UTF8Encoding(false));writer.Write(content);}
    Add("status.json",Json(new {schemaVersion=2,appVersion=typeof(DiagnosticArchive).Assembly.GetName().Version?.ToString(),windows=Environment.OSVersion.VersionString,state}));
    Add("connection.json",Json(new {command="getDiagnostics",pipe="DSEmu.Control.v1",reachable,elapsedMs=watch.ElapsedMilliseconds,errorType,hresult,error=bridgeError,stateIsCached=!reachable}));
    foreach(var source in local)if(source.Text!=null)Add(source.Name,source.Name.EndsWith(".json",StringComparison.OrdinalIgnoreCase)?Json(JsonNode.Parse(source.Text)!):Diagnostics.Mask(source.Text));
    Add("manifest.json",Json(new {collectedAt=DateTimeOffset.Now,sources=local.Select(s=>s.Status),bridgeReachable=reachable}));
    Add("README.txt","DS5Emu diagnostic snapshot\nLocal collection works independently of the bridge service.\nstatus.json may contain cached UI state; see connection.json.\nmanifest.json lists collected, missing, accessDenied, timeout and failed sources.\nLogs are bounded tails, not the complete history. Device identifiers and user identities are masked.\nThe collector does not start/stop services or modify driver settings.\n");
   }
   }
   File.Move(temporary,path,false);
  }catch{if(created)try{File.Delete(temporary);}catch(IOException){}throw;}
  return new(path,reachable,local.Count(s=>s.Status.Status=="collected"),local.Count(s=>s.Status.Status!="collected"));
 }
 internal static string ReadTail(string path,int limit=512*1024) {
  using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
  byte[] header=new byte[3];int headerSize=file.Read(header);Encoding encoding=Encoding.UTF8;
  if(headerSize>=2 && header[0]==255 && header[1]==254)encoding=Encoding.Unicode;
  else if(headerSize>=2 && header[0]==254 && header[1]==255)encoding=Encoding.BigEndianUnicode;
  long start=Math.Max(0,file.Length-limit);if(encoding!=Encoding.UTF8)start-=start%2;
  file.Position=start;byte[] bytes=new byte[(int)Math.Min(limit+2,file.Length-start)];int read=0,count;
  while(read<bytes.Length && (count=file.Read(bytes,read,bytes.Length-read))>0)read+=count;
  var text=encoding.GetString(bytes,0,read).TrimStart('\uFEFF');
  if(start>0){int newline=text.IndexOf('\n');if(newline>=0)text=text[(newline+1)..];text="[tail: original file "+file.Length+" bytes]\n"+text;}
  return text;
 }
}
