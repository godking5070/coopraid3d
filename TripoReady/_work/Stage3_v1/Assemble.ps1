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
$package=Join-Path $workspace 'TripoReady\Stage3_v1'
$sourceDir=Join-Path $PSScriptRoot 'Sources'
$views=@('front','left','right','back')
$groups=@(
 @{key='normal';folder='Bodies\Normal';canvasW=2048;canvasH=1800;centerX=1024;baseline=1650;height=1500},
 @{key='hard';folder='Bodies\Hard';canvasW=2048;canvasH=1800;centerX=1024;baseline=1650;height=1500},
 @{key='belfryarch';folder='Buildings\BelfryArch';canvasW=1600;canvasH=1600;centerX=800;baseline=1460;height=1320},
 @{key='railing';folder='Buildings\Railing';canvasW=1600;canvasH=1600;centerX=800;baseline=1100;height=420},
 @{key='windingstation';folder='Buildings\WindingStation';canvasW=1600;canvasH=1600;centerX=800;baseline=1380;height=1000}
)
$placed=@()
foreach($group in $groups) {
 foreach($view in $views) {
  $key=$group.key+'-'+$view
  if($key -eq 'hard-front'){$key='hard-front-fixed'}
  $src=Join-Path $sourceDir ($key+'.png')
  $subject=[SheetArt]::Read($src)
  $canvas=[SheetArt]::Canvas($group.canvasW,$group.canvasH)
  $painter=[SheetArt]::Painter($canvas)
  $rect=[SheetArt]::Place($painter,$subject,$group.centerX,$group.baseline,$group.height)
  if($rect.Left -lt 40 -or $rect.Right -gt $group.canvasW-40 -or $rect.Top -lt 40){throw "Asset clipped: $key $rect"}
  $placed += [ordered]@{group=$group.key;view=$view;canvas=@($group.canvasW,$group.canvasH);centerX=$group.centerX;baseline=$group.baseline;height=$group.height;rect=@($rect.X,$rect.Y,$rect.Width,$rect.Height);source=$key+'.png'}
  $painter.Dispose();$subject.Dispose()
  $canvas.Save((Join-Path $package ($group.folder+'\'+$view+'.png')),[Drawing.Imaging.ImageFormat]::Png)
  $canvas.Dispose()
 }
}
$slots=@(
 @{id='cloak-normal';centerX=400;height=1050;minX=30;maxX=770},
 @{id='cloak-hard';centerX=1170;height=1050;minX=800;maxX=1540},
 @{id='clapper';centerX=1870;height=1250;minX=1650;maxX=2090},
 @{id='drive';centerX=2480;height=650;minX=2120;maxX=2840},
 @{id='chain';centerX=3340;height=500;minX=2910;maxX=3770}
)
$accessoryPlacements=@()
foreach($view in $views) {
 $canvas=[SheetArt]::Canvas(3840,1800);$painter=[SheetArt]::Painter($canvas)
 foreach($slot in $slots) {
  $sourceKey=$slot.id+'-'+$view
  if($sourceKey -eq 'drive-right'){$sourceKey='drive-right-fixed'}
  $src=Join-Path $sourceDir ($sourceKey+'.png')
  $subject=[SheetArt]::Read($src)
  $rect=[SheetArt]::Place($painter,$subject,$slot.centerX,1600,$slot.height)
  if($rect.Left -lt $slot.minX -or $rect.Right -gt $slot.maxX){throw "Accessory outside fixed slot: $($slot.id) $view $rect"}
  $accessoryPlacements += [ordered]@{item=$slot.id;view=$view;centerX=$slot.centerX;baseline=1600;height=$slot.height;x=$rect.X;y=$rect.Y;width=$rect.Width;sourceBounds=@($subject.Bounds.X,$subject.Bounds.Y,$subject.Bounds.Width,$subject.Bounds.Height)}
  $subject.Dispose()
 }
 $painter.Dispose()
 $canvas.Save((Join-Path $package ('Accessories\'+$view+'.png')),[Drawing.Imaging.ImageFormat]::Png);$canvas.Dispose()
}
[ordered]@{canvas=@{width=3840;height=1800;background='#E8E8E8'};slotOrder=@('cloak-normal','cloak-hard','clapper','drive','chain');method='Deterministic slot placement. Same per-item bounding-box horizontal center, bottom baseline and fixed height in each view. No whole-sheet mirroring.';slots=$slots;placements=$accessoryPlacements}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $package 'accessory-layout.json') -Encoding UTF8
$placed|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $package 'image-alignment.json') -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $sourceDir 'floor-top.png') -Destination (Join-Path $package 'Floor\top.png') -Force
Write-Output 'Created Stage3: 8 boss bodies, 4 fixed accessory sheets, 12 architectural views, 1 floor.'

