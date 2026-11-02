using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
internal class Program {
 [STAThread] static void Main(string[] args){
  var app=new Application();var asm=Assembly.Load("DS5Emu");var wt=asm.GetType("DSEmu.MainWindow")!;var st=asm.GetType("DSEmu.BridgeState")!;
  var folder=args.Length>0?args[0]:Path.Combine(Path.GetTempPath(),"DS5Emu-ui-tests");Directory.CreateDirectory(folder);
  int checks=0;
  foreach(var height in new[]{728d,824d,680d})foreach(var mode in new[]{"ps5","xbox"})foreach(var stateName in new[]{"Active","Paused","Error","Wired","Conflict","Stopping"}){
   var window=(Window)Activator.CreateInstance(wt,new object[]{true})!;var state=Activator.CreateInstance(st)!;
   st.GetProperty("Mode")!.SetValue(state,mode);
   st.GetProperty("State")!.SetValue(state,stateName);st.GetProperty("Enabled")!.SetValue(state,true);
   if(stateName=="Error")st.GetProperty("Error")!.SetValue(state,string.Join(" ",Enumerable.Repeat("Ошибка восстановления контроллера.",20)));
   st.GetProperty("LogLines")!.SetValue(state,new[]{"2026-11-02T18:40:00+03:00|daemon|INFO|Служба запущена", "2026-11-02T18:40:01+03:00|bluetooth|WARN|Проверка соединения с контроллером", "2026-11-02T18:40:02+03:00|port|ERROR|"+new string('x',500)});
   wt.GetMethod("Display")!.Invoke(window,new[]{state});wt.GetMethod("SetDiagnosticsOpen",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,new object[]{true});
   if(stateName!="Active" && ((TextBlock)window.FindName("EmuTitle")).Text=="Эмуляция включена")throw new Exception("Inactive session presented as active");
   if(stateName=="Active" && mode=="xbox" && !((TextBlock)window.FindName("EmuSubtitle")).Text.Contains("Xbox 360"))throw new Exception("Xbox mode missing from UI");
   if(window.FindName("SegmentDS4") != null)throw new Exception("DS4 placeholder must be removed");
   if(Grid.GetColumn((Button)window.FindName("SegmentXInput")) != 2)throw new Exception("Xbox segment must occupy the third column");
   if(window.FindName("LogText") != null)throw new Exception("Raw nested log textbox must be removed");
   if(((ItemsControl)window.FindName("EventList")).Items.Count!=3)throw new Exception("Recent events missing from diagnostics");
   var scroll=(ScrollViewer)window.Content;scroll.Measure(new Size(388,height));scroll.Arrange(new Rect(0,0,388,height));scroll.UpdateLayout();
   if(scroll.ViewportHeight>height+1 || scroll.ViewportHeight<=0)throw new Exception("Invalid viewport");
   if(((ColumnDefinition)window.FindName("BatteryFilled")).Width.Value!=0)throw new Exception("Unknown battery has invented level");
   scroll.ScrollToBottom();scroll.UpdateLayout();if(scroll.ExtentHeight>height+1 && scroll.VerticalOffset<=0)throw new Exception("Bottom cannot be reached");
   if(stateName=="Error" || stateName=="Active"){
    var bmp=new RenderTargetBitmap(388,(int)height,96,96,PixelFormats.Pbgra32);bmp.Render(scroll);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using var file=File.Create(Path.Combine(folder,stateName+"-"+mode+"-"+height+".png"));encoder.Save(file);
   }
   ++checks;
  }
  foreach(var error in new[]{"HidHide inverse mode is incompatible; configuration was not changed", "Нет связи со службой", "HidHide returned an unknown inverse state"}){
   var window=(Window)Activator.CreateInstance(wt,new object[]{true})!;var state=Activator.CreateInstance(st)!;
   st.GetProperty("State")!.SetValue(state,"Error");st.GetProperty("Enabled")!.SetValue(state,true);st.GetProperty("Error")!.SetValue(state,error);
   wt.GetMethod("Display")!.Invoke(window,new[]{state});
   var fix=(Button)window.FindName("FixHidHideButton");
   if((fix.Visibility==Visibility.Visible)!=error.Contains("is incompatible"))throw new Exception("HidHide repair affordance shown for an unrelated error");
   if(fix.Visibility==Visibility.Visible){
    var content=(FrameworkElement)window.Content;content.Measure(new Size(388,double.PositiveInfinity));var height=(int)Math.Ceiling(content.DesiredSize.Height);
    content.Arrange(new Rect(0,0,388,height));content.UpdateLayout();
    var bmp=new RenderTargetBitmap(388,height,96,96,PixelFormats.Pbgra32);bmp.Render(content);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using var file=File.Create(Path.Combine(folder,"HidHide-repair.png"));encoder.Save(file);
   }
   ++checks;
  }
  Console.WriteLine(checks+" UI layout/state scenarios passed (synthetic viewport sizes; no hardware)");
 }
}