using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
internal class Program {
 [STAThread] static void Main(string[] args){
  var app=new Application();var asm=Assembly.Load("DS5Link");var wt=asm.GetType("DS5Link.MainWindow")!;var st=asm.GetType("DS5Link.BridgeState")!;
  if(args.Contains("--show-preview")){
   var main=(Window)Activator.CreateInstance(wt,new object[]{true})!;
   var settings=(Window)wt.GetMethod("CreateInputSettingsWindow",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,null)!;
   settings.Title="DS5Link — проверка читаемости";settings.ShowActivated=false;
   if(args.Contains("--smooth")){TextOptions.SetTextFormattingMode(settings,TextFormattingMode.Ideal);TextOptions.SetTextRenderingMode(settings,TextRenderingMode.Grayscale);}
   if(args.Contains("--opaque")){settings.AllowsTransparency=false;settings.Background=new SolidColorBrush(Color.FromRgb(24,24,27));}
   if(args.Contains("--display")){TextOptions.SetTextFormattingMode(settings,TextFormattingMode.Display);TextOptions.SetTextRenderingMode(settings,TextRenderingMode.ClearType);}
   if(args.Contains("--subpixel")){
    settings.AllowsTransparency=false;
    settings.Background=new SolidColorBrush(Color.FromRgb(24,24,27));
    TextOptions.SetTextFormattingMode(settings,args.Contains("--display") ? TextFormattingMode.Display : TextFormattingMode.Ideal);
    TextOptions.SetTextRenderingMode(settings,TextRenderingMode.ClearType);
    RenderOptions.SetClearTypeHint(settings,ClearTypeHint.Enabled);
    ((TabItem)settings.FindName("AudioTab")).IsSelected=true;
    settings.Title="DS5Link — Inter 14 · ClearType";
   }
   var fontOverride=args.FirstOrDefault(arg=>arg.StartsWith("--font-preview="));
   if(fontOverride!=null){
    var parts=fontOverride.Substring("--font-preview=".Length).Split('|');
    settings.FontFamily=new FontFamily(new Uri(Path.GetFullPath(parts[0])+Path.DirectorySeparatorChar),"./#"+parts[1]);
    foreach(var weight in args.Contains("--strong")?new[]{FontWeights.Normal,FontWeights.Medium,FontWeights.SemiBold,FontWeights.Bold}:new[]{FontWeights.Normal,FontWeights.Medium,FontWeights.SemiBold}){
     var face=new Typeface(settings.FontFamily,FontStyles.Normal,weight,FontStretches.Normal);
     if(!face.TryGetGlyphTypeface(out var glyph) || glyph.Weight!=weight || "Настройки ЁёЙйABCDE0123".Any(c=>!glyph.CharacterToGlyphMap.ContainsKey(c)))throw new Exception("Preview font did not resolve: "+parts[1]);
    }
    settings.Title="DS5Link — "+parts[1];
   }
   if(args.Contains("--strong"))settings.Loaded+=(_,_)=>{
    void Strengthen(DependencyObject node){
     if(node is TextBlock text && !text.FontFamily.Source.Contains("Segoe MDL2"))text.FontWeight=text.FontSize>=20?FontWeights.Bold:text.FontWeight>=FontWeights.SemiBold?FontWeights.SemiBold:FontWeights.Medium;
     else if(node is Control control)control.FontWeight=control.FontWeight>=FontWeights.SemiBold?FontWeights.SemiBold:FontWeights.Medium;
     for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)Strengthen(VisualTreeHelper.GetChild(node,i));
    }
    Strengthen(settings);
   };
   app.MainWindow=settings;app.ShutdownMode=ShutdownMode.OnMainWindowClose;
   app.Run(settings);return;
  }
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
   AssertTextRendering(scroll);
   foreach(var wrap in Descendants(scroll).OfType<WrapPanel>())foreach(var child in wrap.Children.OfType<FrameworkElement>()){
    var edge=child.TranslatePoint(new Point(child.ActualWidth,0),wrap);
    if(edge.X>wrap.ActualWidth+1)throw new Exception("Typography clips a wrapped action");
   }
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
   if(((TextBlock)window.FindName("CaptureHint")).FontWeight!=FontWeights.Normal)throw new Exception("Selected sidebar tab made ordinary page text bold: "+((TextBlock)window.FindName("CaptureHint")).FontWeight);
   foreach(var weight in new[]{FontWeights.Normal,FontWeights.Medium,FontWeights.SemiBold,FontWeights.Bold}){
    var face=new Typeface(window.FontFamily,FontStyles.Normal,weight,FontStretches.Normal);

    if(!face.TryGetGlyphTypeface(out var glyph) || !glyph.FontUri.OriginalString.Contains("GolosText",StringComparison.OrdinalIgnoreCase) || glyph.Weight!=weight || face.IsBoldSimulated)throw new Exception("Bundled settings font not resolved for "+weight);
    if("Настройки ЁёЙйABCDE0123".Any(c=>!glyph.CharacterToGlyphMap.ContainsKey(c)))throw new Exception("Settings font lacks Russian/English glyphs");
   }
   var mainFrame=(Style)main.FindResource("SharedWindowFrame");var settingsFrame=(Border)window.FindName("WindowFrame");
   if(settingsFrame.CornerRadius!=new CornerRadius(28) || settingsFrame.Background!=null || ((Border)((Grid)settingsFrame.Parent).Children[0]).Background.ToString()!="#EE18181B")throw new Exception("Settings frame differs from main theme");
   if(settingsFrame.Effect!=null || mainFrame.Setters.OfType<Setter>().Any(setter=>setter.Property==UIElement.EffectProperty))throw new Exception("Window shadow must not rasterize the text subtree");
   if(((CheckBox)window.FindName("TouchBindingsEnabled")).IsChecked==true || ((Slider)window.FindName("LeftDeadzone")).Value!=0)throw new Exception("Input defaults are not disabled");
   if(((Panel)window.FindName("ClickButtons")).Children.Count+((Panel)window.FindName("GestureButtons")).Children.Count!=7)throw new Exception("Touch mapping controls missing");
   ((Slider)window.FindName("LeftDeadzone")).Value=20;((Slider)window.FindName("RightDeadzone")).Value=12;
   var originalProfile=wt.GetField("inputProfile",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(main)!;
   var options=originalProfile.GetType().GetProperty("Options")!.GetValue(originalProfile)!;
   if((int)options.GetType().GetProperty("LeftDeadzone")!.GetValue(options)!!=0)throw new Exception("Draft slider changed active profile before Apply");
   var bindingButton=(Button)window.FindName("SelectedBindingButton");bindingButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   if(window.FontSize<14 || ((TextBlock)window.FindName("ActiveProfileText")).FontSize<14)throw new Exception("Settings body text must remain readable without magnification");
   bindingButton.ApplyTemplate();
   for(DependencyObject? node=bindingButton;node!=null && node!=window;node=VisualTreeHelper.GetParent(node))if(node is UIElement visual && visual.Effect!=null)throw new Exception("Binding text must not be rasterized by an ancestor effect");
   if(!bindingButton.Content.ToString()!.Contains("Нажмите клавишу"))throw new Exception("Mapping button did not enter keyboard capture");
   ((TabItem)window.FindName("EffectsTab")).IsSelected=true;
   if(bindingButton.Content.ToString()!.Contains("Нажмите клавишу"))throw new Exception("Tab switch retained keyboard mapping capture");
   ((TabControl)window.FindName("SettingsTabs")).SelectedIndex=1;
   var pt=asm.GetType("DS5Link.InputPreview")!;
   var sample=System.Text.Json.JsonSerializer.Deserialize("{\"Enabled\":true,\"AgeMs\":5,\"LeftX\":140,\"LeftY\":128,\"RightX\":210,\"RightY\":65,\"Touches\":[{\"Active\":true,\"X\":480,\"Y\":400},{\"Active\":true,\"X\":1500,\"Y\":700}]}",pt)!;
   var setter=window.GetType().GetMethod("SetPreview",BindingFlags.Instance|BindingFlags.NonPublic)!;setter.Invoke(window,new[]{sample});
   var left=window.FindName("LeftVisual");var output=left.GetType().GetProperty("Output",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(left)!;
   if(!output.Equals((0d,0d)))throw new Exception("Deadzone preview failed to center raw stick");
   var content=(FrameworkElement)window.Content;content.Measure(viewport);content.Arrange(new Rect(new Point(),viewport));content.UpdateLayout();
   var settingsScroll=(Border)window.FindName("SettingsScroll");content.UpdateLayout();
   if(window.ResizeMode!=ResizeMode.NoResize || settingsScroll.ActualHeight<=0)throw new Exception("Settings must remain usable in a fixed window");
   content.UpdateLayout();
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
   if(((Panel)window.FindName("TriggerEditors")).Children.Count!=2)throw new Exception("Independent trigger editors missing");
   var content=(FrameworkElement)window.Content;content.Measure(viewport);content.Arrange(new Rect(new Point(),viewport));content.UpdateLayout();
   var scroll=(Border)window.FindName("EffectsScroll");content.UpdateLayout();if(scroll.ActualHeight<=0)throw new Exception("Effects content must be laid out");
   foreach(var position in new[]{"bottom","top"}){if(position=="top"){content.UpdateLayout();}var bmp=new RenderTargetBitmap((int)viewport.Width,(int)viewport.Height,96,96,PixelFormats.Pbgra32);bmp.Render(content);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using var file=File.Create(Path.Combine(folder,"Effects-"+viewport.Width+"-"+position+".png"));encoder.Save(file);}
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
  {
   var language=asm.GetType("DS5Link.L")!;
   var set=language.GetMethod("SetLanguage",BindingFlags.Static|BindingFlags.NonPublic)!;
   foreach(var languageCode in new[]{"ru","en"}){
   set.Invoke(null,new object[]{languageCode});
   foreach(var mode in new[]{"ps5","xbox"})foreach(var name in new[]{"Active","Waiting","Paused","Error"}){
    var main=(Window)Activator.CreateInstance(wt,new object[]{true})!;var state=Activator.CreateInstance(st)!;
    st.GetProperty("State")!.SetValue(state,name);st.GetProperty("Mode")!.SetValue(state,mode);st.GetProperty("Enabled")!.SetValue(state,name=="Active");wt.GetMethod("Display")!.Invoke(main,new[]{state});
    if(((Button)main.FindName("LanguageButton")).Content?.ToString()!=(languageCode=="en"?"EN":"RU"))throw new Exception("Language button must display only the current language");
    if(languageCode=="en" && ((TextBlock)main.FindName("EmuTitle")).Text.Any(c=>c>='А'&&c<='я'))throw new Exception("Russian main status in English mode");
    var window=(Window)wt.GetMethod("CreateInputSettingsWindow",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,null)!;
    if(!window.Title.Contains(languageCode=="en"?"Controller settings":"Настройки контроллера"))throw new Exception("Settings title not translated");
    if(((ComboBox)window.FindName("ProfileSelector")).SelectedItem!.ToString()!=(languageCode=="en"?"General settings":"Общие настройки"))throw new Exception("Profile name not translated");
    using var menu=(System.Windows.Forms.ContextMenuStrip)wt.GetMethod("CreateTrayMenu",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,null)!;
    if(!menu.Items.Cast<System.Windows.Forms.ToolStripItem>().Any(i=>i.Text=="RU / EN"))throw new Exception("Language submenu missing");
    ((ComboBox)window.FindName("TriggerPreset")).SelectedIndex=4;
    ((ComboBox)window.FindName("LedMode")).SelectedIndex=1;
    foreach(var viewport in new[]{new Size(1120,840),new Size(960,790),new Size(860,690),new Size(1120,640)})foreach(var tab in new[]{0,1,2,3,4,5}){
     ((TabControl)window.FindName("SettingsTabs")).SelectedIndex=tab;
     var content=(FrameworkElement)window.Content;content.Measure(viewport);content.Arrange(new Rect(new Point(),viewport));content.UpdateLayout();
     AssertTextRendering(content);
     var tabs=(TabControl)window.FindName("SettingsTabs");
     if(tabs.Items.Count!=6)throw new Exception("Six settings sections required");
     var selectedTab=(TabItem)tabs.SelectedItem;
     if(((StackPanel)selectedTab.Header).Children.OfType<TextBlock>().Last().FontWeight!=FontWeights.Medium)throw new Exception("Selected sidebar label must use preset UI weight 500");
     AssertBodyWeight((DependencyObject)selectedTab.Content);
     if(tabs.Items.Cast<TabItem>().Any(item=>string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetName(item))))throw new Exception("Settings sections require accessible labels");
     var host=(FrameworkElement)tabs.Template.FindName("PART_SelectedContentHost",tabs);
     var pageScroll=(ScrollViewer)tabs.Template.FindName("SettingsPageScroll",tabs);
     if(pageScroll.ViewportHeight<=0 || pageScroll.ViewportWidth<=0 || pageScroll.ScrollableWidth>0)throw new Exception("Settings page requires a usable vertical-only viewport");
     var title=(TextBlock)window.FindName("SelectedActionName");
     if(tab==1){var transform=title.TransformToAncestor(content);var origin=transform.Transform(new Point());var unit=transform.Transform(new Point(1,1));if(Math.Abs(unit.X-origin.X-1)>0.001 || Math.Abs(unit.Y-origin.Y-1)>0.001)throw new Exception("Settings text was scaled instead of laid out");}
     var pageBounds=host.TransformToAncestor(content).TransformBounds(new Rect(host.RenderSize));
     void CheckBounds(DependencyObject node){
      if(node is FrameworkElement element && element.Visibility==Visibility.Visible && element.RenderSize.Width>0 && element.RenderSize.Height>0){
       var bounds=element.TransformToAncestor(content).TransformBounds(new Rect(element.RenderSize));
       if(bounds.Right>viewport.Width+1 || bounds.Left< -1)throw new Exception("Settings content outside window width: tab "+tab+" "+element.GetType().Name+" "+element.Name+" "+bounds);
       if(bounds.Bottom>pageBounds.Bottom+1 || bounds.Right>pageBounds.Right+1 || bounds.Top<pageBounds.Top-1 || bounds.Left<pageBounds.Left-1)throw new Exception("Settings content clipped by page host: tab "+tab+" "+element.GetType().Name+" "+element.Name+" "+bounds);
      }
      if(node is UIElement clipped && clipped.ClipToBounds)return;
      for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)CheckBounds(VisualTreeHelper.GetChild(node,i));
     }
     CheckBounds((DependencyObject)((TabItem)tabs.SelectedItem).Content);
     pageScroll.ScrollToBottom();content.UpdateLayout();
     if(Math.Abs(pageScroll.VerticalOffset-pageScroll.ScrollableHeight)>1)throw new Exception("Bottom of settings page is unreachable");
     pageScroll.ScrollToTop();content.UpdateLayout();
     if(mode=="ps5" && name=="Active" && viewport==new Size(1120,840)){
      var image=new RenderTargetBitmap(1120,840,96,96,PixelFormats.Pbgra32);var drawing=new DrawingVisual();using(var context=drawing.RenderOpen()){context.DrawRectangle(new VisualBrush(content){Stretch=Stretch.Fill},null,new Rect(0,0,1120,840));}image.Render(drawing);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(folder,(languageCode=="en"?"English":"Russian")+"-settings-"+tab+".png"));encoder.Save(file);
     }
     if(mode=="ps5" && name=="Active" && languageCode=="ru" && tab==1 && viewport==new Size(1120,640)){
      foreach(var dpi in new[]{96d,120d,144d}){var image=new RenderTargetBitmap((int)(viewport.Width*dpi/96),(int)(viewport.Height*dpi/96),dpi,dpi,PixelFormats.Pbgra32);image.Render(content);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(folder,"Settings-text-"+dpi+"dpi.png"));encoder.Save(file);}
     }
    }
    ++checks;
   }
   }
   set.Invoke(null,new object[]{"ru"});
   var restored=(Window)Activator.CreateInstance(wt,new object[]{true})!;
   var settings=(Window)wt.GetMethod("CreateInputSettingsWindow",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(restored,null)!;
   if(!settings.Title.Contains("Настройки контроллера"))throw new Exception("Switching back did not restore Russian");
  }
  {
   var main=(Window)Activator.CreateInstance(wt,new object[]{true})!;
   var profileType=asm.GetType("DS5Link.InputProfile")!;
   var profile=System.Text.Json.JsonSerializer.Deserialize("""{"Options":{"TouchEnabled":true},"Bindings":{"leftClick":{"VirtualKey":77,"Modifiers":0}}}""",profileType)!;
   wt.GetField("inputProfile",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(main,profile);
   // Create a direct preview editor to preserve the seeded profile rather than the catalog default.
   var editorType=asm.GetType("DS5Link.InputSettingsWindow")!;
   var applyMethod=wt.GetMethod("ApplyInputSettings",BindingFlags.Instance|BindingFlags.NonPublic)!;
   var callback=Delegate.CreateDelegate(typeof(Func<,>).MakeGenericType(profileType,typeof(Task)),main,applyMethod);
   var editor=(Window)Activator.CreateInstance(editorType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object?[]{profile,callback,true,null,null},null)!;
   var key=(Button)editor.FindName("SelectedBindingButton");if(key.Content?.ToString()!="M")throw new Exception("Selected action did not show its saved key");
   ((Button)((Panel)editor.FindName("ClickButtons")).Children[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   if(key.Content?.ToString()!="Не назначено")throw new Exception("Selecting unbound side retained the other side binding");
   ((Button)((Panel)editor.FindName("ClickButtons")).Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   if(key.Content?.ToString()!="M")throw new Exception("Selecting a region lost its saved key");
   var originalBindings=profileType.GetProperty("Bindings")!.GetValue(profile) as System.Collections.IDictionary;
   if(originalBindings!.Count!=1)throw new Exception("Navigating regions changed saved profile");
   ++checks;
  }
  Console.WriteLine(checks+" UI layout/state scenarios passed (synthetic viewport sizes; no hardware)");
 }
 static IEnumerable<DependencyObject> Descendants(DependencyObject root){yield return root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Descendants(VisualTreeHelper.GetChild(root,i)))yield return child;}
 static void AssertBodyWeight(DependencyObject node){
  if(node is TextBlock text && text.ReadLocalValue(TextBlock.FontWeightProperty)==DependencyProperty.UnsetValue && text.FontWeight!=FontWeights.Normal)throw new Exception("Sidebar selection leaked font weight into body text: "+text.Text+" ("+text.FontWeight+")");
  if(node is Panel panel)foreach(UIElement child in panel.Children)AssertBodyWeight(child);
  else if(node is Border border && border.Child!=null)AssertBodyWeight(border.Child);
 }
 static void AssertTextRendering(DependencyObject root){
  void Walk(DependencyObject node){
   if(node is TextBlock || node.GetType().Name is "StickView" or "TouchpadView"){
    if(TextOptions.GetTextFormattingMode(node)!=TextFormattingMode.Ideal || TextOptions.GetTextRenderingMode(node)!=TextRenderingMode.Grayscale)throw new Exception("Inconsistent text rendering: "+node.GetType().Name);
    for(DependencyObject? ancestor=node;ancestor!=null;ancestor=VisualTreeHelper.GetParent(ancestor)){
     if(ancestor is UIElement element && element.Effect!=null)throw new Exception("Text is rendered through an ancestor effect: "+node.GetType().Name);
     if(ReferenceEquals(ancestor,root))break;
    }
   }
   for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)Walk(VisualTreeHelper.GetChild(node,i));
  }
  Walk(root);
 }
}
