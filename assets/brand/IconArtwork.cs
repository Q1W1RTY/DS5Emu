using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// Original vector artwork; shared sizes are rasterized from these paths.
public static class IconArtwork {
 static GraphicsPath Round(float x,float y,float w,float h,float r) {
  var p=new GraphicsPath(); float d=r*2;
  p.AddArc(x,y,d,d,180,90);p.AddArc(x+w-d,y,d,d,270,90);
  p.AddArc(x+w-d,y+h-d,d,d,0,90);p.AddArc(x,y+h-d,d,d,90,90);p.CloseFigure();return p;
 }
 public static Bitmap Render(int size) {
  using(var large=new Bitmap(size*4,size*4,PixelFormat.Format32bppArgb)) {
   using(var g=Graphics.FromImage(large)) {
    g.SmoothingMode=SmoothingMode.AntiAlias;g.ScaleTransform(size*4/256f,size*4/256f);
    using(var bg=new SolidBrush(Color.FromArgb(28,33,37)))
    using(var tile=Round(8,8,240,240,55))g.FillPath(bg,tile);
    using(var mint=new SolidBrush(Color.FromArgb(169,216,195)))
    using(var pad=new GraphicsPath()) {
     pad.AddBezier(87,76,64,76,54,92,48,119);
     pad.AddBezier(48,119,40,146,33,177,47,186);
     pad.AddBezier(47,186,59,194,72,180,88,160);
     pad.AddLine(88,160,168,160);
     pad.AddBezier(168,160,184,180,197,194,209,186);
     pad.AddBezier(209,186,223,177,216,146,208,119);
     pad.AddBezier(208,119,202,92,192,76,169,76);
     pad.CloseFigure();g.FillPath(mint,pad);
    }
    using(var dark=new SolidBrush(Color.FromArgb(28,33,37))) {
     using(var touch=Round(100,86,56,29,7))g.FillPath(dark,touch);
     using(var cross=Round(67,109,12,38,3))g.FillPath(dark,cross);
     using(var cross=Round(54,122,38,12,3))g.FillPath(dark,cross);
     g.FillEllipse(dark,177,107,12,12);g.FillEllipse(dark,191,121,12,12);
     g.FillEllipse(dark,163,121,12,12);g.FillEllipse(dark,177,135,12,12);
     g.FillEllipse(dark,95,131,22,22);g.FillEllipse(dark,139,131,22,22);
    }
   }
   var result=new Bitmap(size,size,PixelFormat.Format32bppArgb);
   using(var g=Graphics.FromImage(result)){g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.CompositingQuality=CompositingQuality.HighQuality;g.DrawImage(large,0,0,size,size);}
   return result;
  }
 }
}
