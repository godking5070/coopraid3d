param([switch]$AccessoriesOnly)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
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
'@

$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$package=Join-Path $workspace 'TripoReady\Stage2_v1'
$sourceDir=Join-Path $PSScriptRoot 'Sources'
$views=@('front','left','right','back')
$slots=@(
 @{id='shears';centerX=450;height=1400;minX=50;maxX=950},
 @{id='spring';centerX=1775;height=1250;minX=1100;maxX=2400},
 @{id='key';centerX=3000;height=350;minX=2450;maxX=3550}
)
$placements=@()
foreach($view in $views){
 $canvas=[SheetArt]::Canvas(3600,1800);$g=[SheetArt]::Painter($canvas)
 foreach($slot in $slots){
  $subject=[SheetArt]::Read((Join-Path $sourceDir ($slot.id+'-'+$view+'.png')))
  $rect=[SheetArt]::Place($g,$subject,$slot.centerX,1600,$slot.height)
  if($rect.Left -lt $slot.minX -or $rect.Right -gt $slot.maxX){throw "Accessory overflow: $($slot.id) $view $rect"}
  $placements += [ordered]@{item=$slot.id;view=$view;centerX=$slot.centerX;baseline=1600;height=$slot.height;x=$rect.X;y=$rect.Y;width=$rect.Width}
  $subject.Dispose()
 }
 $g.Dispose();$canvas.Save((Join-Path $package ('Accessories\'+$view+'.png')),[Drawing.Imaging.ImageFormat]::Png);$canvas.Dispose()
}
[ordered]@{canvas=@{width=3600;height=1800;background='#E8E8E8'};slotOrder=@('shears','spring','key');slots=$slots;placements=$placements;method='Deterministic per-object bounding-box center X, height and baseline. Camera view changes only; complete sheet never mirrored.'} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $package 'accessory-layout.json') -Encoding UTF8
foreach($mode in @('Normal','Hard')){
 foreach($view in $views){
  $subject=[SheetArt]::Read((Join-Path $sourceDir ($mode.ToLower()+'-'+$view+'.png')))
  $canvas=[SheetArt]::Canvas(2048,1600);$g=[SheetArt]::Painter($canvas)
  $rect=[SheetArt]::Place($g,$subject,1024,1460,1300)
  if($rect.Left -lt 40 -or $rect.Right -gt 2008){throw "Body clipped: $mode $view"}
  $g.Dispose();$subject.Dispose();$canvas.Save((Join-Path $package ('Bodies\'+$mode+'\'+$view+'.png')),[Drawing.Imaging.ImageFormat]::Png);$canvas.Dispose()
 }
}
foreach($item in @(@{id='entrance';folder='EntranceBay';height=1300;baseline=1460},@{id='balcony';folder='BalconyStairs';height=600;baseline=1160},@{id='vent';folder='VentCabinet';height=1300;baseline=1460})){
 $folder=Join-Path $package ('Buildings\'+$item.folder);New-Item -ItemType Directory -Force -Path $folder | Out-Null
 foreach($view in $views){
  $subject=[SheetArt]::Read((Join-Path $sourceDir ($item.id+'-'+$view+'.png')))
  $canvas=[SheetArt]::Canvas(1600,1600);$g=[SheetArt]::Painter($canvas)
  $rect=[SheetArt]::Place($g,$subject,800,$item.baseline,$item.height)
  if($rect.Left -lt 40 -or $rect.Right -gt 1560){throw "Building clipped: $($item.id) $view $rect"}
  $g.Dispose();$subject.Dispose();$canvas.Save((Join-Path $folder ($view+'.png')),[Drawing.Imaging.ImageFormat]::Png);$canvas.Dispose()
 }
}
Copy-Item -LiteralPath (Join-Path $sourceDir 'floor-top.png') -Destination (Join-Path $package 'Floor\top.png') -Force
Write-Output 'Built 25 modeling PNGs with fixed accessory slot layout.'


