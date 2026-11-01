using System;
using System.Text.RegularExpressions;
namespace DSEmu;
internal static class Diagnostics {
 internal static string Mask(string text) {
  text=Regex.Replace(text,@"(?i)([0-9a-f]{2}:){5}[0-9a-f]{2}","<device>");
  text=Regex.Replace(text,@"(?i)\b[0-9a-f]{12}\b","<device>");
  text=Regex.Replace(text,@"(?i)(?:HID|BTHENUM|USB)[\\#][^\s""|]+","<device-path>");
  return text.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<user>",StringComparison.OrdinalIgnoreCase);
 }
}
