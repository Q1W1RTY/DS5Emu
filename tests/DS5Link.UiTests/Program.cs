using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
internal class Program {
 [STAThread] static void Main(string[] args){
  var app=new Application();var asm=Assembly.Load("DS5Link");var wt=asm.GetType("DS5Link.MainWindow")!;var st=asm.GetType("DS5Link.BridgeState")!;
  var folder=args.Length>0?args[0]:Path.Combine(Path.GetTempPath(),"DS5Link-ui-tests");Directory.CreateDirectory(folder);
  int checks=0;
  foreach(var height in new[]{728d,824d,680d})foreach(var mode in new[]{"ps5","xbox"})foreach(var stateName in new[]{"Active","Paused","Error","Wired","Conflict","Stopping"}){
   var window=(Window)Activator.CreateInstance(wt,new object[]{true})!;var state=Activator.CreateInstance(st)!;
   st.GetProperty("Mode")!.SetValue(state,mode);
   st.GetProperty("State")!.SetValue(state,stateName);st.GetProperty("Enabled")!.SetValue(state,true);
   if(stateName=="Error")st.GetProperty("Error")!.SetValue(state,string.Join(" ",Enumerable.Repeat("Ошибка восстановления контроллера.",20)));
   st.GetProperty("LogLines")!.SetValue(state,new[]{"2026-11-02T18:40:00+03:00|daemon|INFO|Служба запущена", "2026-11-02T18:40:01+03:00|bluetooth|WARN|Проверка соединения с контроллером", "2026-11-02T18:40:02+03:00|port|ERROR|"+new string('x',500)});
   wt.GetMethod("Display")!.Invoke(window,new[]{state});wt.GetMethod("SetDiagnosticsOpen",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,new object[]{true});
   if(stateName!="Active" && ((TextBlock)window.FindName("EmuTitle")).Text=="Эмуляция включена")throw new Exception("Inactive session presented as active");
   if(window.FindName("SegmentDS4") != null)throw new Exception("DS4 placeholder must be removed");
   if(Grid.GetColumn((Button)window.FindName("SegmentXInput")) != 2)throw new Exception("Xbox segment must occupy the third column");
   if(window.FindName("LogText") != null)throw new Exception("Raw nested log textbox must be removed");
   if(((ItemsControl)window.FindName("EventList")).Items.Count!=3)throw new Exception("Recent events missing from diagnostics");
   var scroll=(ScrollViewer)window.Content;scroll.Measure(new Size(388,height));scroll.Arrange(new Rect(0,0,388,height));scroll.UpdateLayout();
   if(scroll.ViewportHeight>height+1 || scroll.ViewportHeight<=0)throw new Exception("Invalid viewport");
   if(((ColumnDefinition)window.FindName("BatteryFilled")).Width.Value!=0)throw new Exception("Unknown battery has invented level");
   scroll.ScrollToBottom();scroll.UpdateLayout();if(scroll.ExtentHeight>height+1 && scroll.VerticalOffset<=0)throw new Exception("Bottom cannot be reached");
   scroll.ScrollToTop();scroll.UpdateLayout();
   if(stateName=="Error" || stateName=="Active"){
    var bmp=new RenderTargetBitmap(388,(int)height,96,96,PixelFormats.Pbgra32);bmp.Render(scroll);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using var file=File.Create(Path.Combine(folder,stateName+"-"+mode+"-"+height+".png"));encoder.Save(file);
   }
   ++checks;
  }
  foreach(var mode in new[]{"ps5","xbox"})foreach(bool enabled in new[]{false,true}){
   var window=(Window)Activator.CreateInstance(wt,new object[]{true})!;var state=Activator.CreateInstance(st)!;
   st.GetProperty("UsbConnected")!.SetValue(state,true);st.GetProperty("Transport")!.SetValue(state,"USB");st.GetProperty("Mode")!.SetValue(state,mode);st.GetProperty("Enabled")!.SetValue(state,enabled);st.GetProperty("State")!.SetValue(state,enabled && mode=="xbox"?"Active":"Paused");
   wt.GetMethod("Display")!.Invoke(window,new[]{state});
   if(((Button)window.FindName("SegmentPS5")).IsEnabled || !((Button)window.FindName("SegmentXInput")).IsEnabled)throw new Exception("USB must disable only the PS5 segment");
   if(enabled && mode=="xbox" && ((TextBlock)window.FindName("BadgeText")).Text!="USB")throw new Exception("Wired Xbox mislabeled Bluetooth");
   if(((Button)window.FindName("DisconnectButton")).IsEnabled)throw new Exception("Bluetooth disconnect offered for USB device");
   var settings=(Window)wt.GetMethod("CreateInputSettingsWindow",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null)!;
   if(((Border)settings.FindName("AudioEffectsCard")).IsEnabled!=(mode!="xbox"))throw new Exception("Audio haptics availability must follow emulation mode");
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
  foreach(var viewport in new[]{new Size(860,690),new Size(960,790)}){
   var main=(Window)Activator.CreateInstance(wt,new object[]{true})!;
   if(main.FindName("InputSettingsPanel")!=null)throw new Exception("Settings must be a separate window");
   var window=(Window)wt.GetMethod("CreateInputSettingsWindow",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,null)!;
   if(window.WindowStyle!=WindowStyle.None || !window.AllowsTransparency || window.FindName("DragHeader")==null)throw new Exception("Settings must use the application chrome");
   var mainFrame=(Style)main.FindResource("SharedWindowFrame");var settingsFrame=(Border)window.FindName("WindowFrame");
   if(settingsFrame.CornerRadius!=new CornerRadius(28) || settingsFrame.Background.ToString()!=((System.Windows.Media.Brush)main.FindResource("ShellFrameBrush")).ToString())throw new Exception("Settings frame differs from main theme");
   if(((CheckBox)window.FindName("TouchBindingsEnabled")).IsChecked==true || ((Slider)window.FindName("LeftDeadzone")).Value!=0)throw new Exception("Input defaults are not disabled");
   if(((StackPanel)window.FindName("TouchMappings")).Children.Count!=7)throw new Exception("Touch mapping controls missing");
   ((Slider)window.FindName("LeftDeadzone")).Value=20;((Slider)window.FindName("RightDeadzone")).Value=12;
   var originalProfile=wt.GetField("inputProfile",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(main)!;
   var options=originalProfile.GetType().GetProperty("Options")!.GetValue(originalProfile)!;
   if((int)options.GetType().GetProperty("LeftDeadzone")!.GetValue(options)!!=0)throw new Exception("Draft slider changed active profile before Apply");
   var firstRow=(Grid)((StackPanel)window.FindName("TouchMappings")).Children[0];
   var bindingButton=firstRow.Children.OfType<Button>().Single();bindingButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   if(!bindingButton.Content.ToString()!.Contains("Нажмите клавишу"))throw new Exception("Mapping button did not enter keyboard capture");
   ((TabItem)window.FindName("EffectsTab")).IsSelected=true;
   if(bindingButton.Content.ToString()!.Contains("Нажмите клавишу"))throw new Exception("Tab switch retained keyboard mapping capture");
   ((TabControl)window.FindName("SettingsTabs")).SelectedIndex=0;
   var pt=asm.GetType("DS5Link.InputPreview")!;
   var sample=System.Text.Json.JsonSerializer.Deserialize("{\"Enabled\":true,\"AgeMs\":5,\"LeftX\":140,\"LeftY\":128,\"RightX\":210,\"RightY\":65,\"Touches\":[{\"Active\":true,\"X\":480,\"Y\":400},{\"Active\":true,\"X\":1500,\"Y\":700}]}",pt)!;
   var setter=window.GetType().GetMethod("SetPreview",BindingFlags.Instance|BindingFlags.NonPublic)!;setter.Invoke(window,new[]{sample});
   var left=window.FindName("LeftVisual");var output=left.GetType().GetProperty("Output",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(left)!;
   if(!output.Equals((0d,0d)))throw new Exception("Deadzone preview failed to center raw stick");
   var content=(FrameworkElement)window.Content;content.Measure(viewport);content.Arrange(new Rect(new Point(),viewport));content.UpdateLayout();
   var settingsScroll=(ScrollViewer)window.FindName("SettingsScroll");settingsScroll.ScrollToBottom();content.UpdateLayout();
   if(window.ResizeMode!=ResizeMode.NoResize || settingsScroll.VerticalScrollBarVisibility!=ScrollBarVisibility.Disabled || settingsScroll.ScrollableHeight>0)throw new Exception("Settings must fit without scrolling in a fixed window");
   settingsScroll.ScrollToTop();content.UpdateLayout();
   var bmp=new RenderTargetBitmap((int)viewport.Width,(int)viewport.Height,96,96,PixelFormats.Pbgra32);bmp.Render(content);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using var file=File.Create(Path.Combine(folder,"Input-window-"+viewport.Width+".png"));encoder.Save(file);
   pt.GetProperty("AgeMs")!.SetValue(sample,600);setter.Invoke(window,new[]{sample});
   if((bool)left.GetType().GetField("HasSample",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(left)!)throw new Exception("Stale stick dot remained visible");
   ++checks;
  }
  {
   var main=(Window)Activator.CreateInstance(wt,new object[]{true})!;
   using var menu=(System.Windows.Forms.ContextMenuStrip)wt.GetMethod("CreateTrayMenu",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,null)!;
   if(!menu.Items.Cast<System.Windows.Forms.ToolStripItem>().Any(item=>item.Text=="Настройки контроллера"))throw new Exception("Input settings missing from tray menu");
   if(menu.Renderer.GetType().Name!="TrayMenuRenderer" || menu.BackColor!=System.Drawing.Color.FromArgb(24,24,27))throw new Exception("Tray theme differs from application");
   menu.Size=menu.PreferredSize;
   using var bitmap=new System.Drawing.Bitmap(menu.Width,menu.Height);menu.DrawToBitmap(bitmap,new System.Drawing.Rectangle(System.Drawing.Point.Empty,menu.Size));bitmap.Save(Path.Combine(folder,"Tray-menu.png"));
   ++checks;
  }
  foreach(var viewport in new[]{new Size(860,690),new Size(960,840)}){
   var main=(Window)Activator.CreateInstance(wt,new object[]{true})!;var window=(Window)wt.GetMethod("CreateInputSettingsWindow",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,null)!;
   ((TabItem)window.FindName("EffectsTab")).IsSelected=true;
   if(window.FindName("SaveInputButton")!=null)throw new Exception("Manual Apply button must be removed");
   if(((CheckBox)window.FindName("AudioEffectsEnabled")).IsChecked==true)throw new Exception("Audio capture is enabled by default");
   ((ComboBox)window.FindName("TriggerPreset")).SelectedIndex=4;((ComboBox)window.FindName("LedMode")).SelectedIndex=1;
   if(((StackPanel)window.FindName("TriggerEditors")).Children.Count!=2)throw new Exception("Independent trigger editors missing");
   var content=(FrameworkElement)window.Content;content.Measure(viewport);content.Arrange(new Rect(new Point(),viewport));content.UpdateLayout();
   var scroll=(ScrollViewer)window.FindName("EffectsScroll");scroll.ScrollToBottom();content.UpdateLayout();if(scroll.VerticalScrollBarVisibility!=ScrollBarVisibility.Disabled || scroll.ScrollableHeight>0)throw new Exception("Effects must fit without scrolling");
   foreach(var position in new[]{"bottom","top"}){if(position=="top"){scroll.ScrollToTop();content.UpdateLayout();}var bmp=new RenderTargetBitmap((int)viewport.Width,(int)viewport.Height,96,96,PixelFormats.Pbgra32);bmp.Render(content);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using var file=File.Create(Path.Combine(folder,"Effects-"+viewport.Width+"-"+position+".png"));encoder.Save(file);}
   ++checks;
  }
  {
   var main=(Window)Activator.CreateInstance(wt,new object[]{true})!;
   var catalogType=asm.GetType("DS5Link.GameProfiles")!;
   var catalog=System.Text.Json.JsonSerializer.Deserialize("""
    {"Games":[{"Executable":"C:\\Games\\ProfileTest.exe","Input":{"Options":{"LeftDeadzone":19,"TouchEnabled":true},"Bindings":{"tap":{"VirtualKey":65,"Modifiers":2}}},"Effects":{"Left":{"Mode":"resistance","Strength":7},"LedMode":"rainbow"}}]}
    """,catalogType)!;
   wt.GetField("profiles",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(main,catalog);
   var window=(Window)wt.GetMethod("CreateInputSettingsWindow",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,null)!;
   var selector=(ComboBox)window.FindName("ProfileSelector");selector.SelectedIndex=1;
   if(((Slider)window.FindName("LeftDeadzone")).Value!=19 || ((CheckBox)window.FindName("TouchBindingsEnabled")).IsChecked!=true)throw new Exception("Game profile selection did not load input draft");
   if(((ComboBox)window.FindName("LedMode")).SelectedIndex!=3 || !((TextBlock)window.FindName("ProfilePath")).Text.EndsWith("ProfileTest.exe"))throw new Exception("Game profile selection did not load effect/path draft");
   selector.SelectedIndex=0;
   if(((Slider)window.FindName("LeftDeadzone")).Value!=0 || ((ComboBox)window.FindName("LedMode")).SelectedIndex!=0 || ((Button)window.FindName("RemoveProfileButton")).IsEnabled)throw new Exception("Returning to defaults retained game settings");
   ++checks;
  }
  Console.WriteLine(checks+" UI layout/state scenarios passed (synthetic viewport sizes; no hardware)");
 }
}