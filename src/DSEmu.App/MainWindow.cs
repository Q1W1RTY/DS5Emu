using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms=System.Windows.Forms;
using Brush=System.Windows.Media.Brush;
using Color=System.Windows.Media.Color;

namespace DSEmu;
internal sealed partial class MainWindow:Window {
 readonly Dictionary<string,TextBlock> metrics=new();
 readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(1)};
 readonly Forms.NotifyIcon? tray;
 readonly Forms.ToolStripMenuItem? startupMenu;
 readonly bool preview;
 BridgeState current=new();
 bool updating,commanding,exiting,lowBatteryNotified,diagnosticsOpen,exporting,syncingStartup;
 int stateGeneration;
 string notifiedError="",trayState="";
 DateTime lastDiagnostics=DateTime.MinValue;
 string[] diagnosticLines=Array.Empty<string>();
 static Brush Ink=>new SolidColorBrush(Color.FromRgb(240,241,243));
 static Brush Muted=>new SolidColorBrush(Color.FromRgb(150,155,165));
 static Brush Accent=>new SolidColorBrush(Color.FromRgb(169,216,195));

 public MainWindow(bool preview) {
  this.preview=preview;
  InitializeComponent();
  AddMetric(ConnectionMetrics,"Пакеты / с");AddMetric(ConnectionMetrics,"Паузы > 10 мс");
  AddMetric(ConnectionMetrics,"Макс. пауза");AddMetric(ConnectionMetrics,"Окно наблюдения");
  AddMetric(AudioMetrics,"Игровое аудио");AddMetric(AudioMetrics,"Микрофон");
  AddMetric(AudioMetrics,"Наушники");AddMetric(AudioMetrics,"RSSI");
  SourceInitialized+=(_,_)=>{int dark=1;_ = DwmSetWindowAttribute(new WindowInteropHelper(this).Handle,20,ref dark,sizeof(int));};
  KeyDown+=(_,e)=>{if(e.Key==Key.F12){SetDiagnosticsOpen(!diagnosticsOpen);e.Handled=true;}};
  Closing+=(_,e)=>{if(!exiting){e.Cancel=true;Hide();}};
  StateChanged+=(_,_)=>{if(WindowState==WindowState.Minimized)Hide();};
  if(preview){Display(new());return;}
  StartupSwitch.IsChecked=Settings.AutoStart;
  StartupSwitch.Checked+=(_,_)=>ChangeStartup(true);StartupSwitch.Unchecked+=(_,_)=>ChangeStartup(false);
  tray=new Forms.NotifyIcon{Visible=true,Icon=MakeIcon("Waiting"),Text="DSEmu · ожидание"};
  tray.DoubleClick+=(_,_)=>Open();
  var menu=new Forms.ContextMenuStrip();
  menu.Items.Add("Открыть окно",null,(_,_)=>Open());
  menu.Items.Add("Включить / выключить эмуляцию PS5",null,async(_,_)=>await Command(current.Enabled?"pause":"enable"));
  menu.Items.Add("Переподключить",null,async(_,_)=>await Command("reconnect"));
  menu.Items.Add("Отключить геймпад",null,async(_,_)=>await Command("disconnect"));
  startupMenu=new Forms.ToolStripMenuItem("Автозапуск"){Checked=StartupSwitch.IsChecked==true,CheckOnClick=true};
  startupMenu.CheckedChanged+=(_,_)=>ChangeStartup(startupMenu.Checked);menu.Items.Add(startupMenu);
  menu.Items.Add("Диагностика",null,(_,_)=>{Open();SetDiagnosticsOpen(true);});
  menu.Items.Add("Сохранить диагностику",null,async(_,_)=>await ExportDiagnostics());
  menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add("Выход",null,async(_,_)=>await Exit());
  tray.ContextMenuStrip=menu;
  timer.Tick+=async(_,_)=>await Refresh();Display(new());_ = Initialize();
 }
 void AddMetric(StackPanel panel,string name){
  var row=new Grid{Height=25};row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
  row.Children.Add(new TextBlock{Text=name,Foreground=Muted,FontSize=11});
  var value=new TextBlock{Text="—",Foreground=Ink,FontSize=11};Grid.SetColumn(value,1);row.Children.Add(value);metrics[name]=value;panel.Children.Add(row);
 }
 public void Display(BridgeState state){
  current=state;
  var name=state.State switch{"Active"=>"Подключён","Paused"=>state.BluetoothDisconnected?"Отключён":"Приостановлено","Wired"=>"Кабель USB","Conflict"=>"Занят DSX","Stopping"=>"Отключение…","Connecting"=>"Подключение…","Error"=>state.Error.StartsWith("Нет связи со службой")?"Нет связи":"Ошибка",_=>"Ожидание"};
  var color=state.State switch{"Active"=>Accent,"Error"=>new SolidColorBrush(Color.FromRgb(233,180,168)),"Connecting" or "Stopping"=>new SolidColorBrush(Color.FromRgb(223,198,146)),_=>Muted};
  BadgeText.Text=StatusText.Text=name;BadgeText.Foreground=StatusDot.Fill=color;
  StatusBadge.Background=new SolidColorBrush(state.State=="Active"?Color.FromRgb(32,40,37):Color.FromRgb(34,36,41));
  StatusBadge.BorderBrush=new SolidColorBrush(state.State=="Active"?Color.FromRgb(52,70,62):Color.FromRgb(51,55,62));
  BatteryText.Text=state.BatteryPercent is int battery?$"≈{battery}%":"—";
  BatteryText.ToolTip="Приблизительный уровень по данным контроллера";
  var charge=Math.Clamp(state.BatteryPercent??0,0,100);
  BatteryFilled.Width=new GridLength(charge,GridUnitType.Star);BatteryEmpty.Width=new GridLength(100-charge,GridUnitType.Star);
  BatteryFill.Background=state.BatteryPercent is <=20?new SolidColorBrush(Color.FromRgb(223,198,146)):Accent;
  ConnectionText.Text=state.State is "Active" or "Wired"?state.Transport:"—";
  SignalText.Text=state.State!="Active"?"—":state.Rssi is int rssi?$"{rssi} dBm":"Нет данных";
  ControlDetail.Text=!string.IsNullOrEmpty(state.Error)?FriendlyError(state.Error):state.State switch{
   "Active"=>"","Paused"=>state.BluetoothDisconnected?"Bluetooth отключён. Для подключения нажмите PS.":"Эмуляция PS5 выключена.",
   "Connecting"=>"Подключаем контроллер…","Stopping"=>"Освобождаем контроллер…",_=>"Включите контроллер кнопкой PS."};
  ControlDetail.Visibility=string.IsNullOrEmpty(ControlDetail.Text)?Visibility.Collapsed:Visibility.Visible;
  ToggleButton.Content=state.Enabled?"Выключить эмуляцию PS5":"Включить эмуляцию PS5";
  ToggleButton.IsEnabled=!commanding && state.State!="Stopping";
  ReconnectButton.IsEnabled=state.Enabled && !commanding && state.State is not ("Wired" or "Conflict" or "Stopping");
  DisconnectButton.IsEnabled=!commanding && !state.BluetoothDisconnected && state.State is not ("Wired" or "Conflict" or "Stopping" or "Error");
  metrics["Пакеты / с"].Text=state.PacketsPerSecond.ToString("F1");metrics["Паузы > 10 мс"].Text=state.GapsOver10ms.ToString();
  metrics["Макс. пауза"].Text=$"{state.MaxGapMs:F1} мс";metrics["Окно наблюдения"].Text=$"{state.ObservedSeconds:F0} с";
  metrics["Игровое аудио"].Text=state.AudioActive?"есть поток":"нет потока";
  metrics["Микрофон"].Text=state.Muted?"mute":state.MicrophoneActive?"есть поток":"нет потока";
  metrics["Наушники"].Text=state.State=="Active"?(state.Headphones?"подключены":"не подключены"):"—";
  metrics["RSSI"].Text=state.Rssi is int signal?$"{signal} dBm":"недоступен";
  DiagnosticError.Text=Diagnostics.Mask(state.Error);DiagnosticError.Visibility=string.IsNullOrEmpty(state.Error)?Visibility.Collapsed:Visibility.Visible;
  if(state.LogLines.Length>0){diagnosticLines=state.LogLines;var text=string.Join(Environment.NewLine,state.LogLines.Select(Diagnostics.Mask));if(LogText.Text!=text){LogText.Text=text;LogText.ScrollToEnd();}DiagnosticsUpdated.Text="Обновлено "+DateTime.Now.ToString("HH:mm:ss");}
  if(tray!=null){
   tray.Text="DSEmu · "+name+(state.BatteryPercent is int b?$" · ≈{b}%":"");
   if(trayState!=state.State){var old=tray.Icon;tray.Icon=MakeIcon(state.State);old?.Dispose();trayState=state.State;}
   if(state.BatteryPercent is >30 || state.Charging)lowBatteryNotified=false;
   if(state.BatteryPercent is <=20 && !state.Charging && !lowBatteryNotified){lowBatteryNotified=true;Notify("Низкий заряд DualSense",$"Осталось приблизительно {state.BatteryPercent}%.");}
   if(state.State=="Error" && state.Error!=notifiedError){notifiedError=state.Error;Notify("DSEmu",ControlDetail.Text);}
  }
 }
 internal void SetDiagnosticsOpen(bool open){
  diagnosticsOpen=open;DiagnosticsPanel.Visibility=open?Visibility.Visible:Visibility.Collapsed;
  AdvancedButton.Content=open?"Скрыть диагностику  ‹":"Дополнительно  ›";
  Height=Math.Min(open?900:520,SystemParameters.WorkArea.Height-32);
  if(open){lastDiagnostics=DateTime.MinValue;if(!preview)_=Refresh();}
 }
 void ChangeStartup(bool enabled){
  if(syncingStartup)return;
  syncingStartup=true;
  try{Settings.AutoStart=enabled;StartupSwitch.IsChecked=enabled;if(startupMenu!=null)startupMenu.Checked=enabled;}
  catch(Exception e){var actual=Settings.AutoStart;StartupSwitch.IsChecked=actual;if(startupMenu!=null)startupMenu.Checked=actual;Notice("Не удалось изменить автозапуск: "+e.Message);}
  finally{syncingStartup=false;}
 }
 static string FriendlyError(string message){
  if(message.Contains("Enabling HidHide would affect"))return "В HidHide настроены другие устройства. Подробности в диагностике.";
  if(message.Contains("inverse mode"))return "В HidHide включён обратный режим.";
  if(message.Contains("recovery deferred"))return "Проверьте восстановление в диагностике.";
  if(message=="BRIDGE_PAUSED")return "Сначала включите эмуляцию PS5.";
  if(message=="BLUETOOTH_DISCONNECT_PENDING")return "Дождитесь отключения Bluetooth.";
  if(message=="CONTROLLER_BUSY")return "Контроллер подключён по USB или используется DSX.";
  if(message=="CONTROLLER_NOT_CONFIGURED")return "Сначала подключите один DualSense по Bluetooth.";
  return Regex.IsMatch(message,"[А-Яа-я]")?message:"Сбой подключения. Подробности в диагностике.";
 }
 static Icon MakeIcon(string state){
  using var bitmap=new Bitmap(24,24);using(var graphics=Graphics.FromImage(bitmap)){
   graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
   using var stream=Application.GetResourceStream(new Uri("pack://application:,,,/Assets/DSEmu.ico")).Stream;
   using var brand=new System.Drawing.Icon(stream,24,24);graphics.DrawIcon(brand,new Rectangle(0,0,24,24));
   var color=state switch{"Active"=>System.Drawing.Color.FromArgb(169,216,195),"Error"=>System.Drawing.Color.FromArgb(233,180,168),"Connecting" or "Stopping"=>System.Drawing.Color.Goldenrod,_=>System.Drawing.Color.SlateGray};
   using var outline=new SolidBrush(System.Drawing.Color.FromArgb(28,33,37));graphics.FillEllipse(outline,15,15,9,9);
   using var brush=new SolidBrush(color);graphics.FillEllipse(brush,17,17,5,5);
  }
  var handle=bitmap.GetHicon();try{using var icon=System.Drawing.Icon.FromHandle(handle);return (System.Drawing.Icon)icon.Clone();}finally{DestroyIcon(handle);}
 }
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr handle);
 [System.Runtime.InteropServices.DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
 internal void Open(){Show();WindowState=WindowState.Normal;Activate();}
 void Notify(string title,string text){if(tray!=null){tray.BalloonTipTitle=title;tray.BalloonTipText=text.Length>240?text[..240]:text;tray.ShowBalloonTip(5000);}}
 void Notice(string text){DiagnosticsNotice.Text=text;DiagnosticsNotice.Visibility=Visibility.Visible;if(!IsVisible||!diagnosticsOpen)Notify("DSEmu",text);else Dispatcher.BeginInvoke(new Action(()=>DiagnosticsNotice.BringIntoView()),DispatcherPriority.Background);}
 async Task Initialize(){await Command("enable");timer.Start();}
 async Task Refresh(){
  if(updating||exiting||commanding)return;updating=true;var generation=stateGeneration;
  try{
   bool diagnostics=diagnosticsOpen && IsVisible && DateTime.UtcNow-lastDiagnostics>TimeSpan.FromSeconds(3);
   var state=await BridgeClient.SendAsync(diagnostics?"getDiagnostics":"getState");
   if(diagnostics)lastDiagnostics=DateTime.UtcNow;
   if(generation==stateGeneration && !commanding)Display(state);
  }catch(Exception){if(generation==stateGeneration && !commanding)Display(current with {State="Error",Error="Нет связи со службой. Проверьте установку DSEmu.",BatteryPercent=null,Charging=false,Rssi=null,AudioActive=false,MicrophoneActive=false,Headphones=false,PacketsPerSecond=0,GapsOver10ms=0,MaxGapMs=0,ObservedSeconds=0});}
  finally{updating=false;}
 }
 async Task Command(string command){
  if(commanding||exiting)return;commanding=true;++stateGeneration;Display(current);
  try{Display(await BridgeClient.SendAsync(command));}catch(Exception e){Display(current with {State="Error",Error=e.Message});}
  finally{commanding=false;Display(current);}
 }
 async Task Exit(){
  if(exiting||commanding)return;
  try{
   await BridgeClient.SendAsync("pause");var deadline=DateTime.UtcNow.AddSeconds(10);bool released=false;
   while(DateTime.UtcNow<deadline){if((await BridgeClient.SendAsync("getState")).State=="Paused"){released=true;break;}await Task.Delay(200);}
   if(!released)throw new IOException("Служба не подтвердила освобождение контроллера.");
  }catch(Exception e){if(!ServiceState.StoppedOrMissing()){Notice("Не удалось выключить эмуляцию PS5: "+e.Message);return;}}
  exiting=true;timer.Stop();tray?.Icon?.Dispose();tray?.Dispose();Close();Application.Current.Shutdown();
 }
 async Task ExportDiagnostics(){
  if(exporting)return;exporting=true;ExportButton.IsEnabled=false;
  try{
   BridgeState state;try{state=await BridgeClient.SendAsync("getDiagnostics");}catch(Exception){state=current with {Error="Служба недоступна при экспорте диагностики. "+current.Error,LogLines=diagnosticLines};}
   var folder=Path.Combine(Settings.DirectoryPath,"Diagnostics");Directory.CreateDirectory(folder);
   var path=Path.Combine(folder,"DSEmu-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".zip");
   using(var zip=ZipFile.Open(path,ZipArchiveMode.Create)){
    var entry=zip.CreateEntry("status.json");using var writer=new StreamWriter(entry.Open());
    var sanitized=state with {Error=Diagnostics.Mask(state.Error),LogLines=state.LogLines.Select(Diagnostics.Mask).ToArray()};
    writer.Write(JsonSerializer.Serialize(new{appVersion="0.1.0",windows=Environment.OSVersion.VersionString,state=sanitized},BridgeClient.JsonOptions));
   }
   Notice("Сохранено: "+Path.GetFileName(path));
  }catch(Exception e){Notice("Не удалось сохранить диагностику: "+e.Message);}
  finally{exporting=false;ExportButton.IsEnabled=true;}
 }
 void ToggleAdvanced(object sender,RoutedEventArgs e)=>SetDiagnosticsOpen(!diagnosticsOpen);
 async void ToggleBridge(object sender,RoutedEventArgs e)=>await Command(current.Enabled?"pause":"enable");
 async void ReconnectBridge(object sender,RoutedEventArgs e)=>await Command("reconnect");
 async void DisconnectController(object sender,RoutedEventArgs e)=>await Command("disconnect");
 async void SaveDiagnostics(object sender,RoutedEventArgs e)=>await ExportDiagnostics();
 async void ExitApplication(object sender,RoutedEventArgs e)=>await Exit();
 void OpenDiagnosticsFolder(object sender,RoutedEventArgs e){try{var folder=Path.Combine(Settings.DirectoryPath,"Diagnostics");Directory.CreateDirectory(folder);Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});}catch(Exception error){Notice(error.Message);}}
}
