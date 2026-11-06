using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
namespace DS5Link;

internal static class ForegroundExecutable {
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
 [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,uint id);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder path,ref int size);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
 internal static (bool Own,string Path) Read(){
  GetWindowThreadProcessId(GetForegroundWindow(),out uint id);
  if(id==Environment.ProcessId)return (true,"");
  var handle=OpenProcess(0x1000,false,id);
  if(handle==IntPtr.Zero)return (false,"");
  try{var path=new StringBuilder(32768);int size=path.Capacity;return (false,QueryFullProcessImageName(handle,0,path,ref size)?path.ToString():"");}
  finally{CloseHandle(handle);}
 }
}

internal sealed partial class MainWindow {
 readonly DispatcherTimer profileTimer=new(){Interval=TimeSpan.FromMilliseconds(500)};
 GameProfiles profiles=new();
 string activeProfile="",profileLoadError="";
 bool profileSwitching,profileApplied;
 long nextProfileRetry;
 int profileRevision,appliedProfileRevision=-1;
 void InitializeGameProfiles(){
  if(preview)return;
  try{profiles=GameProfiles.Load(GameProfiles.FilePath,inputProfile,effectOptions);}
  catch(Exception error){profileLoadError=error.Message;return;}
  profileTimer.Tick+=async(_,_)=>await RefreshGameProfile();
  profileTimer.Start();
 }
 async Task SaveProfile(GameProfile profile){
  var next=profiles.Put(profile);next.Save(GameProfiles.FilePath);profiles=next;++profileRevision;
  await RefreshGameProfile();
 }
 async Task DeleteProfile(string executable){
  if(executable=="")return;
  var next=profiles.Remove(executable);next.Save(GameProfiles.FilePath);profiles=next;++profileRevision;
  await RefreshGameProfile();
 }
 async Task RefreshGameProfile(){
  if(preview || profileLoadError!="" || profileSwitching || commanding || exiting || exitRequested || Environment.TickCount64<nextProfileRetry)return;
  var foreground=ForegroundExecutable.Read();
  var target=profiles.Find(foreground.Own?activeProfile:foreground.Path);
  if(profileApplied && activeProfile==target.Executable && appliedProfileRevision==profileRevision)return;
  profileSwitching=true;
  // Invalidate pending touch events before changing either half of the profile.
  KeyboardOutput.ReleaseAll();inputCursor.Reset();
  var oldInput=inputProfile;var oldEffects=effectOptions;int revision=profileRevision;
  try{
   await ProfileApplication.ApplyAsync(target,new(){Input=oldInput,Effects=oldEffects},ApplyInputSettings,ApplyEffectSettings);
   activeProfile=target.Executable;appliedProfileRevision=revision;profileApplied=true;
   inputSettingsWindow?.SetActiveProfile(target.Label);
  }catch(Exception error){
   profileApplied=false;nextProfileRetry=Environment.TickCount64+3000;
   inputSettingsWindow?.SetActiveProfile(L.T("Ошибка применения: ")+error.Message);
  }finally{profileSwitching=false;}
 }
 bool ProfileAllowsKeyboard(){
  if(profileSwitching || !profileApplied)return false;
  var foreground=ForegroundExecutable.Read();
  return !foreground.Own && string.Equals(profiles.Find(foreground.Path).Executable,activeProfile,StringComparison.OrdinalIgnoreCase);
 }
}
