$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$package=Join-Path $workspace 'TripoReady\Stage2_v1'
$reviewDir=Join-Path $PSScriptRoot 'stage2-visual'
New-Item -ItemType Directory -Force -Path $reviewDir | Out-Null
function New-Review($groups,$filename) {
  $rows=$groups.Count
  $canvas=New-Object Drawing.Bitmap(1600,($rows*400))
  $g=[Drawing.Graphics]::FromImage($canvas)
  $g.Clear([Drawing.Color]::FromArgb(232,232,232))
  $g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $font=New-Object Drawing.Font('Arial',13)
  for($row=0;$row -lt $rows;$row++) {
    $col=0
    foreach($view in @('front','left','right','back')) {
      $img=[Drawing.Image]::FromFile((Join-Path $package ($groups[$row]+'\'+$view+'.png')))
      $scale=[Math]::Min(390.0/$img.Width,365.0/$img.Height)
      $w=[int]($img.Width*$scale); $h=[int]($img.Height*$scale)
      $rect=New-Object Drawing.Rectangle(($col*400+[int]((400-$w)/2)),($row*400+30+[int]((365-$h)/2)),$w,$h)
      $g.DrawImage($img,$rect)
      $g.DrawString(($groups[$row]+' / '+$view),$font,[Drawing.Brushes]::Black,($col*400+8),($row*400+5))
      $img.Dispose(); $col++
    }
  }
  $font.Dispose(); $g.Dispose()
  $canvas.Save((Join-Path $reviewDir $filename),[Drawing.Imaging.ImageFormat]::Png)
  $canvas.Dispose()
}
New-Review @('Bodies\Normal','Bodies\Hard') 'bodies.png'
New-Review @('Buildings\EntranceBay','Buildings\BalconyStairs','Buildings\VentCabinet') 'buildings.png'
Write-Output 'Created two review contact sheets outside the delivery package.'


