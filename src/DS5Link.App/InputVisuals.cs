using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
namespace DS5Link;
internal static class InputDrawing {
 internal static readonly Typeface Face=new(new FontFamily("pack://application:,,,/DS5Link;component/Assets/Fonts/#Golos Text"),FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);
 internal static readonly Brush Muted=new SolidColorBrush(Color.FromRgb(143,145,151));
 internal static readonly Brush Blue=new SolidColorBrush(Color.FromRgb(64,156,255));
 internal static readonly Pen GridPen=new(new SolidColorBrush(Color.FromRgb(66,67,72)),1);
 internal static void Text(Visual owner,DrawingContext dc,string text,double x,double y,double size,Brush? color=null)=>dc.DrawText(new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,Face,size,color??Muted,VisualTreeHelper.GetDpi(owner).PixelsPerDip),new Point(x,y));
}
public sealed class StickView:FrameworkElement {
 internal int Deadzone;
 internal bool HasSample;
 internal double RawX,RawY;
 internal static double Axis(int value)=>(Math.Clamp(value,0,255)-128)/(value>=128?127.0:128.0);
 internal void SetSample(bool fresh,int x,int y){HasSample=fresh;RawX=Axis(x);RawY=Axis(y);InvalidateVisual();}
 internal (double X,double Y) Output {
  get{double radius=Math.Sqrt(RawX*RawX+RawY*RawY),zone=Deadzone/100.0;if(radius<=zone)return (0,0);double scale=(radius-zone)/(radius*(1-zone));return (Math.Clamp(RawX*scale,-1,1),Math.Clamp(RawY*scale,-1,1));}
 }
 protected override void OnRender(DrawingContext dc){
  base.OnRender(dc);double x=ActualWidth/2,y=ActualHeight/2-9,r=Math.Max(1,Math.Min(ActualWidth-24,ActualHeight-42)/2);
  dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(29,30,33)),InputDrawing.GridPen,new Point(x,y),r,r);
  dc.DrawLine(InputDrawing.GridPen,new Point(x-r,y),new Point(x+r,y));dc.DrawLine(InputDrawing.GridPen,new Point(x,y-r),new Point(x,y+r));
  dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(55,64,156,255)),new Pen(InputDrawing.Blue,1),new Point(x,y),r*Deadzone/100.0,r*Deadzone/100.0);
  if(HasSample){var output=Output;dc.DrawEllipse(null,new Pen(Brushes.White,1.5),new Point(x+RawX*r,y+RawY*r),5,5);dc.DrawEllipse(InputDrawing.Blue,null,new Point(x+output.X*r,y+output.Y*r),3,3);InputDrawing.Text(this,dc,$"X {output.X*100:0}%  Y {output.Y*100:0}%",Math.Max(2,x-57),ActualHeight-21,14);}
  else InputDrawing.Text(this,dc,L.T("Нет данных"),Math.Max(2,x-33),ActualHeight-21,14);
 }
}
public sealed class TouchpadView:FrameworkElement {
 internal InputPreview Sample=new();
 internal string? SelectedAction;
 internal event Action<string>? ZoneSelected;
 public TouchpadView(){Cursor=Cursors.Hand;MouseLeftButtonDown+=(_,e)=>{ZoneSelected?.Invoke(e.GetPosition(this).X<ActualWidth/2?"leftClick":"rightClick");e.Handled=true;};}
 protected override void OnRender(DrawingContext dc){
  base.OnRender(dc);var rect=new Rect(1,1,Math.Max(1,ActualWidth-2),Math.Max(1,ActualHeight-2));
  dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(30,31,35)),new Pen(new SolidColorBrush(Color.FromRgb(77,80,88)),1),rect,26,26);
  dc.PushClip(new RectangleGeometry(rect,26,26));
  if(SelectedAction is "leftClick" or "rightClick"){bool left=SelectedAction=="leftClick";dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(28,64,156,255)),null,new Rect(left?0:ActualWidth/2,0,ActualWidth/2,ActualHeight));}
  if(Sample.Fresh && Sample.Click)dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(45,64,156,255)),null,rect);
  var dash=new Pen(new SolidColorBrush(Color.FromRgb(69,72,80)),1){DashStyle=DashStyles.Dash};dc.DrawLine(dash,new Point(ActualWidth/2,14),new Point(ActualWidth/2,ActualHeight-14));
  InputDrawing.Text(this,dc,L.T("Левое нажатие"),18,20,14);InputDrawing.Text(this,dc,L.T("Правое нажатие"),ActualWidth/2+15,20,14);
  InputDrawing.Text(this,dc,"1920 × 1080",ActualWidth/2-37,ActualHeight-26,12);
  if(Sample.Fresh)for(int i=0;i<Sample.Touches.Length && i<2;i++){var touch=Sample.Touches[i];if(!touch.Active)continue;var point=new Point(8+Math.Clamp(touch.X/1920.0,0,1)*(ActualWidth-16),8+Math.Clamp(touch.Y/1080.0,0,1)*(ActualHeight-16));dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(45,64,156,255)),new Pen(InputDrawing.Blue,1),point,16,16);dc.DrawEllipse(InputDrawing.Blue,null,point,5,5);}
  dc.Pop();
 }
}
