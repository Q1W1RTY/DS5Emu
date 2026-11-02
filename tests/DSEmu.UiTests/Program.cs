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
   wt.GetMethod("Display")!.Invoke(window,new[]{state});wt.GetMethod("SetDiagnosticsOpen",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,new object[]{true});
   if(stateName!="Active" && ((TextBlock)window.FindName("EmuTitle")).Text=="Эмуляция включена")throw new Exception("Inactive session presented as active");
   if(stateName=="Active" && mode=="xbox" && !((TextBlock)window.FindName("EmuSubtitle")).Text.Contains("Xbox 360"))throw new Exception("Xbox mode missing from UI");
   if(window.FindName("SegmentDS4") != null)throw new Exception("DS4 placeholder must be removed");
   if(Grid.GetColumn((Button)window.FindName("SegmentXInput")) != 2)throw new Exception("Xbox segment must occupy the third column");
   var scroll=(ScrollViewer)window.Content;scroll.Measure(new Size(388,height));scroll.Arrange(new Rect(0,0,388,height));scroll.UpdateLayout();
   if(scroll.ViewportHeight>height+1 || scroll.ViewportHeight<=0)throw new Exception("Invalid viewport");
   if(((ColumnDefinition)window.FindName("BatteryFilled")).Width.Value!=0)throw new Exception("Unknown battery has invented level");
   scroll.ScrollToBottom();scroll.UpdateLayout();if(scroll.ExtentHeight>height+1 && scroll.VerticalOffset<=0)throw new Exception("Bottom cannot be reached");
   if(stateName=="Error" || stateName=="Active"){
    var bmp=new RenderTargetBitmap(388,(int)height,96,96,PixelFormats.Pbgra32);bmp.Render(scroll);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using var file=File.Create(Path.Combine(folder,stateName+"-"+mode+"-"+height+".png"));encoder.Save(file);
   }
   ++checks;
  }
  Console.WriteLine(checks+" UI layout/state scenarios passed (synthetic viewport sizes; no hardware)");
 }
}