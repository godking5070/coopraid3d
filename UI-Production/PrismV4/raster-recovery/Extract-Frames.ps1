param(
    [string]$AtlasPath = '',
    [string]$ProjectRoot = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) { $ProjectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..')) }
$recoveryRoot = Join-Path $ProjectRoot 'UI-Production/PrismV4/raster-recovery'
New-Item -ItemType Directory -Path $recoveryRoot -Force | Out-Null
$atlasCopy = Join-Path $recoveryRoot 'painted-transparent-atlas.png'
if ([string]::IsNullOrWhiteSpace($AtlasPath)) { $AtlasPath = $atlasCopy }
if (-not (Test-Path -LiteralPath $AtlasPath)) { throw 'The canonical painted atlas is missing; provide -AtlasPath explicitly.' }
if (Test-Path -LiteralPath $atlasCopy) {
    if ((Get-FileHash -LiteralPath $atlasCopy).Hash -ne (Get-FileHash -LiteralPath $AtlasPath).Hash) {
        throw 'The shared painted atlas differs; refusing to replace it.'
    }
} else { Copy-Item -LiteralPath $AtlasPath -Destination $atlasCopy }

$extractSource = @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

public static class PrismPaintedFrameExtraction
{
    static Bitmap atlas;
    static string spriteRoot, recovery;
    static List<string> saved = new List<string>();
    static PointF[] P(params float[] values) {
        PointF[] result = new PointF[values.Length / 2];
        for(int i=0;i<result.Length;i++) result[i]=new PointF(values[i*2],values[i*2+1]);
        return result;
    }
    static Bitmap Empty(int w,int h) { return new Bitmap(w,h,PixelFormat.Format32bppArgb); }
    static void Setup(Graphics graphics) {
        graphics.CompositingMode=CompositingMode.SourceCopy;
        graphics.CompositingQuality=CompositingQuality.HighQuality;
        graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
        graphics.SmoothingMode=SmoothingMode.AntiAlias;
    }
    static Bitmap Crop(Rectangle source,int w,int h,Rectangle destination) {
        Bitmap result=Empty(w,h);
        using(Graphics graphics=Graphics.FromImage(result))
        using(ImageAttributes attributes=new ImageAttributes()) {
            Setup(graphics); attributes.SetWrapMode(WrapMode.TileFlipXY);
            graphics.DrawImage(atlas,destination,source.X,source.Y,source.Width,source.Height,GraphicsUnit.Pixel,attributes);
        }
        return result;
    }
    static Bitmap Mask(int w,int h,PointF[] outer,PointF[] inner) {
        Bitmap result=Empty(w,h);
        using(Graphics graphics=Graphics.FromImage(result))
        using(SolidBrush white=new SolidBrush(Color.White))
        using(SolidBrush clear=new SolidBrush(Color.Transparent)) {
            Setup(graphics); graphics.FillPolygon(white,outer);
            if(inner!=null) graphics.FillPolygon(clear,inner);
        }
        return result;
    }
    static void ApplyMask(Bitmap result,Bitmap mask,bool cleanFringes) {
        Rectangle bounds=new Rectangle(0,0,result.Width,result.Height);
        BitmapData data=result.LockBits(bounds,ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
        BitmapData maskData=mask.LockBits(bounds,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        byte[] bytes=new byte[data.Stride*result.Height],alpha=new byte[maskData.Stride*mask.Height];
        Marshal.Copy(data.Scan0,bytes,0,bytes.Length); Marshal.Copy(maskData.Scan0,alpha,0,alpha.Length);
        for(int y=0;y<result.Height;y++) for(int x=0;x<result.Width;x++) {
            int i=y*data.Stride+x*4, m=y*maskData.Stride+x*4;
            int a=bytes[i+3]*alpha[m+3]/255;
            int b=bytes[i],g=bytes[i+1],r=bytes[i+2];
            if(cleanFringes && ((g>150&&g>r*1.5&&g>b*1.3)||(r>210&&b>180&&g<75))) a=0;
            bytes[i+3]=(byte)a;
            if(a==0) { bytes[i]=0;bytes[i+1]=0;bytes[i+2]=0; }
        }
        Marshal.Copy(bytes,0,data.Scan0,bytes.Length);
        mask.UnlockBits(maskData); result.UnlockBits(data);
    }
    static void Shape(Bitmap bitmap,PointF[] outer,PointF[] inner) {
        using(Bitmap mask=Mask(bitmap.Width,bitmap.Height,outer,inner)) ApplyMask(bitmap,mask,true);
    }
    static void Save(Bitmap bitmap,string relative) {
        if(!(relative.StartsWith("Skills/")||relative.StartsWith("Hub/")||relative.StartsWith("Party/role-")))
            throw new InvalidOperationException("Extraction target is outside the owned component set.");
        string file=Path.Combine(spriteRoot,relative.Replace('/',Path.DirectorySeparatorChar));
        string backup=Path.Combine(recovery,"previous-structural",relative.Replace('/',Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        if(File.Exists(file)&&!File.Exists(backup)) { Directory.CreateDirectory(Path.GetDirectoryName(backup)); File.Copy(file,backup); }
        bitmap.Save(file,ImageFormat.Png); saved.Add(relative);
    }
    static Bitmap Tint(Bitmap source,float brightness,float redBias,float blueBias,float saturation,float opacity) {
        Bitmap result=Empty(source.Width,source.Height);
        float inv=1-saturation;
        ColorMatrix matrix=new ColorMatrix(new float[][] {
            new float[]{brightness*(saturation+.2126f*inv),brightness*.2126f*inv,brightness*.2126f*inv,0,0},
            new float[]{brightness*.7152f*inv,brightness*(saturation+.7152f*inv),brightness*.7152f*inv,0,0},
            new float[]{brightness*.0722f*inv,brightness*.0722f*inv,brightness*(saturation+.0722f*inv),0,0},
            new float[]{0,0,0,opacity,0},new float[]{redBias,0,blueBias,0,1}
        });
        using(Graphics graphics=Graphics.FromImage(result))
        using(ImageAttributes attributes=new ImageAttributes()) {
            Setup(graphics); attributes.SetColorMatrix(matrix);
            graphics.DrawImage(source,new Rectangle(0,0,result.Width,result.Height),0,0,source.Width,source.Height,GraphicsUnit.Pixel,attributes);
        }
        return result;
    }
    static void SkillParts() {
        PointF[] outer=P(28,8,164,8,184,28,184,164,164,184,28,184,8,164,8,28);
        PointF[] inner=P(32,20,160,20,172,32,172,160,160,172,32,172,20,160,20,32);
        using(Bitmap frame=Crop(new Rectangle(233,710,145,142),192,192,new Rectangle(8,8,176,176))) {
            // The blank key badge occludes this short upper rim. Continue the
            // adjacent painted rim pixels, preserving its actual bevel material.
            using(Graphics graphics=Graphics.FromImage(frame)) {
                Setup(graphics);
                graphics.DrawImage(atlas,new Rectangle(63,8,73,15),new Rectangle(338,710,20,12),GraphicsUnit.Pixel);
            }
            Shape(frame,outer,inner); Save(frame,"Skills/slot-frame-normal.png");
            using(Bitmap hover=Tint(frame,1.07f,.006f,.014f,1,1)) Save(hover,"Skills/slot-frame-hover.png");
            using(Bitmap pressed=Tint(frame,.78f,0,.006f,.9f,1)) Save(pressed,"Skills/slot-frame-pressed.png");
            using(Bitmap focused=Tint(frame,1.04f,.018f,.012f,1,1)) Save(focused,"Skills/slot-frame-focused.png");
            using(Bitmap disabled=Tint(frame,.67f,0,.012f,.18f,1)) Save(disabled,"Skills/slot-frame-disabled.png");
        }
        using(Bitmap background=Crop(new Rectangle(246,738,116,100),192,192,new Rectangle(20,20,152,152))) {
            Shape(background,inner,null); Save(background,"Skills/slot-background.png");
        }
        using(Bitmap mask=Mask(192,192,inner,null)) Save(mask,"Skills/slot-inner-mask.png");
        foreach(int width in new int[]{72,112}) {
            PointF[] edge=P(11,2,width-11,2,width-3,10,width-3,38,width-11,46,11,46,3,38,3,10);
            using(Bitmap badge=Crop(new Rectangle(278,694,57,37),width,48,new Rectangle(3,2,width-6,44))) {
                Shape(badge,edge,null);Save(badge,width==72?"Skills/key-badge.png":"Skills/key-badge-wide.png");
            }
        }
    }
    static void HubParts() {
        PointF[] outer=P(100,8,284,8,376,100,376,284,284,376,100,376,8,284,8,100);
        PointF[] inner=P(120,37,268,37,347,117,347,274,268,350,118,350,35,274,35,117);
        using(Bitmap frame=Crop(new Rectangle(698,608,277,278),384,384,new Rectangle(8,8,368,368))) {
            // The separate V badge covers the upper center of this same rim.
            using(Graphics graphics=Graphics.FromImage(frame)) {
                Setup(graphics);
                graphics.DrawImage(atlas,new Rectangle(134,8,116,30),new Rectangle(880,608,18,24),GraphicsUnit.Pixel);
            }
            Shape(frame,outer,inner);Save(frame,"Hub/hub-frame.png");
        }
        // The crystal hides the original center backplate. Extend clean painted
        // backplate pixels from its left flank; no new illustration is invented.
        using(Bitmap panel=Crop(new Rectangle(724,708,40,91),384,384,new Rectangle(35,37,312,313))) {
            Shape(panel,inner,null);Save(panel,"Hub/hub-background.png");
        }
        PointF[] crystal=P(192,42,282,192,192,344,102,192);
        using(Bitmap art=Crop(new Rectangle(772,640,129,219),384,384,new Rectangle(102,42,180,302))) {
            Shape(art,crystal,null);Save(art,"Hub/hub-crystal.png");
        }
        using(Bitmap mask=Mask(384,384,inner,null)) Save(mask,"Hub/hub-inner-mask.png");
        PointF[] badgeEdge=P(24,8,104,8,120,24,120,56,104,72,24,72,8,56,8,24);
        using(Bitmap badge=Crop(new Rectangle(797,586,80,53),128,80,new Rectangle(8,8,112,64))) {
            Shape(badge,badgeEdge,null);Save(badge,"Hub/ultimate-badge.png");
        }
    }
    static void RoleParts() {
        PointF[] outer=P(18,6,78,6,90,18,90,78,78,90,18,90,6,78,6,18);
        PointF[] inner=P(22,14,74,14,82,22,82,74,74,82,22,82,14,74,14,22);
        using(Bitmap frame=Crop(new Rectangle(48,84,72,69),96,96,new Rectangle(6,6,84,84))) {
            Shape(frame,outer,inner);Save(frame,"Party/role-frame.png");
        }
        using(Bitmap panel=Crop(new Rectangle(59,102,47,37),96,96,new Rectangle(14,14,68,68))) {
            Shape(panel,inner,null);Save(panel,"Party/role-background.png");
        }
        using(Bitmap mask=Mask(96,96,inner,null))Save(mask,"Party/role-inner-mask.png");
    }
    static void ContactPreview() {
        int columns=5,tile=238,height=((saved.Count+columns-1)/columns)*236+52;
        using(Bitmap preview=Empty(columns*tile,height))
        using(Graphics graphics=Graphics.FromImage(preview))
        using(Font font=new Font("Segoe UI",11))
        using(SolidBrush label=new SolidBrush(Color.FromArgb(226,222,237))) {
            graphics.Clear(Color.FromArgb(18,20,39));Setup(graphics);graphics.CompositingMode=CompositingMode.SourceOver;
            graphics.DrawString("PAINTED ATLAS / extracted material / no icons or baked text",font,label,12,12);
            for(int i=0;i<saved.Count;i++) {
                int x=(i%columns)*tile+12,y=(i/columns)*236+48;
                using(Bitmap image=new Bitmap(Path.Combine(spriteRoot,saved[i].Replace('/',Path.DirectorySeparatorChar)))) {
                    float scale=Math.Min(208f/image.Width,184f/image.Height);
                    graphics.DrawImage(image,new RectangleF(x+(208-image.Width*scale)/2,y,image.Width*scale,image.Height*scale));
                }
                graphics.DrawString(saved[i],font,label,x,y+194);
            }
            preview.Save(Path.Combine(recovery,"frames-contact-preview.png"),ImageFormat.Png);
        }
        using(Bitmap assembly=Empty(720,430))
        using(Graphics graphics=Graphics.FromImage(assembly)) {
            graphics.Clear(Color.FromArgb(16,18,35));Setup(graphics);graphics.CompositingMode=CompositingMode.SourceOver;
            foreach(string name in new string[]{"Hub/hub-background.png","Hub/hub-crystal.png","Hub/hub-frame.png"}) {
                using(Bitmap image=new Bitmap(Path.Combine(spriteRoot,name.Replace('/',Path.DirectorySeparatorChar))))graphics.DrawImage(image,new Rectangle(310,24,384,384));
            }
            foreach(string name in new string[]{"Skills/slot-background.png","Skills/slot-frame-normal.png"}) {
                using(Bitmap image=new Bitmap(Path.Combine(spriteRoot,name.Replace('/',Path.DirectorySeparatorChar))))graphics.DrawImage(image,new Rectangle(72,128,192,192));
            }
            using(Bitmap image=new Bitmap(Path.Combine(spriteRoot,"Hub/ultimate-badge.png")))graphics.DrawImage(image,new Rectangle(438,0,128,80));
            using(Bitmap image=new Bitmap(Path.Combine(spriteRoot,"Skills/key-badge.png")))graphics.DrawImage(image,new Rectangle(132,112,72,48));
            assembly.Save(Path.Combine(recovery,"frames-assembled-preview.png"),ImageFormat.Png);
        }
    }
    public static string[] Run(string source,string project,string output) {
        spriteRoot=Path.Combine(project,"Assets/UI/AstellaPrismV4/Sprites");recovery=output;saved.Clear();
        using(Bitmap loaded=new Bitmap(source)) {
            atlas=new Bitmap(loaded.Width,loaded.Height,PixelFormat.Format32bppArgb);
            using(Graphics graphics=Graphics.FromImage(atlas)){Setup(graphics);graphics.DrawImageUnscaled(loaded,0,0);}
        }
        try { SkillParts();HubParts();RoleParts();ContactPreview();return saved.ToArray(); }
        finally { atlas.Dispose(); }
    }
}
'@
if (-not ('PrismPaintedFrameExtraction' -as [type])) {
    Add-Type -TypeDefinition $extractSource -ReferencedAssemblies System.Drawing
}
$updatedFiles = [PrismPaintedFrameExtraction]::Run($atlasCopy, $ProjectRoot, $recoveryRoot)
$extractionReport = [ordered]@{
    mode = 'Deterministic mechanical extraction after built-in image_gen transparent atlas generation'
    source = 'painted-transparent-atlas.png'
    sourceSha256 = (Get-FileHash -LiteralPath $atlasCopy).Hash.ToLowerInvariant()
    textBakedIntoAssets = $false
    iconsIncluded = $false
    updatedSprites = $updatedFiles
    slotInnerPolygon = @(@(32,20),@(160,20),@(172,32),@(172,160),@(160,172),@(32,172),@(20,160),@(20,32))
    roleInnerPolygon = @(@(22,14),@(74,14),@(82,22),@(82,74),@(74,82),@(22,82),@(14,74),@(14,22))
    hubInnerPolygon = @(@(120,37),@(268,37),@(347,117),@(347,274),@(268,350),@(118,350),@(35,274),@(35,117))
    notes = @('Badge-occluded upper frame segments continue neighboring painted rim pixels.', 'Occluded center backplate extends clean painted side-panel pixels.', 'Existing Unity metadata and native canvas sizes are preserved.')
}
$extractionReport | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $recoveryRoot 'frames-extraction.json') -Encoding UTF8
Write-Output ('Updated painted frame/hub/empty-role sprites: ' + $updatedFiles.Count)
Write-Output (Join-Path $recoveryRoot 'frames-contact-preview.png')
Write-Output (Join-Path $recoveryRoot 'frames-assembled-preview.png')
