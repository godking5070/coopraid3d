using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
public class SheetSubject : IDisposable {
  public Bitmap Image;
  public Rectangle Bounds;
  public void Dispose() { Image.Dispose(); }
}
public static class SheetArt {
  public static SheetSubject Read(string path) {
    Bitmap source = new Bitmap(path);
    Bitmap bmp = new Bitmap(source.Width,source.Height,PixelFormat.Format32bppArgb);
    using(Graphics g=Graphics.FromImage(bmp)) { g.DrawImageUnscaled(source,0,0); }
    source.Dispose();
    int w=bmp.Width,h=bmp.Height;
    BitmapData data=bmp.LockBits(new Rectangle(0,0,w,h),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
    byte[] b=new byte[data.Stride*h]; Marshal.Copy(data.Scan0,b,0,b.Length);
    bool[] bg=new bool[w*h]; int[] q=new int[w*h]; int head=0,tail=0;
    Action<int> seed = delegate(int id) {
      if(bg[id]) return; int i=(id/w)*data.Stride+(id%w)*4;
      int low=Math.Min(b[i],Math.Min(b[i+1],b[i+2]));
      int high=Math.Max(b[i],Math.Max(b[i+1],b[i+2]));
      if(b[i+3]==0 || (low>=195 && high-low<=18)) { bg[id]=true; q[tail++]=id; }
    };
    for(int x=0;x<w;x++) { seed(x); seed((h-1)*w+x); }
    for(int y=0;y<h;y++) { seed(y*w); seed(y*w+w-1); }
    while(head<tail) {
      int id=q[head++],x=id%w,y=id/w;
      if(x>0)seed(id-1); if(x<w-1)seed(id+1); if(y>0)seed(id-w); if(y<h-1)seed(id+w);
    }
    int minx=w,miny=h,maxx=0,maxy=0;
    for(int y=0;y<h;y++) for(int x=0;x<w;x++) {
      int id=y*w+x, i=y*data.Stride+x*4;
      if(bg[id]) b[i+3]=0;
      else { minx=Math.Min(minx,x); maxx=Math.Max(maxx,x); miny=Math.Min(miny,y); maxy=Math.Max(maxy,y); }
    }
    Marshal.Copy(b,0,data.Scan0,b.Length); bmp.UnlockBits(data);
    if(minx>=maxx || miny>=maxy)throw new Exception("No subject: "+path);
    return new SheetSubject{Image=bmp,Bounds=Rectangle.FromLTRB(minx,miny,maxx+1,maxy+1)};
  }
  public static Rectangle Place(Graphics g,SheetSubject s,int centerX,int baseline,int height) {
    int width=(int)Math.Round((double)s.Bounds.Width*height/s.Bounds.Height);
    Rectangle dest=new Rectangle(centerX-width/2,baseline-height,width,height);
    g.DrawImage(s.Image,dest,s.Bounds,GraphicsUnit.Pixel);
    return dest;
  }
  public static Bitmap Canvas(int w,int h) {
    Bitmap b=new Bitmap(w,h,PixelFormat.Format24bppRgb);
    using(Graphics g=Graphics.FromImage(b))g.Clear(Color.FromArgb(232,232,232));
    return b;
  }
  public static Graphics Painter(Bitmap b) {
    Graphics g=Graphics.FromImage(b);
    g.InterpolationMode=InterpolationMode.HighQualityBicubic;
    g.PixelOffsetMode=PixelOffsetMode.HighQuality;
    g.CompositingQuality=CompositingQuality.HighQuality;
    return g;
  }
}
