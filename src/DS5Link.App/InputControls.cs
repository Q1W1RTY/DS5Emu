using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
namespace DS5Link;
internal sealed partial class MainWindow {
 readonly DispatcherTimer inputTimer=new(){Interval=TimeSpan.FromMilliseconds(30)};
 readonly InputEventCursor inputCursor=new();
 InputProfile inputProfile=new();
 InputSettingsWindow? inputSettingsWindow;
 bool inputPolling,keyboardErrorShown;
 void InitializeInputControls() {
  inputProfile=preview?new():InputProfile.Load();
  inputTimer.Tick+=async(_,_)=>await PollInput();
  if(!preview && inputProfile.Options.TouchEnabled)inputTimer.Start();
 }
 internal InputSettingsWindow CreateInputSettingsWindow(){
  var window=new InputSettingsWindow(inputProfile,ApplyInputSettings,preview,effectOptions,ApplyEffectSettings);
  window.SetEffectState(current.Effects,audioEffects.Error,audioEffects.Level,audioEffects.SignalPeak,audioEffects.SourceName,current.Mode=="xbox");return window;
 }
 void ToggleInputSettings(object sender,RoutedEventArgs e)=>OpenInputSettings();
 void OpenInputSettings(){
  if(inputSettingsWindow!=null){if(inputSettingsWindow.WindowState==WindowState.Minimized)inputSettingsWindow.WindowState=WindowState.Normal;inputSettingsWindow.Show();inputSettingsWindow.Activate();return;}
  inputSettingsWindow=CreateInputSettingsWindow();inputSettingsWindow.Owner=this;
  inputSettingsWindow.Closed+=(_,_)=>inputSettingsWindow=null;
  inputSettingsWindow.Show();
 }
 async Task ApplyInputSettings(InputProfile next) {
  if(commanding || exiting || exitRequested)throw new InvalidOperationException("Дождитесь завершения текущей операции.");
  commanding=true;++stateGeneration;KeyboardOutput.ReleaseAll();inputCursor.Reset();Display(current);
  try {
   var state=await BridgeClient.SetInputOptionsAsync(next.Options);
   try{next.Save();}catch{await BridgeClient.SetInputOptionsAsync(inputProfile.Options);throw;}
   inputProfile=next;keyboardErrorShown=false;Display(state);
   if(next.Options.TouchEnabled)inputTimer.Start();else inputTimer.Stop();
  }finally{commanding=false;Display(current);}
 }
 async Task PollInput() {
  if(inputPolling || commanding || exiting || exitRequested || !inputProfile.Options.TouchEnabled)return;
  inputPolling=true;
  try {
   var batch=await BridgeClient.ReadInputEventsAsync(inputCursor.Sequence);
   var received=Stopwatch.StartNew();
   foreach(var action in inputCursor.Read(batch).TakeLast(4)){
    if(commanding || exiting || exitRequested || IsActive || inputSettingsWindow?.IsActive==true || action.AgeMs+received.ElapsedMilliseconds>150)continue;
    if(inputProfile.Bindings.TryGetValue(action.Action,out var binding) && !await KeyboardOutput.SendAsync(binding) && !keyboardErrorShown){keyboardErrorShown=true;Notice("Windows не приняла назначение тачпада. Проверьте права приложения и игры.");}
   }
  }catch(Exception){inputCursor.Reset();KeyboardOutput.ReleaseAll();}
  finally{inputPolling=false;}
 }
}
