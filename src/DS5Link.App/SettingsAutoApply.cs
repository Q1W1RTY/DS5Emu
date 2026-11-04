using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace DS5Link;
// A keyed, serialized queue: changes made during an outstanding request
// replace the pending value rather than racing or replaying stale drafts.
internal sealed class LatestSettingsApplier {
 readonly Dictionary<string,Func<Task>> pending=new();Task? running;
 internal bool HasPending=>pending.Count>0;internal bool IsRunning=>running is {IsCompleted:false};
 internal void Queue(string key,Func<Task> apply)=>pending[key]=apply;
 internal Task FlushAsync(){if(IsRunning)return running!;return running=Run();}
 async Task Run(){while(pending.Count>0){var item=pending.First();pending.Remove(item.Key);try{await item.Value();}catch{if(!pending.ContainsKey(item.Key))pending[item.Key]=item.Value;throw;}}}
}
internal sealed partial class InputSettingsWindow {
 readonly LatestSettingsApplier autoApplier=new();
 readonly DispatcherTimer autoApplyTimer=new(){Interval=TimeSpan.FromMilliseconds(120)};
 bool autoReady,allowClose,closingFlush;
 void InitializeAutoApply(){
  autoApplyTimer.Tick+=async(_,_)=>{autoApplyTimer.Stop();await ApplyPendingSettings();};
  TouchBindingsEnabled.Checked+=(_,_)=>QueueAutoApply(false);TouchBindingsEnabled.Unchecked+=(_,_)=>QueueAutoApply(false);
  ConsumeTouchpad.Checked+=(_,_)=>QueueAutoApply(false);ConsumeTouchpad.Unchecked+=(_,_)=>QueueAutoApply(false);
  LeftDeadzone.ValueChanged+=(_,_)=>QueueAutoApply(false);RightDeadzone.ValueChanged+=(_,_)=>QueueAutoApply(false);
  leftTrigger.Changed+=()=>QueueAutoApply(true);rightTrigger.Changed+=()=>QueueAutoApply(true);
  LedMode.SelectionChanged+=(_,_)=>QueueAutoApply(true);AudioDevice.SelectionChanged+=(_,_)=>QueueAutoApply(true);
  foreach(var field in new[]{redSlider,greenSlider,blueSlider,sensitivitySlider,limitSlider,gateSlider})field.Slider.ValueChanged+=(_,_)=>QueueAutoApply(true);
  LedBrightness.ValueChanged+=(_,_)=>QueueAutoApply(true);LedSpeed.ValueChanged+=(_,_)=>QueueAutoApply(true);
  AudioEffectsEnabled.Checked+=(_,_)=>QueueAutoApply(true);AudioEffectsEnabled.Unchecked+=(_,_)=>QueueAutoApply(true);
  AudioBassOnly.Checked+=(_,_)=>QueueAutoApply(true);AudioBassOnly.Unchecked+=(_,_)=>QueueAutoApply(true);
  autoReady=true;
 }
 void QueueAutoApply(bool effects){
  if(!autoReady || preview || closed)return;
  autoApplier.Queue(effects?"effects":"input",effects?ApplyEffectDraftAsync:ApplyTouchDraftAsync);
  InputSettingsNotice.Text="";InputSettingsNotice.Visibility=Visibility.Collapsed;autoApplyTimer.Stop();autoApplyTimer.Start();
 }
 async Task<bool> ApplyPendingSettings(){
  if(closed)return false;
  try{await autoApplier.FlushAsync();InputSettingsNotice.Text="";InputSettingsNotice.Visibility=Visibility.Collapsed;return true;}
  catch(Exception error){InputSettingsNotice.Text="Не удалось применить: "+error.Message;InputSettingsNotice.Visibility=Visibility.Visible;return false;}
 }
 async void OnSettingsClosing(object? sender,CancelEventArgs e){
  if(!allowClose && (autoApplier.HasPending || autoApplier.IsRunning)){
   e.Cancel=true;if(closingFlush)return;closingFlush=true;autoApplyTimer.Stop();
   bool ok=await ApplyPendingSettings();closingFlush=false;if(ok || MessageBox.Show(this,"Не удалось сохранить изменения. Закрыть окно и отказаться от неприменённых изменений?","Настройки",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes){allowClose=true;Close();}return;
  }
  closed=true;autoApplyTimer.Stop();previewTimer.Stop();CancelCapture();
 }
 async Task ApplyTouchDraftAsync(){
  var next=new InputProfile {Options=new(){LeftDeadzone=(int)LeftDeadzone.Value,RightDeadzone=(int)RightDeadzone.Value,TouchEnabled=TouchBindingsEnabled.IsChecked==true,ConsumeTouchpad=ConsumeTouchpad.IsChecked==true},Bindings=new(bindings)};
  if(!next.Valid)throw new InvalidOperationException("Назначьте хотя бы одну клавишу для тачпада.");
  if(saveProfile!=null)await SaveProfileInput(next);else await apply(next);
 }
}
