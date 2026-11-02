using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
namespace DSEmu;
internal sealed record DiagnosticEvent(string Time,string Level,string Category,string Message,string Detail,int Count=1) {
 public string RepeatLabel => Count>1?"×"+Count:"";
}
internal static class Diagnostics {
 internal static DiagnosticEvent[] RecentEvents(IEnumerable<string> lines) {
  var events=new List<DiagnosticEvent>();
  string? previousMessage=null;
  foreach(var raw in lines) {
   if(string.IsNullOrWhiteSpace(raw))continue;
   var safe=Mask(raw);var parts=safe.Split('|',4);
   var structured=parts.Length==4 && parts[2] is "DEBUG" or "INFO" or "WARN" or "ERROR";
   var time=structured && DateTimeOffset.TryParse(parts[0],CultureInfo.InvariantCulture,DateTimeStyles.None,out var stamp)?stamp.ToString("HH:mm:ss",CultureInfo.InvariantCulture):"";
   var level=structured?parts[2]:"INFO";
   if(level=="DEBUG")continue;
   var scope=structured?parts[1]:"daemon";
   var category=scope switch {"bluetooth" or "hid"=>"Bluetooth","port" or "usb"=>"Виртуальный контроллер","input-audio" or "output"=>"Звук и эффекты","config"=>"Настройки","control"=>"Управление","input-control"=>"Ввод",_=>"Служба"};
   var message=Regex.Replace(structured?parts[3]:safe,@"\s+"," ").Trim();
   if(message.Length==0)continue;
   // Keep the original masked detail for tooltips; ZIP export retains all raw lines.
   if(events.Count>0 && events[^1].Level==level && events[^1].Category==category && previousMessage==message) {
    events[^1]=events[^1] with {Time=time,Detail=safe,Count=events[^1].Count+1};continue;
   }
   events.Add(new(time,level,category,message.Length>220?message[..219]+"…":message,safe));
   previousMessage=message;
  }
  return events.TakeLast(3).Reverse().ToArray();
 }
 internal static string Mask(string text) {
  text=Regex.Replace(text,@"(?i)([0-9a-f]{2}:){5}[0-9a-f]{2}","<device>");
  text=Regex.Replace(text,@"(?i)\b[0-9a-f]{12}\b","<device>");
  text=Regex.Replace(text,@"(?i)(?:HID|BTHENUM|USB)[\\#][^\s""|]+","<device-path>");
  return text.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<user>",StringComparison.OrdinalIgnoreCase);
 }
}
