using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace DS5Link;

internal sealed partial class InputSettingsWindow {
 Func<GameProfiles>? readProfiles;
 Func<GameProfile,Task>? saveProfile;
 Func<string,Task>? deleteProfile;
 GameProfile editingProfile=new();
 bool selectingProfile;

 internal void InitializeProfiles(Func<GameProfiles> read,Func<GameProfile,Task> save,Func<string,Task> delete,string active,string error=""){
  readProfiles=read;saveProfile=save;deleteProfile=delete;
  ProfileBar.IsEnabled=!preview && error=="";
  if(error!=""){InputSettingsNotice.Text=error;InputSettingsNotice.Visibility=Visibility.Visible;SettingsTabs.IsEnabled=false;return;}
  editingProfile=read().Find(active);
  RefreshProfileList();LoadProfileDraft();SetActiveProfile(read().Find(active).Label);
 }
 internal void SetActiveProfile(string label)=>ActiveProfileText.Text="Сейчас: "+label;
 void RefreshProfileList(){
  selectingProfile=true;
  try{ProfileSelector.ItemsSource=new[]{readProfiles!().Default}.Concat(readProfiles().Games.OrderBy(p=>p.Label)).ToArray();ProfileSelector.SelectedItem=readProfiles().Find(editingProfile.Executable);}
  finally{selectingProfile=false;}
  RemoveProfileButton.IsEnabled=editingProfile.Executable!="";
  ProfilePath.Text=editingProfile.Executable==""?"Для приложений без отдельного профиля":editingProfile.Executable;
 }
 void LoadProfileDraft(){
  bool ready=autoReady;autoReady=false;CancelCapture();
  try{
   var input=editingProfile.Input;bindings.Clear();foreach(var item in input.Bindings)bindings.Add(item.Key,item.Value);
   foreach(var item in mappingButtons)item.Value.Content=BindingLabel(item.Key);
   TouchBindingsEnabled.IsChecked=input.Options.TouchEnabled;ConsumeTouchpad.IsChecked=input.Options.ConsumeTouchpad;
   LeftDeadzone.Value=input.Options.LeftDeadzone;RightDeadzone.Value=input.Options.RightDeadzone;
   LoadEffectDraft(editingProfile.Effects);
   if(!preview)LoadAudioDevices(editingProfile.Effects.AudioDeviceId);
  }finally{autoReady=ready;}
 }
 async void SelectProfile(object sender,SelectionChangedEventArgs e){
  if(selectingProfile || readProfiles==null || ProfileSelector.SelectedItem is not GameProfile selected || selected.Executable==editingProfile.Executable)return;
  ProfileBar.IsEnabled=false;SettingsTabs.IsEnabled=false;
  try{if(await ApplyPendingSettings()){editingProfile=readProfiles().Find(selected.Executable);LoadProfileDraft();}RefreshProfileList();}
  finally{ProfileBar.IsEnabled=true;SettingsTabs.IsEnabled=true;}
 }
 async void AddGameProfile(object sender,RoutedEventArgs e){
  if(readProfiles==null || saveProfile==null)return;
  ProfileBar.IsEnabled=false;SettingsTabs.IsEnabled=false;
  try{
   if(!await ApplyPendingSettings())return;
   var picker=new OpenFileDialog {Title="Выберите EXE самой игры (не лаунчер)",Filter="Приложения (*.exe)|*.exe",CheckFileExists=true,Multiselect=false};
   if(picker.ShowDialog(this)!=true)return;
   var existing=readProfiles().Games.FirstOrDefault(p=>string.Equals(p.Executable,picker.FileName,StringComparison.OrdinalIgnoreCase));
   var next=existing??editingProfile with {Executable=System.IO.Path.GetFullPath(picker.FileName)};
   if(existing==null)await saveProfile(next);
   editingProfile=next;RefreshProfileList();LoadProfileDraft();
  }catch(Exception error){InputSettingsNotice.Text=error.Message;InputSettingsNotice.Visibility=Visibility.Visible;}
  finally{ProfileBar.IsEnabled=true;SettingsTabs.IsEnabled=true;}
 }
 async void RemoveGameProfile(object sender,RoutedEventArgs e){
  if(deleteProfile==null || readProfiles==null || editingProfile.Executable=="")return;
  if(MessageBox.Show(this,"Удалить профиль «"+editingProfile.Label+"»? Игра будет использовать общие настройки.","Профили игр",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
  ProfileBar.IsEnabled=false;SettingsTabs.IsEnabled=false;
  try{if(!await ApplyPendingSettings())return;await deleteProfile(editingProfile.Executable);editingProfile=readProfiles().Default;RefreshProfileList();LoadProfileDraft();}
  catch(Exception error){InputSettingsNotice.Text=error.Message;InputSettingsNotice.Visibility=Visibility.Visible;}
  finally{ProfileBar.IsEnabled=true;SettingsTabs.IsEnabled=true;}
 }
 async Task SaveProfileInput(InputProfile input){
  var next=editingProfile with {Input=input};await saveProfile!(next);editingProfile=next;
 }
 async Task SaveProfileEffects(EffectOptions effects){
  var next=editingProfile with {Effects=effects};await saveProfile!(next);editingProfile=next;
 }
}
