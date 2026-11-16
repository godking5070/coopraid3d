[CmdletBinding()]
param(
    [string]$SourceAtlas = '',
    [string]$ProjectRoot = 'C:/Astella/Astella'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$rasterDirectory = $PSScriptRoot
[IO.Directory]::CreateDirectory($rasterDirectory) | Out-Null
$rasterAtlas = Join-Path $rasterDirectory 'painted-transparent-atlas.png'
if ([string]::IsNullOrWhiteSpace($SourceAtlas)) { $SourceAtlas = $rasterAtlas }
if (!(Test-Path -LiteralPath $rasterAtlas)) { [IO.File]::Copy($SourceAtlas, $rasterAtlas, $false) }
elseif ((Get-FileHash -LiteralPath $SourceAtlas).Hash -ne (Get-FileHash -LiteralPath $rasterAtlas).Hash) { throw 'The preserved gauge source differs; do not overwrite it.' }

if (-not ('PaintedPrismGaugeExtraction' -as [type])) {
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;

public static class PaintedPrismGaugeExtraction
{
    private struct C { public double R,G,B,A; public C(double r,double g,double b,double a){R=r;G=g;B=b;A=a;} }
    private sealed class Triangle { public PointF A,B,C,S,T,U; }
    private static byte[] source;
    private static int sourceWidth,sourceHeight,stride;
    private const int Samples=4;
    private static readonly List<string> written=new List<string>();
    private static PointF P(float x,float y){return new PointF(x,y);}
    private static PointF[] Poly(params float[] v){var p=new PointF[v.Length/2];for(int i=0;i<p.Length;i++)p[i]=P(v[i*2],v[i*2+1]);return p;}

    public static string[] Run(string atlas,string project,string recovery)
    {
        written.Clear();
        using(var original=new Bitmap(atlas)) using(var bitmap=new Bitmap(original.Width,original.Height,PixelFormat.Format32bppArgb))
        {
            using(var g=Graphics.FromImage(bitmap)){g.CompositingMode=CompositingMode.SourceCopy;g.DrawImageUnscaled(original,0,0);}
            sourceWidth=bitmap.Width;sourceHeight=bitmap.Height;
            var data=bitmap.LockBits(new Rectangle(0,0,bitmap.Width,bitmap.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            stride=data.Stride;source=new byte[stride*bitmap.Height];Marshal.Copy(data.Scan0,source,0,source.Length);bitmap.UnlockBits(data);
        }

        // Source paths trace the clean metal inside the atlas's stray colored fringe.
        PointF[] hpOuter=Poly(77,654,115,627,650,627,687,654,650,682,115,682);
        PointF[] hpInner=Poly(95,654,119,631,646,631,674,654,646,675,119,675);
        PointF[] playerOuter=Poly(12,48,60,10,964,10,1012,48,964,86,60,86);
        PointF[] playerInner=Poly(29,48,65,18,959,18,995,48,959,78,65,78);
        Save(project,recovery,"Player/gauge-frame",Ring(1024,96,hpOuter,hpInner,playerOuter,playerInner,null,null));
        Save(project,recovery,"Player/gauge-track",Paint(1024,96,playerInner,new RectangleF(1425,639,115,31),0));
        Save(project,recovery,"Player/hp-fill",Paint(1024,96,playerInner,new RectangleF(127,638,508,33),0));
        Save(project,recovery,"Player/resource-fill",Paint(1024,96,playerInner,new RectangleF(1030,638,285,33),0));
        Save(project,recovery,"Player/gauge-inner-mask",Paint(1024,96,playerInner,RectangleF.Empty,2));

        PointF[] bossOuter=Poly(424,118,444,97,1234,97,1254,118,1234,141,444,141);
        PointF[] bossInner=Poly(434,118,448,103,1229,103,1244,118,1229,134,448,134);
        PointF[] bossTargetOuter=Poly(14,48,44,22,1236,22,1266,48,1236,74,44,74);
        PointF[] bossTargetInner=Poly(27,48,49,29,1231,29,1253,48,1231,67,49,67);
        // Native boss top/bottom paint; lower-bar corner paint reconstructs the metal concealed by gems.
        Save(project,recovery,"Boss/boss-frame",Ring(1280,96,bossOuter,bossInner,bossTargetOuter,bossTargetInner,hpOuter,hpInner));
        Save(project,recovery,"Boss/boss-track",Paint(1280,96,bossTargetInner,new RectangleF(1425,639,115,31),0));
        Save(project,recovery,"Boss/boss-fill",Paint(1280,96,bossTargetInner,new RectangleF(465,106,740,25),0));
        Save(project,recovery,"Boss/boss-inner-mask",Paint(1280,96,bossTargetInner,RectangleF.Empty,2));
        Save(project,recovery,"Boss/boss-gem-left",Gem(Poly(377,118,426,92,448,118,426,142)));
        Save(project,recovery,"Boss/boss-gem-right",Gem(Poly(1227,118,1252,92,1297,118,1252,142)));

        PointF[] castOuter=Poly(572,151,582,143,1089,143,1099,151,1089,159,582,159);
        PointF[] castInner=Poly(579,151,585,147,1086,147,1092,151,1086,155,585,155);
        PointF[] castTargetOuter=Poly(4,24,17,17,495,17,508,24,495,31,17,31);
        PointF[] castTargetInner=Poly(15,24,22,20,490,20,497,24,490,28,22,28);
        Save(project,recovery,"Boss/cast-frame",Ring(512,48,castOuter,castInner,castTargetOuter,castTargetInner,null,null));
        Save(project,recovery,"Boss/cast-track",Paint(512,48,castTargetInner,new RectangleF(978,149,95,5),0));
        Save(project,recovery,"Boss/cast-fill",Paint(512,48,castTargetInner,new RectangleF(600,149,310,4),0));
        Save(project,recovery,"Boss/cast-inner-mask",Paint(512,48,castTargetInner,RectangleF.Empty,2));

        PointF[] partyOuter=Poly(120,129,131,117,252,117,265,129,252,141,131,141);
        PointF[] partyInner=Poly(125,129,134,123,249,123,258,129,249,135,134,135);
        PointF[] partyTargetOuter=Poly(4,20,20,6,300,6,316,20,300,34,20,34);
        PointF[] partyTargetInner=Poly(14,20,24,12,296,12,306,20,296,28,24,28);
        Save(project,recovery,"Party/bar-frame",Ring(320,40,partyOuter,partyInner,partyTargetOuter,partyTargetInner,null,null));
        Save(project,recovery,"Party/bar-track",Paint(320,40,partyTargetInner,new RectangleF(1425,639,115,31),0));
        Save(project,recovery,"Party/bar-fill",Paint(320,40,partyTargetInner,new RectangleF(138,125,92,8),0));
        Save(project,recovery,"Party/bar-fill-critical",Paint(320,40,partyTargetInner,new RectangleF(138,125,92,8),1));
        Save(project,recovery,"Party/bar-inner-mask",Paint(320,40,partyTargetInner,RectangleF.Empty,2));
        Preview(project,Path.Combine(recovery,"gauge-raster-contact.png"));
        Verify(project);
        return written.ToArray();
    }

    public static void Verify(string project)
    {
        VerifyGroup(project,"Player/",new[]{"gauge-track","hp-fill","resource-fill","gauge-inner-mask"},31,48,993,48);
        VerifyGroup(project,"Boss/",new[]{"boss-track","boss-fill","boss-inner-mask"},29,48,1251,48);
        VerifyGroup(project,"Boss/",new[]{"cast-track","cast-fill","cast-inner-mask"},17,24,495,24);
        VerifyGroup(project,"Party/",new[]{"bar-track","bar-fill","bar-fill-critical","bar-inner-mask"},16,20,304,20);
        foreach(string name in new[]{"Player/gauge-frame","Boss/boss-frame","Boss/cast-frame","Party/bar-frame"})
            using(var image=new Bitmap(Path.Combine(project,"Assets/UI/AstellaPrismV4/Sprites",name+".png")))
                if(image.GetPixel(image.Width/2,image.Height/2).A!=0)throw new InvalidOperationException("Frame hole is not transparent: "+name);
        foreach(string name in new[]{"Boss/boss-gem-left","Boss/boss-gem-right"})
            using(var image=new Bitmap(Path.Combine(project,"Assets/UI/AstellaPrismV4/Sprites",name+".png")))
                if(image.GetPixel(0,0).A!=0||image.GetPixel(95,95).A!=0)throw new InvalidOperationException("Boss gem has opaque rectangular background: "+name);
    }

    private static void VerifyGroup(string project,string folder,string[] names,int leftX,int leftY,int rightX,int rightY)
    {
        byte[] expected=null;
        foreach(string name in names)
        {
            using(var image=new Bitmap(Path.Combine(project,"Assets/UI/AstellaPrismV4/Sprites",folder+name+".png")))
            {
                if(image.GetPixel(leftX,leftY).A<192||image.GetPixel(rightX,rightY).A<192)
                    throw new InvalidOperationException("A pointed inner tip is empty: "+folder+name);
                if(image.GetPixel(0,0).A!=0||image.GetPixel(image.Width-1,image.Height-1).A!=0)
                    throw new InvalidOperationException("Opaque canvas corner: "+folder+name);
                var data=image.LockBits(new Rectangle(0,0,image.Width,image.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
                var bytes=new byte[data.Stride*image.Height];Marshal.Copy(data.Scan0,bytes,0,bytes.Length);image.UnlockBits(data);
                if(expected!=null){for(int i=3;i<bytes.Length;i+=4)if(bytes[i]!=expected[i])throw new InvalidOperationException("Track/fill/mask alpha mismatch: "+folder+name);}
                else expected=bytes;
            }
        }
    }

    private static Bitmap Ring(int width,int height,PointF[] srcOuter,PointF[] srcInner,PointF[] dstOuter,PointF[] dstInner,PointF[] cornerOuter,PointF[] cornerInner)
    {
        var triangles=new List<Triangle>();
        for(int i=0;i<dstOuter.Length;i++)
        {
            int next=(i+1)%dstOuter.Length;
            PointF[] so=(cornerOuter!=null && i!=1 && i!=4)?cornerOuter:srcOuter;
            PointF[] si=(cornerInner!=null && i!=1 && i!=4)?cornerInner:srcInner;
            triangles.Add(new Triangle{A=dstOuter[i],B=dstOuter[next],C=dstInner[next],S=so[i],T=so[next],U=si[next]});
            triangles.Add(new Triangle{A=dstOuter[i],B=dstInner[next],C=dstInner[i],S=so[i],T=si[next],U=si[i]});
        }
        return Render(width,height,delegate(double x,double y){
            foreach(var t in triangles)
            {
                double a,b,c;
                if(!Barycentric(t,x,y,out a,out b,out c))continue;
                return Sample(a*t.S.X+b*t.T.X+c*t.U.X,a*t.S.Y+b*t.T.Y+c*t.U.Y);
            }
            return new C();
        });
    }

    private static bool Barycentric(Triangle t,double x,double y,out double a,out double b,out double c)
    {
        double d=(t.B.Y-t.C.Y)*(t.A.X-t.C.X)+(t.C.X-t.B.X)*(t.A.Y-t.C.Y);
        a=((t.B.Y-t.C.Y)*(x-t.C.X)+(t.C.X-t.B.X)*(y-t.C.Y))/d;
        b=((t.C.Y-t.A.Y)*(x-t.C.X)+(t.A.X-t.C.X)*(y-t.C.Y))/d;c=1-a-b;
        return a>=-0.000001 && b>=-0.000001 && c>=-0.000001;
    }

    private static Bitmap Paint(int width,int height,PointF[] polygon,RectangleF texture,int mode)
    {
        RectangleF bounds=Bounds(polygon);
        return Render(width,height,delegate(double x,double y){
            if(!Inside(polygon,x,y))return new C();
            if(mode==2)return new C(255,255,255,1);
            double u=(x-bounds.Left)/bounds.Width,v=(y-bounds.Top)/bounds.Height;
            C p=Sample(texture.Left+u*texture.Width,texture.Top+v*texture.Height);
            p.A=1;
            if(mode==1){double l=.22*p.R+.65*p.G+.13*p.B;p.R=Math.Min(255,l*1.22);p.G=l*.47;p.B=l*.65;}
            return p;
        });
    }

    private static Bitmap Gem(PointF[] sourcePolygon)
    {
        RectangleF sourceBounds=Bounds(sourcePolygon);
        double scale=92.0/sourceBounds.Width;
        double height=sourceBounds.Height*scale;
        return Render(96,96,delegate(double x,double y){
            double sx=sourceBounds.Left+(x-2)/scale,sy=sourceBounds.Top+(y-(96-height)*.5)/scale;
            return Inside(sourcePolygon,sx,sy)?Sample(sx,sy):new C();
        });
    }

    private static Bitmap Render(int width,int height,Func<double,double,C> paint)
    {
        byte[] pixels=new byte[width*height*4];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            double red=0,green=0,blue=0,alpha=0;
            for(int sy=0;sy<Samples;sy++)for(int sx=0;sx<Samples;sx++)
            {
                C c=paint(x+(sx+.5)/Samples,y+(sy+.5)/Samples);
                red+=c.R*c.A;green+=c.G*c.A;blue+=c.B*c.A;alpha+=c.A;
            }
            int offset=(y*width+x)*4;
            if(alpha>0){pixels[offset]=Byte(blue/alpha);pixels[offset+1]=Byte(green/alpha);pixels[offset+2]=Byte(red/alpha);pixels[offset+3]=Byte(alpha*255/(Samples*Samples));}
        }
        var bitmap=new Bitmap(width,height,PixelFormat.Format32bppArgb);
        var data=bitmap.LockBits(new Rectangle(0,0,width,height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
        Marshal.Copy(pixels,0,data.Scan0,pixels.Length);bitmap.UnlockBits(data);return bitmap;
    }

    private static C Sample(double x,double y)
    {
        x=Math.Max(0,Math.Min(sourceWidth-1.001,x));y=Math.Max(0,Math.Min(sourceHeight-1.001,y));
        int ix=(int)x,iy=(int)y;double fx=x-ix,fy=y-iy;
        C a=Read(ix,iy),b=Read(ix+1,iy),c=Read(ix,iy+1),d=Read(ix+1,iy+1);
        double wa=(1-fx)*(1-fy),wb=fx*(1-fy),wc=(1-fx)*fy,wd=fx*fy;
        double alpha=a.A*wa+b.A*wb+c.A*wc+d.A*wd;
        if(alpha<.000001)return new C();
        return new C((a.R*a.A*wa+b.R*b.A*wb+c.R*c.A*wc+d.R*d.A*wd)/alpha,
            (a.G*a.A*wa+b.G*b.A*wb+c.G*c.A*wc+d.G*d.A*wd)/alpha,
            (a.B*a.A*wa+b.B*b.A*wb+c.B*c.A*wc+d.B*d.A*wd)/alpha,alpha);
    }
    private static C Read(int x,int y){int i=y*stride+x*4;return new C(source[i+2],source[i+1],source[i],source[i+3]/255.0);}
    private static byte Byte(double value){return (byte)Math.Max(0,Math.Min(255,Math.Round(value)));}
    private static RectangleF Bounds(PointF[] points){float l=float.MaxValue,t=float.MaxValue,r=float.MinValue,b=float.MinValue;foreach(var p in points){l=Math.Min(l,p.X);t=Math.Min(t,p.Y);r=Math.Max(r,p.X);b=Math.Max(b,p.Y);}return RectangleF.FromLTRB(l,t,r,b);}
    private static bool Inside(PointF[] points,double x,double y)
    {
        bool inside=false;
        for(int i=0,j=points.Length-1;i<points.Length;j=i++)
            if((points[i].Y>y)!=(points[j].Y>y) && x<(points[j].X-points[i].X)*(y-points[i].Y)/(points[j].Y-points[i].Y)+points[i].X)inside=!inside;
        return inside;
    }

    private static void Save(string project,string recovery,string relative,Bitmap bitmap)
    {
        if(!(relative.StartsWith("Player/")||relative.StartsWith("Boss/")||relative.StartsWith("Party/bar-")))throw new InvalidOperationException("Outside owned gauge paths: "+relative);
        string target=Path.Combine(project,"Assets/UI/AstellaPrismV4/Sprites",relative+".png");
        string backup=Path.Combine(recovery,"original-vector-gauges",relative+".png");
        Directory.CreateDirectory(Path.GetDirectoryName(backup));
        if(File.Exists(target)&&!File.Exists(backup))File.Copy(target,backup,false);
        using(bitmap){bitmap.Save(target,ImageFormat.Png);}written.Add(target);
    }

    private static void Preview(string project,string destination)
    {
        using(var image=new Bitmap(1400,760,PixelFormat.Format32bppArgb))using(var g=Graphics.FromImage(image))using(var font=new Font("Segoe UI",20))
        {
            g.Clear(Color.FromArgb(13,16,33));g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            g.DrawString("Painted atlas pixels / separated gauges",font,Brushes.White,24,14);
            Composite(g,project,"Player/gauge-track","Player/hp-fill","Player/gauge-frame",new Rectangle(24,76,1024,96));
            Composite(g,project,"Player/gauge-track","Player/resource-fill","Player/gauge-frame",new Rectangle(24,205,1024,96));
            Composite(g,project,"Boss/boss-track","Boss/boss-fill","Boss/boss-frame",new Rectangle(40,354,1280,96));
            Draw(g,project,"Boss/boss-gem-left",new Rectangle(24,354,96,96));Draw(g,project,"Boss/boss-gem-right",new Rectangle(1240,354,96,96));
            Composite(g,project,"Boss/cast-track","Boss/cast-fill","Boss/cast-frame",new Rectangle(500,478,512,48));
            Composite(g,project,"Party/bar-track","Party/bar-fill","Party/bar-frame",new Rectangle(24,574,640,80));
            Composite(g,project,"Party/bar-track","Party/bar-fill-critical","Party/bar-frame",new Rectangle(724,574,640,80));
            g.DrawString("RGB is sampled from the image_gen atlas. Runtime text is not baked. Existing .meta files preserved.",new Font("Segoe UI",14),Brushes.White,24,700);
            image.Save(destination,ImageFormat.Png);
        }
    }
    private static void Composite(Graphics g,string project,string track,string fill,string frame,Rectangle rect){Draw(g,project,track,rect);Draw(g,project,fill,rect);Draw(g,project,frame,rect);}
    private static void Draw(Graphics g,string project,string asset,Rectangle rect){using(var bmp=new Bitmap(Path.Combine(project,"Assets/UI/AstellaPrismV4/Sprites",asset+".png")))g.DrawImage(bmp,rect);}
}
'@
}

$rasterFiles = [PaintedPrismGaugeExtraction]::Run($rasterAtlas, $ProjectRoot, $rasterDirectory)
$rasterAssets = foreach ($file in $rasterFiles) {
    $bitmap = [System.Drawing.Bitmap]::FromFile($file)
    $clear = 0; $solid = 0
    for ($y=0; $y -lt $bitmap.Height; $y++) { for ($x=0; $x -lt $bitmap.Width; $x++) {
        $alpha = $bitmap.GetPixel($x,$y).A
        if ($alpha -eq 0) { $clear++ }; if ($alpha -eq 255) { $solid++ }
    } }
    [ordered]@{ path=$file.Substring($ProjectRoot.Length+1).Replace('\','/'); width=$bitmap.Width; height=$bitmap.Height;
        transparentPixels=$clear; solidPixels=$solid; centerAlpha=$bitmap.GetPixel([int]($bitmap.Width/2),[int]($bitmap.Height/2)).A;
        sha256=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() }
    $bitmap.Dispose()
}
$rasterReport = [ordered]@{ generatedUtc=[DateTime]::UtcNow.ToString('o'); source=$rasterAtlas;
    sourceSha256=(Get-FileHash -LiteralPath $rasterAtlas -Algorithm SHA256).Hash.ToLowerInvariant();
    mode='Deterministic downstream crop/affine ring extraction from built-in image_gen transparent atlas; no new image generation';
    canvasAndInnerBounds='Existing canonical dimensions and polygons preserved'; metadata='Existing .meta files not modified';
    verification=@('PASS: four track/fill/mask groups have identical alpha','PASS: all full left and right pointed tips are filled','PASS: four frame centers are transparent','PASS: all canvas corners and boss-gem outside corners are transparent');
    layers='Painted metal frames with transparent holes; fixed full-width painted fills and tracks; white alpha masks; boss spears; critical state recolor';
    assets=@($rasterAssets) }
[IO.File]::WriteAllText((Join-Path $rasterDirectory 'gauge-raster-report.json'),($rasterReport | ConvertTo-Json -Depth 8),(New-Object Text.UTF8Encoding $false))
Write-Output ('Recovered ' + $rasterFiles.Count + ' painted gauge layers; source/backup/preview/report: ' + $rasterDirectory)
