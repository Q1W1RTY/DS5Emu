using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace DS5Link;

internal static class L {
 static readonly Dictionary<string,string> English=Load();
 static readonly Dictionary<string,string> Keys=English.Keys.ToDictionary(Key,k=>k);
 internal static string Language {get;private set;}="ru";
 static Dictionary<string,string> Load(){
  using var stream=typeof(L).Assembly.GetManifestResourceStream("DS5Link.Strings.en.json")??throw new InvalidDataException("Missing language resources");
  return JsonSerializer.Deserialize<Dictionary<string,string>>(stream)??throw new InvalidDataException("Invalid language resources");
 }
 internal static string Key(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).Substring(0,12).ToLowerInvariant();
 internal static void SetLanguage(string? language)=>Language=language=="en"?"en":"ru";
 internal static string T(string text)=>Language=="en" && English.TryGetValue(text,out var value)?value:text;
 internal static string F(string format,params object?[] args)=>string.Format(CultureInfo.GetCultureInfo(Language=="en"?"en-US":"ru-RU"),T(format),args);
 internal static string ByKey(string key)=>T(Keys.TryGetValue(key,out var text)?text:throw new InvalidDataException("Unknown language resource: "+key));
 internal static bool HasPrefix(string text,string prefix)=>text.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||text.StartsWith(T(prefix),StringComparison.OrdinalIgnoreCase)||(Language=="en" && English.Any(p=>p.Key.StartsWith(prefix,StringComparison.OrdinalIgnoreCase) && text.StartsWith(p.Value,StringComparison.OrdinalIgnoreCase)));
 internal static bool IsTranslated(string text)=>Language=="en" && English.Values.Contains(text);
}

public sealed class TrExtension:Avalonia.Markup.Xaml.MarkupExtension {
 public string Key {get;set;}="";
 public override object ProvideValue(IServiceProvider serviceProvider)=>L.ByKey(Key);
}
