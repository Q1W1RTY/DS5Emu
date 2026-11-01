using System;
using System.Runtime.InteropServices;
namespace DSEmu;
internal static class ServiceState {
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr OpenSCManager(string? machine,string? database,uint access);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr OpenService(IntPtr manager,string name,uint access);
 [DllImport("advapi32.dll",SetLastError=true)]static extern bool QueryServiceStatusEx(IntPtr service,int level,out Status status,int size,out int needed);
 [DllImport("advapi32.dll")]static extern bool CloseServiceHandle(IntPtr handle);
 [StructLayout(LayoutKind.Sequential)]struct Status {public uint Type,State,Controls,Exit,Specific,Checkpoint,WaitHint,ProcessId,Flags;}
 internal static bool StoppedOrMissing() {
  var manager=OpenSCManager(null,null,1);if(manager==IntPtr.Zero)return false;
  try{var service=OpenService(manager,"DSEmuBridge",4);if(service==IntPtr.Zero)return Marshal.GetLastWin32Error()==1060;
   try{return QueryServiceStatusEx(service,0,out var status,Marshal.SizeOf<Status>(),out _) && status.State==1;}finally{CloseServiceHandle(service);}
  }finally{CloseServiceHandle(manager);}
 }
}
