$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$source=[Drawing.Image]::FromFile((Join-Path $PSScriptRoot 'woodland.png'))
$bitmap=[Drawing.Bitmap]::new(1280,720)
$graphics=[Drawing.Graphics]::FromImage($bitmap)
try{
 $graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
 $graphics.DrawImage($source,0,0,1280,720)
 $codec=[Drawing.Imaging.ImageCodecInfo]::GetImageEncoders()|Where-Object MimeType -eq 'image/jpeg'
 $parameters=[Drawing.Imaging.EncoderParameters]::new(1)
 $parameters.Param[0]=[Drawing.Imaging.EncoderParameter]::new([Drawing.Imaging.Encoder]::Quality,[long]72)
 $bitmap.Save((Join-Path $PSScriptRoot 'inline-background.jpg'),$codec,$parameters)
}finally{$graphics.Dispose();$bitmap.Dispose();$source.Dispose()}
node (Join-Path $PSScriptRoot 'publish-inline.cjs')
