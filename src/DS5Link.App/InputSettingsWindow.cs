using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
namespace DS5Link;
internal sealed partial class InputSettingsWindow:Window {
 readonly Dictionary<string,Button> mappingButtons=new();
 readonly Dictionary<string,KeyBinding> bindings;
 readonly Func<InputProfile,Task> apply;
 readonly DispatcherTimer previewTimer=new(){Interval=TimeSpan.FromMilliseconds(33)};
 readonly bool preview;
 string? capturingAction;
 bool polling,closed;
 readonly Stopwatch sampleAge=new();
 long sampleReportAge=500;
 internal static readonly string[] Labels={"Нажатие слева","Нажатие справа","Касание","Свайп влево","Свайп вправо","Свайп вверх","Свайп вниз"};
 internal InputSettingsWindow(InputProfile profile,Func<InputProfile,Task> apply,bool preview=false,EffectOptions? effects=null,Func<EffectOptions,Task>? applyEffects=null){
  this.apply=apply;this.preview=preview;bindings=new(profile.Bindings);InitializeComponent();
  TouchBindingsEnabled.IsChecked=profile.Options.TouchEnabled;ConsumeTouchpad.IsChecked=profile.Options.ConsumeTouchpad;
  LeftDeadzone.Value=profile.Options.LeftDeadzone;RightDeadzone.Value=profile.Options.RightDeadzone;
  for(int i=0;i<InputProfile.Actions.Length;i++){
   var action=InputProfile.Actions[i];var row=new Grid {Margin=new Thickness(0,0,0,5)};
   row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=new GridLength(142)});
   row.Children.Add(new TextBlock {Text=Labels[i],FontSize=12,VerticalAlignment=VerticalAlignment.Center});
   var button=new Button {Content=BindingLabel(action),Style=(Style)FindResource("CompactButton"),FontSize=11,MinHeight=30};
   button.Click+=(_,_)=>BeginCapture(action);Grid.SetColumn(button,1);row.Children.Add(button);TouchMappings.Children.Add(row);mappingButtons[action]=button;
  }
  foreach(var i in new[]{2,3,4,5,6}){var action=InputProfile.Actions[i];var button=new Button {Content=Labels[i],Style=(Style)FindResource("CompactButton"),FontSize=10,Margin=new Thickness(2),MinHeight=30,Padding=new Thickness(3)};button.Click+=(_,_)=>BeginCapture(action);GestureButtons.Children.Add(button);}
  TouchVisual.ZoneSelected+=action=>BeginCapture(action);
  PreviewKeyDown+=CaptureKey;Deactivated+=(_,_)=>CancelCapture();
  SourceInitialized+=(_,_)=>HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(HeaderHitTest);
  previewTimer.Tick+=async(_,_)=>{if(sampleAge.IsRunning && sampleReportAge<500 && sampleAge.ElapsedMilliseconds+sampleReportAge>=500)SetPreview(new());UpdateLedPreview();await RefreshPreview();};
  Loaded+=(_,_)=>{if(!preview)previewTimer.Start();};
  Closing+=OnSettingsClosing;
  SettingsTabs.SelectionChanged+=(_,e)=>{if(ReferenceEquals(e.Source,SettingsTabs))CancelCapture();};
  InitializeEffectControls(effects??new(),applyEffects);
  DeadzoneChanged(this,null!);
  InitializeAutoApply();
 }
 string BindingLabel(string action)=>bindings.TryGetValue(action,out var binding)?binding.Label:"Не назначено";
 void BeginCapture(string action){CancelCapture();capturingAction=action;mappingButtons[action].Content="Нажмите клавишу…";mappingButtons[action].Focus();TouchVisual.SelectedAction=action;TouchVisual.InvalidateVisual();CaptureHint.Text=Labels[Array.IndexOf(InputProfile.Actions,action)]+": нажмите клавишу или сочетание. Backspace — убрать.";}
 void CancelCapture(){if(capturingAction!=null){mappingButtons[capturingAction].Content=BindingLabel(capturingAction);capturingAction=null;}}
 void CaptureKey(object sender,KeyEventArgs e){
  if(capturingAction==null)return;e.Handled=true;var key=e.Key==Key.System?e.SystemKey:e.Key;
  if(key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)return;
  if(key==Key.Back)bindings.Remove(capturingAction);
  else{var binding=new KeyBinding(KeyInterop.VirtualKeyFromKey(key),(int)Keyboard.Modifiers);if(!binding.Valid){CaptureHint.Text="Поддерживаются буквы, цифры, F1–F12, пробел, Enter, Esc, Tab и навигация. Win не поддерживается.";return;}bindings[capturingAction]=binding;}
  CancelCapture();CaptureHint.Text="Назначение применяется автоматически.";QueueAutoApply(false);
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
  var edge=WindowFrame.PointFromScreen(screen);int hit=0;
  bool left=edge.X<6,right=edge.X>WindowFrame.ActualWidth-6,top=edge.Y<6,bottom=edge.Y>WindowFrame.ActualHeight-6;
  if(left)hit=top?13:bottom?16:10;else if(right)hit=top?14:bottom?17:11;else if(top)hit=12;else if(bottom)hit=15;
  if(hit!=0){handled=true;return new IntPtr(hit);}
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
