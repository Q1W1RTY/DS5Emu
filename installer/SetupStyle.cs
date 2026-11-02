using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

internal static class SetupShape {
 internal static GraphicsPath Round(Rectangle bounds,int radius) {
  var path=new GraphicsPath();int d=Math.Min(radius*2,Math.Min(bounds.Width,bounds.Height));
  path.AddArc(bounds.Left,bounds.Top,d,d,180,90);path.AddArc(bounds.Right-d,bounds.Top,d,d,270,90);
  path.AddArc(bounds.Right-d,bounds.Bottom-d,d,d,0,90);path.AddArc(bounds.Left,bounds.Bottom-d,d,d,90,90);path.CloseFigure();return path;
 }
}
internal sealed class SetupCard:Panel {
 internal SetupCard(){BackColor=Color.FromArgb(35,35,38);SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
 protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);if(Width>0 && Height>0)using(var shape=SetupShape.Round(ClientRectangle,20)){var old=Region;Region=new Region(shape);if(old!=null)old.Dispose();}}
}
internal sealed class SetupButton:Button {
 readonly bool primary;bool hover,down;internal bool Ready;
 internal SetupButton(bool primary){this.primary=primary;FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;UseVisualStyleBackColor=false;Cursor=Cursors.Hand;Font=new Font("Segoe UI Semibold",9);SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
 protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
 protected override void OnMouseLeave(EventArgs e){hover=down=false;Invalidate();base.OnMouseLeave(e);}
 protected override void OnMouseDown(MouseEventArgs e){down=true;Invalidate();base.OnMouseDown(e);}
 protected override void OnMouseUp(MouseEventArgs e){down=false;Invalidate();base.OnMouseUp(e);}
 protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);Invalidate();}
 protected override void OnPaint(PaintEventArgs e){
  e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
  e.Graphics.Clear(Parent==null?Color.FromArgb(22,22,24):Parent.BackColor);
  var fill=Ready?Color.FromArgb(35,59,44):primary?(Enabled?Color.FromArgb(10,132,255):Color.FromArgb(40,58,78)):Color.FromArgb(54,54,58);
  if(Enabled && hover)fill=primary?Color.FromArgb(35,148,255):Color.FromArgb(68,68,74);
  if(Enabled && down)fill=Color.FromArgb(50,93,140);
  using(var shape=SetupShape.Round(new Rectangle(0,0,Width-1,Height-1),12))using(var brush=new SolidBrush(fill))e.Graphics.FillPath(brush,shape);
  var ink=Ready?Color.FromArgb(93,210,127):Enabled?Color.FromArgb(240,241,243):Color.FromArgb(150,150,160);
  TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
  if(Focused && ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(5,5,Width-10,Height-10),ink,fill);
 }
}
