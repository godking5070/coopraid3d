$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$package=Join-Path $workspace 'TripoReady\Stage3_v1'
$out=Join-Path $PSScriptRoot 'Review'
New-Item -ItemType Directory -Force -Path $out|Out-Null
function Review($groups,$file) {
 $canvas=New-Object Drawing.Bitmap(1600,($groups.Count*400))
 $g=[Drawing.Graphics]::FromImage($canvas);$g.Clear([Drawing.Color]::FromArgb(232,232,232))
 $g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
 $font=New-Object Drawing.Font('Arial',12)
 for($row=0;$row -lt $groups.Count;$row++){
  $col=0
  foreach($view in @('front','left','right','back')){
   $img=[Drawing.Image]::FromFile((Join-Path $package ($groups[$row]+'\'+$view+'.png')))
   $s=[Math]::Min(390.0/$img.Width,365.0/$img.Height);$w=[int]($img.Width*$s);$h=[int]($img.Height*$s)
   $r=New-Object Drawing.Rectangle(($col*400+[int]((400-$w)/2)),($row*400+30+[int]((365-$h)/2)),$w,$h)
   $g.DrawImage($img,$r);$g.DrawString(($groups[$row]+' / '+$view),$font,[Drawing.Brushes]::Black,($col*400+5),($row*400+5))
   $img.Dispose();$col++
  }
 }
 $font.Dispose();$g.Dispose();$canvas.Save((Join-Path $out $file),[Drawing.Imaging.ImageFormat]::Png);$canvas.Dispose()
}
Review @('Bodies\Normal','Bodies\Hard') 'bodies.png'
Review @('Buildings\BelfryArch','Buildings\Railing','Buildings\WindingStation') 'buildings.png'
$groups=@('cloak-normal','cloak-hard','clapper','drive')
$canvas=New-Object Drawing.Bitmap(1600,1600);$g=[Drawing.Graphics]::FromImage($canvas);$g.Clear([Drawing.Color]::FromArgb(232,232,232));$g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic;$font=New-Object Drawing.Font('Arial',12)
for($row=0;$row -lt 4;$row++){ $col=0;foreach($view in @('front','left','right','back')){
 $img=[Drawing.Image]::FromFile((Join-Path $PSScriptRoot ('Sources\'+$groups[$row]+'-'+$view+'.png')))
 $g.DrawImage($img,($col*400+10),($row*400+30),380,365);$g.DrawString(($groups[$row]+' / '+$view),$font,[Drawing.Brushes]::Black,($col*400+5),($row*400+5));$img.Dispose();$col++
}}
$font.Dispose();$g.Dispose();$canvas.Save((Join-Path $out 'accessory-sources.png'),[Drawing.Imaging.ImageFormat]::Png);$canvas.Dispose()

