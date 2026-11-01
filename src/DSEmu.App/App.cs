using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DSEmu;
internal static class App {
 [STAThread]
 public static void Main(string[] args) {
  var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
  if(args.Length==2 && args[0]=="--render-icon-preview") {
   Directory.CreateDirectory(args[1]);
   foreach(var state in new[]{"Active","Waiting","Error","Connecting"}) {
    using var icon=MainWindow.MakeIcon(state);using var bitmap=icon.ToBitmap();
    bitmap.Save(Path.Combine(args[1],state+".png"),System.Drawing.Imaging.ImageFormat.Png);
   }
   return;
  }
  if(args.Length>=2 && args[0]=="--render-preview") {
   var preview=new MainWindow(true);
   bool advanced=Array.IndexOf(args,"--advanced")>=0;
   bool disconnected=Array.IndexOf(args,"--disconnected")>=0;
   string state=Array.IndexOf(args,"--paused")>=0||disconnected?"Paused":Array.IndexOf(args,"--error")>=0?"Error":"Active";
   preview.Display(new BridgeState {State=state,Enabled=state!="Paused",BluetoothDisconnected=disconnected,BatteryPercent=state=="Active"?85:null,Charging=state=="Active",PacketsPerSecond=state=="Active"?598.4:0,GapsOver10ms=8,MaxGapMs=1.2,ObservedSeconds=60,AudioActive=true,Error=state=="Error"?"Нет связи со службой. Проверьте установку DS5Emu.":"",LogLines=new[]{"[предпросмотр] Служба подключена · JSONL v1","[предпросмотр] Ввод контроллера поступает","[предпросмотр] Виртуальное USB-устройство активно"}});
   preview.SetDiagnosticsOpen(advanced);
   int width=388;
   var content=(FrameworkElement)preview.Content;content.Measure(new Size(width,double.PositiveInfinity));
   int height=(int)Math.Ceiling(content.DesiredSize.Height);
   content.Arrange(new Rect(0,0,width,height));content.UpdateLayout();
   var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
   bitmap.Render(content);
   var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(args[1]);encoder.Save(file);return;
  }
  using var mutex=new Mutex(true,"Local\\DSEmu.UI."+WindowsIdentity.GetCurrent().User?.Value,out bool first);
  using var activate=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\DSEmu.Activate."+WindowsIdentity.GetCurrent().User?.Value);
  if(!first){activate.Set();return;}
  var window=new MainWindow(false);app.MainWindow=window;
  var settings=Settings.Load();bool show=settings.FirstRun || Array.IndexOf(args,"--show")>=0;
  settings.FirstRun=false;settings.Save();
  if(show)window.Show();
  var registration=ThreadPool.RegisterWaitForSingleObject(activate,(_,_)=>app.Dispatcher.BeginInvoke(window.Open),null,Timeout.Infinite,false);
  app.Run();
  registration.Unregister(null);
 }
}
