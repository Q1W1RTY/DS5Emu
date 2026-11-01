param([Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
if(!('DSEmuBluetoothSnapshot' -as [type])){
 Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class DSEmuBluetoothSnapshot {
 [StructLayout(LayoutKind.Sequential)] public struct SystemTime {public ushort Year,Month,DayOfWeek,Day,Hour,Minute,Second,Milliseconds;}
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] public struct Info {
  public uint Size;public ulong Address;public uint DeviceClass;
  [MarshalAs(UnmanagedType.Bool)] public bool Connected;
  [MarshalAs(UnmanagedType.Bool)] public bool Remembered;
  [MarshalAs(UnmanagedType.Bool)] public bool Authenticated;
  public SystemTime LastSeen,LastUsed;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=248)]public string Name;
 }
 [StructLayout(LayoutKind.Sequential)] struct RadioParameters{public uint Size;}
 [DllImport("bthprops.cpl")]public static extern uint BluetoothGetDeviceInfo(IntPtr radio,ref Info info);
 [DllImport("bthprops.cpl",SetLastError=true)]static extern IntPtr BluetoothFindFirstRadio(ref RadioParameters parameters,out IntPtr radio);
 [DllImport("bthprops.cpl")]static extern bool BluetoothFindRadioClose(IntPtr find);
 [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
 public static bool RadioAvailable(){var p=new RadioParameters{Size=(uint)Marshal.SizeOf(typeof(RadioParameters))};IntPtr radio;var find=BluetoothFindFirstRadio(ref p,out radio);if(find==IntPtr.Zero)return false;CloseHandle(radio);BluetoothFindRadioClose(find);return true;}
 public static Info Read(ulong address,out uint result){var info=new Info{Size=(uint)Marshal.SizeOf(typeof(Info)),Address=address};result=BluetoothGetDeviceInfo(IntPtr.Zero,ref info);return info;}
}
'@
}
$records=@(Get-Content "$env:ProgramData\DSEmu\bridge.db"|Where-Object{$_}|ForEach-Object{$_|ConvertFrom-Json})
if($records.Count -ne 1){throw 'Expected one owned configured controller'}
$address=[Convert]::ToUInt64(($records[0].address -replace ':',''),16)
$status=[uint32]0;$info=[DSEmuBluetoothSnapshot]::Read($address,[ref]$status)
[pscustomobject]@{at=[DateTime]::UtcNow.ToString('o');status=$status;connected=$info.Connected;remembered=$info.Remembered;authenticated=$info.Authenticated;radioAvailable=[DSEmuBluetoothSnapshot]::RadioAvailable()}|ConvertTo-Json|Set-Content -LiteralPath $Output -Encoding utf8
if($status -ne 0){throw "BluetoothGetDeviceInfo failed: $status"}
