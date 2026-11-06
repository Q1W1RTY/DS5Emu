using System;
using System.Runtime.InteropServices;
namespace DS5Link;
internal sealed record ServiceSnapshot(string Name,bool? Exists,string State,int? QueryError=null,uint? Win32ExitCode=null,uint? ServiceExitCode=null,uint? ProcessId=null,string? StartMode=null,string? BinaryPath=null,int? ConfigError=null);
internal static class ServiceState {
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr OpenSCManager(string? machine,string? database,uint access);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr OpenService(IntPtr manager,string name,uint access);
 [DllImport("advapi32.dll",SetLastError=true)]static extern bool QueryServiceStatusEx(IntPtr service,int level,out Status status,int size,out int needed);
 [DllImport("advapi32.dll")]static extern bool CloseServiceHandle(IntPtr handle);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool QueryServiceConfig(IntPtr service,IntPtr config,int size,out int needed);
 [StructLayout(LayoutKind.Sequential)]struct Status {public uint Type,State,Controls,Exit,Specific,Checkpoint,WaitHint,ProcessId,Flags;}
 [StructLayout(LayoutKind.Sequential)]struct Config {public uint Type,Start,Error;public IntPtr Binary,Group;public uint Tag;public IntPtr Dependencies,Account,Display;}
 internal static ServiceSnapshot Read(string name="DS5LinkBridge") {
  var manager=OpenSCManager(null,null,1);
  if(manager==IntPtr.Zero)return new(name,null,"Unavailable",Marshal.GetLastWin32Error());
  try {
   var service=OpenService(manager,name,5);var configAllowed=service!=IntPtr.Zero;
   if(service==IntPtr.Zero && Marshal.GetLastWin32Error()==5)service=OpenService(manager,name,4);
   if(service==IntPtr.Zero){int error=Marshal.GetLastWin32Error();return new(name,error==1060?false:null,error==1060?"Missing":"Unavailable",error);}
   try {
    if(!QueryServiceStatusEx(service,0,out var status,Marshal.SizeOf<Status>(),out _))return new(name,true,"Unavailable",Marshal.GetLastWin32Error());
    string? binary=null,mode=null;int? configError=configAllowed?null:5;
    if(configAllowed){
     QueryServiceConfig(service,IntPtr.Zero,0,out int size);
     if(size>0 && size<=65536){var memory=Marshal.AllocHGlobal(size);try {
      if(QueryServiceConfig(service,memory,size,out _)){var config=Marshal.PtrToStructure<Config>(memory);binary=Marshal.PtrToStringUni(config.Binary);mode=config.Start switch {0=>"Boot",1=>"System",2=>"Automatic",3=>"Manual",4=>"Disabled",_=>"Unknown"};}
      else configError=Marshal.GetLastWin32Error();
     }finally{Marshal.FreeHGlobal(memory);}}
     else configError=Marshal.GetLastWin32Error();
    }
    return new(name,true,status.State switch {1=>"Stopped",2=>"StartPending",3=>"StopPending",4=>"Running",5=>"ContinuePending",6=>"PausePending",7=>"Paused",_=>"Unknown"},null,status.Exit,status.Specific,status.ProcessId,mode,binary,configError);
   }finally{CloseServiceHandle(service);}
  }finally{CloseServiceHandle(manager);}
 }
 internal static bool StoppedOrMissing() {
  var manager=OpenSCManager(null,null,1);if(manager==IntPtr.Zero)return false;
  try{var service=OpenService(manager,"DS5LinkBridge",4);if(service==IntPtr.Zero)return Marshal.GetLastWin32Error()==1060;
   try{return QueryServiceStatusEx(service,0,out var status,Marshal.SizeOf<Status>(),out _) && status.State==1;}finally{CloseServiceHandle(service);}
  }finally{CloseServiceHandle(manager);}
 }
}
