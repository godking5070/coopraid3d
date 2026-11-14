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
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$package = Join-Path $workspace 'TripoReady\Stage1_v2'
$sourceMap = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'accessory-sources.json') -Raw | ConvertFrom-Json
$slots = @(
  @{id='Club'; centerX=450; height=1400; minX=80; maxX=850},
  @{id='Shield'; centerX=1400; height=1360; minX=940; maxX=1840},
  @{id='Wing'; centerX=2500; height=1200; minX=1850; maxX=3150}
)
$records = @()
foreach($view in @('front','left','right','back')) {
  $canvas = [SheetArt]::Canvas(3200,1800)
  $painter = [SheetArt]::Painter($canvas)
  foreach($slot in $slots) {
    $src = $sourceMap.($slot.id).$view
    $internalDir = Join-Path $PSScriptRoot ('Sources\'+$slot.id)
    New-Item -ItemType Directory -Force -Path $internalDir | Out-Null
    Copy-Item -LiteralPath $src -Destination (Join-Path $internalDir ($view+'.png')) -Force
    $subject = [SheetArt]::Read($src)
    $bounds = $subject.Bounds
    $rect = [SheetArt]::Place($painter,$subject,$slot.centerX,1600,$slot.height)
    if($rect.Left -lt $slot.minX -or $rect.Right -gt $slot.maxX) { throw "Accessory exceeds fixed slot: $($slot.id) $view $rect" }
    $records += [ordered]@{view=$view;item=$slot.id;centerX=$slot.centerX;baseline=1600;height=$slot.height;x=$rect.X;y=$rect.Y;width=$rect.Width;sourceBounds=@($bounds.X,$bounds.Y,$bounds.Width,$bounds.Height)}
    $subject.Dispose()
  }
  $painter.Dispose()
  $out=Join-Path $package ('Accessories\'+$view+'.png')
  $canvas.Save($out,[Drawing.Imaging.ImageFormat]::Png); $canvas.Dispose()
}
$layout=[ordered]@{canvas=@{width=3200;height=1800;background='#E8E8E8'};method='Deterministic placement; no whole-sheet mirroring; subject bounding-box center X and bottom Y fixed per item across views; isotropic scaling to a fixed per-item height.';slotOrder=@('Club','Shield','Wing');slots=$slots;placements=$records}
$layout | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $package 'accessory-layout.json') -Encoding UTF8
Write-Output 'Created 4 accessory sheets; fixed centers, heights and baselines checked.'
if($AccessoriesOnly) { return }
$buildingMapPath=Join-Path $PSScriptRoot 'building-sources.json'
if(Test-Path -LiteralPath $buildingMapPath) {
  $buildings=Get-Content -LiteralPath $buildingMapPath -Raw | ConvertFrom-Json
  foreach($name in @('ClockGate','House','Plinth')) {
    $outDir=Join-Path $package ('Buildings\'+$name)
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    foreach($view in @('front','left','right','back')) {
      $subject=[SheetArt]::Read($buildings.$name.$view)
      $canvas=[SheetArt]::Canvas(1600,1600); $painter=[SheetArt]::Painter($canvas)
      $height=1300; $baseline=1460
      if($name -eq 'Plinth') { $height=360; $baseline=1000 }
      $rect=[SheetArt]::Place($painter,$subject,800,$baseline,$height)
      if($rect.Left -lt 60 -or $rect.Right -gt 1540) { throw "Building clipped: $name $view $rect" }
      $painter.Dispose(); $subject.Dispose()
      $canvas.Save((Join-Path $outDir ($view+'.png')),[Drawing.Imaging.ImageFormat]::Png); $canvas.Dispose()
    }
  }
  Write-Output 'Normalized 12 building elevations to per-asset fixed heights and baselines.'
}
$generationRoot = 'C:\Users\godki\.codex\generated_images\01a1b621-9017-7791-b5f0-ced8ce198bcd'
foreach($mode in @('Normal','Hard')) {
  foreach($view in @('front','left','right','back')) {
    $oldFolder = '01_Normal'
    $newRight = 'exec-1e631d34-00a2-4e42-baf8-f135270301dc.png'
    if($mode -eq 'Hard') { $oldFolder='02_Hard'; $newRight='exec-704a6a23-66fb-44b0-a6b0-1bcf593c1b99.png' }
    $src=Join-Path $workspace ('TripoReady\Stage1\'+$oldFolder+'\'+$view+'.png')
    if($view -eq 'right') { $src=Join-Path $generationRoot $newRight }
    $subject=[SheetArt]::Read($src)
    $canvas=[SheetArt]::Canvas(2048,1600); $painter=[SheetArt]::Painter($canvas)
    $rect=[SheetArt]::Place($painter,$subject,1024,1460,1300)
    if($rect.Left -lt 40 -or $rect.Right -gt 2008) { throw "Body clipped: $mode $view $rect" }
    $painter.Dispose(); $subject.Dispose()
    $canvas.Save((Join-Path $package ('Bodies\'+$mode+'\'+$view+'.png')),[Drawing.Imaging.ImageFormat]::Png); $canvas.Dispose()
  }
}
Write-Output 'Normalized 8 body images to fixed height, horizontal center and baseline.'
