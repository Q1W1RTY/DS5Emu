using System;
using System.Threading.Tasks;
using System.Windows.Threading;
namespace DS5Link;
internal sealed partial class MainWindow {
 readonly DispatcherTimer effectTimer=new(){Interval=TimeSpan.FromMilliseconds(10)};
 readonly AudioReactiveCapture audioEffects=new();
 EffectOptions effectOptions=new();bool effectPolling,audioRunning;
 void InitializeEffects(){
  effectOptions=preview?new():EffectOptions.Load();effectTimer.Tick+=async(_,_)=>await PollEffects();
  if(!preview && effectOptions.Enabled)effectTimer.Start();
 }
 void SyncAudioEffects(BridgeState state){
  if(preview)return;bool wanted=state.State=="Active" && state.Effects.Active && state.Mode!="xbox" && state.Transport!="USB" && effectOptions.AudioEnabled && !state.Effects.RumbleOwned && !exiting && !exitRequested;
  if(wanted && !audioRunning){audioEffects.Start(effectOptions);audioRunning=true;}
  else if(!wanted && audioRunning){audioEffects.Stop();audioRunning=false;}
 }
 async Task ApplyEffectSettings(EffectOptions next){
  if(commanding || exiting || exitRequested)throw new InvalidOperationException("Дождитесь завершения текущей операции.");
  if(!next.Valid)throw new InvalidOperationException("Проверьте параметры эффектов.");
  commanding=true;++stateGeneration;Display(current);
  try{
   var state=await BridgeClient.SetEffectOptionsAsync(next);
   try{next.Save();}catch{await BridgeClient.SetEffectOptionsAsync(effectOptions);throw;}
   if(next.AudioDeviceId!=effectOptions.AudioDeviceId || next.AudioEnabled!=effectOptions.AudioEnabled){audioEffects.Stop();audioRunning=false;}
   else audioEffects.UpdateOptions(next);
   effectOptions=next;
   if(next.Enabled)effectTimer.Start();else effectTimer.Stop();
   Display(current with {Effects=state});
  }finally{commanding=false;Display(current);}
 }
 async Task PollEffects(){
  if(effectPolling || commanding || exiting || exitRequested || !effectOptions.Enabled)return;
  effectPolling=true;
  try{var state=await BridgeClient.ReadEffectsAsync(audioEffects.Level,audioTick:audioEffects.SampleTick,audioFrames:audioEffects.TakeFrames());if(!exiting && !exitRequested){current=current with {Effects=state};SyncAudioEffects(current);inputSettingsWindow?.SetEffectState(state,current.Transport=="USB" && effectOptions.AudioEnabled?"Звук → haptics пока доступен через Bluetooth":audioEffects.Error,audioEffects.Level,audioEffects.SignalPeak,audioEffects.SourceName,current.Mode=="xbox");}}
  catch(Exception){audioEffects.Stop();audioRunning=false;inputSettingsWindow?.SetEffectState(new(),"Нет связи со службой",0,xboxMode:current.Mode=="xbox");}
  finally{effectPolling=false;}
 }
}
