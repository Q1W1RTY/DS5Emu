using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;

namespace DS5Link;

internal sealed partial class MainWindow {
 async void ToggleLanguage(object sender,RoutedEventArgs e)=>await ChangeLanguage(L.Language=="ru"?"en":"ru");
 void UpdateLanguageButton(){
  LanguageButton.Tag=L.Language;
  LanguageButton.Content=L.Language=="ru"?"RU":"EN";
 }
 bool changingLanguage;
 async Task ChangeLanguage(string language){
  if(changingLanguage || language==L.Language || preview)return;
  if(commanding || exporting || exiting || exitRequested){Notice(L.T("Дождитесь завершения текущей операции."));return;}
  changingLanguage=true;
  try{
   if(inputSettingsWindow!=null){
    if(!await inputSettingsWindow.PrepareLanguageChange())return;
    inputSettingsWindow.Close();
   }
   exitRequested=true;++stateGeneration;
   var wait=Stopwatch.StartNew();
   while(updating || inputPolling || effectPolling || profileSwitching){
    if(wait.Elapsed>TimeSpan.FromSeconds(5))throw new InvalidOperationException(L.T("Дождитесь завершения текущей операции."));
    await Task.Delay(20);
   }
   var settings=Settings.Load();settings.Language=language;settings.Save();
   bool visible=IsVisible;double left=Left,top=Top;
   profileTimer.Stop();effectTimer.Stop();inputTimer.Stop();timer.Stop();footerResetTimer?.Stop();
   KeyboardOutput.ReleaseAll();audioEffects.Dispose();tray?.ContextMenuStrip?.Dispose();tray?.Icon?.Dispose();tray?.Dispose();
   exiting=true;Close();
   L.SetLanguage(language);
   var window=new MainWindow(false){Left=left,Top=top,activeProfile=activeProfile};
   window.Display(current);
   Application.Current.MainWindow=window;
   if(visible)window.Show();
  }catch(Exception error){exitRequested=false;Notice(error.Message);}
  finally{changingLanguage=false;}
 }
}

internal sealed partial class InputSettingsWindow {
 internal async Task<bool> PrepareLanguageChange(){
  autoApplyTimer.Stop();IsEnabled=false;
  bool saved=await ApplyPendingSettings();
  if(saved){autoReady=false;allowClose=true;}else IsEnabled=true;
  return saved;
 }
}
