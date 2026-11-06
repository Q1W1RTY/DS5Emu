using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
namespace DS5Link;
internal sealed partial class InputSettingsWindow:Window {
 readonly Dictionary<string,Button> actionButtons=new();
 string selectedAction="leftClick";
 readonly Dictionary<string,KeyBinding> bindings;
 readonly Func<InputProfile,Task> apply;
 readonly DispatcherTimer previewTimer=new(){Interval=TimeSpan.FromMilliseconds(33)};
 readonly bool preview;
 string? capturingAction;
 bool polling,closed;
 readonly Stopwatch sampleAge=new();
 long sampleReportAge=500;
 internal static string[] Labels=>new[]{L.T("Нажатие слева"),L.T("Нажатие справа"),L.T("Касание"),L.T("Свайп влево"),L.T("Свайп вправо"),L.T("Свайп вверх"),L.T("Свайп вниз")};
 internal InputSettingsWindow(InputProfile profile,Func<InputProfile,Task> apply,bool preview=false,EffectOptions? effects=null,Func<EffectOptions,Task>? applyEffects=null){
  this.apply=apply;this.preview=preview;bindings=new(profile.Bindings);InitializeComponent();

  var work=SystemParameters.WorkArea;
  Width=Math.Min(1120,work.Width);Height=Math.Min(840,work.Height);
  MaxWidth=Width;MaxHeight=Height;
  TouchBindingsEnabled.IsChecked=profile.Options.TouchEnabled;ConsumeTouchpad.IsChecked=profile.Options.ConsumeTouchpad;
  LeftDeadzone.Value=profile.Options.LeftDeadzone;RightDeadzone.Value=profile.Options.RightDeadzone;
  InitializeTouchActions();
  TouchVisual.ZoneSelected+=SelectTouchAction;
  PreviewKeyDown+=CaptureKey;Deactivated+=(_,_)=>CancelCapture();
  SourceInitialized+=(_,_)=>HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(HeaderHitTest);
  previewTimer.Tick+=async(_,_)=>{if(sampleAge.IsRunning && sampleReportAge<500 && sampleAge.ElapsedMilliseconds+sampleReportAge>=500)SetPreview(new());UpdateLedPreview();await RefreshPreview();};
  Loaded+=(_,_)=>{if(!preview)previewTimer.Start();};
  Closing+=OnSettingsClosing;
  SettingsTabs.SelectionChanged+=(_,e)=>{if(ReferenceEquals(e.Source,SettingsTabs)){CancelCapture();ResetEffectsButton.Visibility=SettingsTabs.SelectedIndex>=3?Visibility.Visible:Visibility.Collapsed;(SettingsTabs.Template.FindName("SettingsPageScroll",SettingsTabs) as ScrollViewer)?.ScrollToTop();}};
  InitializeEffectControls(effects??new(),applyEffects);
  DeadzoneChanged(this,null!);
  InitializeAutoApply();
  if(preview)SaveStatusText.Text=L.T("Предпросмотр");
 }
 string BindingLabel(string action)=>bindings.TryGetValue(action,out var binding)?binding.Label:L.T("Не назначено");
 void InitializeTouchActions(){
  string[] icons={"","","·","←","→","↑","↓"};
  for(int i=0;i<InputProfile.Actions.Length;i++){
   var action=InputProfile.Actions[i];var content=new StackPanel();
   if(i>=2)content.Children.Add(new TextBlock {Text=icons[i],FontSize=20,HorizontalAlignment=HorizontalAlignment.Center});
   content.Children.Add(new TextBlock {Text=Labels[i],FontSize=14,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,MaxWidth=i<2?double.PositiveInfinity:72});
   var button=new Button {Content=content,Tag=action,Style=(Style)FindResource("CompactButton"),Margin=new Thickness(4),Padding=new Thickness(8),MinHeight=i<2?44:66};
   AutomationProperties.SetName(button,Labels[i]);
   button.Click+=(_,_)=>SelectTouchAction(action);actionButtons[action]=button;
   if(i<2)ClickButtons.Children.Add(button);else GestureButtons.Children.Add(button);
  }
  RefreshTouchSelection();
 }
 void RefreshTouchSelection(){
  SelectedActionName.Text=Labels[Array.IndexOf(InputProfile.Actions,selectedAction)];
  if(capturingAction==null)SelectedBindingButton.Content=BindingLabel(selectedAction);
  AutomationProperties.SetName(SelectedBindingButton,L.T("Назначение")+": "+BindingLabel(selectedAction));
  foreach(var pair in actionButtons){
   bool selected=pair.Key==selectedAction;
   pair.Value.Background=new SolidColorBrush(selected?Color.FromArgb(45,10,132,255):Color.FromArgb(20,255,255,255));
   pair.Value.BorderBrush=new SolidColorBrush(selected?Color.FromArgb(90,10,132,255):Color.FromArgb(14,255,255,255));
   pair.Value.Foreground=selected?Brushes.White:new SolidColorBrush(Color.FromArgb(220,255,255,255));
  }
  TouchVisual.SelectedAction=selectedAction;TouchVisual.InvalidateVisual();
 }
 void SelectTouchAction(string action){CancelCapture();selectedAction=action;RefreshTouchSelection();}
 void CaptureSelectedAction(object sender,RoutedEventArgs e)=>BeginCapture(selectedAction);
 void ClearSelectedBinding(object sender,RoutedEventArgs e){
  CancelCapture();bindings.Remove(selectedAction);if(bindings.Count==0)TouchBindingsEnabled.IsChecked=false;
  RefreshTouchSelection();QueueAutoApply(false);
 }
 void BeginCapture(string action){
  CancelCapture();selectedAction=action;RefreshTouchSelection();capturingAction=action;
  SelectedBindingButton.Content=L.T("Нажмите клавишу…");SelectedBindingButton.Focus();
  CaptureHint.Text=Labels[Array.IndexOf(InputProfile.Actions,action)]+L.T(": нажмите клавишу или сочетание. Backspace — убрать.");
 }
 void CancelCapture(){if(capturingAction!=null){capturingAction=null;RefreshTouchSelection();}}
 void CaptureKey(object sender,KeyEventArgs e){
  if(capturingAction==null)return;e.Handled=true;var key=e.Key==Key.System?e.SystemKey:e.Key;
  if(key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)return;
  if(key==Key.Back){bindings.Remove(capturingAction);if(bindings.Count==0)TouchBindingsEnabled.IsChecked=false;}
  else{var binding=new KeyBinding(KeyInterop.VirtualKeyFromKey(key),(int)Keyboard.Modifiers);if(!binding.Valid){CaptureHint.Text=L.T("Поддерживаются буквы, цифры, F1–F12, пробел, Enter, Esc, Tab и навигация. Win не поддерживается.");return;}bindings[capturingAction]=binding;}
  CancelCapture();RefreshTouchSelection();CaptureHint.Text=L.T("Назначение применяется автоматически.");QueueAutoApply(false);
 }
 void DeadzoneChanged(object sender,RoutedPropertyChangedEventArgs<double> e){
  if(LeftDeadzoneValue==null || RightDeadzoneValue==null || LeftVisual==null || RightVisual==null)return;
  LeftDeadzoneValue.Text=$"{LeftDeadzone.Value:F0}%";RightDeadzoneValue.Text=$"{RightDeadzone.Value:F0}%";
  LeftVisual.Deadzone=(int)LeftDeadzone.Value;RightVisual.Deadzone=(int)RightDeadzone.Value;LeftVisual.InvalidateVisual();RightVisual.InvalidateVisual();
 }
 internal void SetPreview(InputPreview value){
  bool fresh=value.Fresh;sampleReportAge=fresh?value.AgeMs:500;sampleAge.Restart();
  TouchVisual.Sample=fresh?value:new();TouchVisual.InvalidateVisual();
  LeftVisual.SetSample(fresh,value.LeftX,value.LeftY);RightVisual.SetSample(fresh,value.RightX,value.RightY);
 }
 IntPtr HeaderHitTest(IntPtr hwnd,int message,IntPtr wparam,IntPtr lparam,ref bool handled){
  if(message!=0x0084)return IntPtr.Zero;
  long packed=lparam.ToInt64();var screen=new Point((short)(packed&0xffff),(short)((packed>>16)&0xffff));
  var point=DragHeader.PointFromScreen(screen);if(!new Rect(new Point(),DragHeader.RenderSize).Contains(point))return IntPtr.Zero;
  for(var source=DragHeader.InputHitTest(point) as DependencyObject;source!=null;source=source is Visual?VisualTreeHelper.GetParent(source):LogicalTreeHelper.GetParent(source)){
   if(source is System.Windows.Controls.Primitives.ButtonBase)return IntPtr.Zero;if(ReferenceEquals(source,DragHeader))break;
  }
  handled=true;return new IntPtr(2);
 }
 void MinimizeWindow(object sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
 void CloseWindow(object sender,RoutedEventArgs e)=>Close();
 async Task RefreshPreview(){
  if(polling || closed)return;polling=true;
  try{var value=await BridgeClient.ReadInputPreviewAsync();if(!closed){SetPreview(value);}}catch(Exception){if(!closed)SetPreview(new());}finally{polling=false;}
 }
}
