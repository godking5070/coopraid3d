param(
    [string]$Clip = 'run',
    [string]$Direction = 'e',
    [string]$SourceDirectory = $PSScriptRoot,
    [string]$BaselineAtlas,
    [string]$OutputDirectory = (Join-Path ([IO.Path]::GetTempPath()) 'IrisLocomotionReview'),
    [ValidateRange(1, 5)]
    [int]$Zoom = 3
)

$ErrorActionPreference = 'Stop'
$manifest = Get-Content -LiteralPath (Join-Path $SourceDirectory 'motions.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$motion = $manifest.clips | Where-Object id -eq $Clip
if (!$motion) { throw "Unknown clip: $Clip" }
$row = $motion.directions | Where-Object id -eq $Direction
if (!$row) { throw "Unknown direction: $Direction" }
$width, $height = $manifest.frameSizePx
$count = $motion.atlas.columns
if ($row.frames.Count -ne $count -or $count -lt 2 -or $motion.frameDurationMs -le 0 -or
    $motion.durationMs -ne $motion.frameDurationMs * $count) {
    throw 'Frame count or duration does not match the atlas.'
}
$atlas = Join-Path $SourceDirectory $motion.atlas.file
if ((Get-FileHash -LiteralPath $atlas -Algorithm SHA256).Hash -ne $motion.atlas.sha256) {
    throw 'The sprite atlas hash does not match motions.json.'
}
for ($i = 0; $i -lt $count; $i++) {
    $rect = @(($i * $width), ($row.row * $height), $width, $height)
    if (($row.frames[$i].atlasRectPx -join ',') -ne ($rect -join ',')) {
        throw "Incorrect atlas cell for frame $($i + 1)."
    }
}

Add-Type -AssemblyName System.Drawing
$source = @"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Security.Cryptography;

public static class IrisLocomotionReview
{
    public static Rectangle Bounds(Bitmap atlas, Rectangle cell)
    {
        Rectangle content = Rectangle.Empty;
        for (int y = 0; y < cell.Height; y++)
        for (int x = 0; x < cell.Width; x++)
        {
            Color color = atlas.GetPixel(cell.X + x, cell.Y + y);
            if (color.A == 0) continue;
            if (color.A != 255) throw new InvalidOperationException("Partial alpha in cel " + cell);
            Rectangle pixel = new Rectangle(x, y, 1, 1);
            content = content.IsEmpty ? pixel : Rectangle.Union(content, pixel);
        }
        if (content.IsEmpty || content.Left == 0 || content.Top == 0 ||
            content.Right == cell.Width || content.Bottom == cell.Height)
            throw new InvalidOperationException("Empty or clipped cel " + cell);
        return content;
    }

    public static int ChangedPixels(Bitmap atlas, Rectangle a, Rectangle b)
    {
        int changes = 0;
        for (int y = 0; y < a.Height; y++)
        for (int x = 0; x < a.Width; x++)
        {
            Color first = atlas.GetPixel(a.X + x, a.Y + y);
            Color second = atlas.GetPixel(b.X + x, b.Y + y);
            if ((first.A != 0 || second.A != 0) && first.ToArgb() != second.ToArgb()) changes++;
        }
        return changes;
    }

    public static string ExpressionHash(Bitmap atlas, Rectangle cell, Rectangle expression)
    {
        if (!new Rectangle(Point.Empty, cell.Size).Contains(expression))
            throw new InvalidOperationException("Expression crop is outside its cel.");
        var bytes = new List<byte>();
        for (int y = expression.Top; y < expression.Bottom; y++)
        for (int x = expression.Left; x < expression.Right; x++)
            bytes.AddRange(BitConverter.GetBytes(atlas.GetPixel(cell.X + x, cell.Y + y).ToArgb()));
        using (var hash = SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(bytes.ToArray())).Replace("-", "");
    }

    public static int FragmentPixels(Bitmap atlas, Rectangle cell)
    {
        int width = cell.Width, height = cell.Height;
        var remaining = new bool[width * height];
        int total = 0, main = 0;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            if (atlas.GetPixel(cell.X + x, cell.Y + y).A == 0) continue;
            remaining[y * width + x] = true;
            total++;
        }
        var queue = new Queue<int>();
        for (int pixel = 0; pixel < remaining.Length; pixel++)
        {
            if (!remaining[pixel]) continue;
            queue.Enqueue(pixel);
            remaining[pixel] = false;
            int size = 0;
            while (queue.Count > 0)
            {
                int current = queue.Dequeue(), x = current % width, y = current / width;
                size++;
                for (int yy = Math.Max(0, y - 1); yy <= Math.Min(height - 1, y + 1); yy++)
                for (int xx = Math.Max(0, x - 1); xx <= Math.Min(width - 1, x + 1); xx++)
                {
                    int next = yy * width + xx;
                    if (!remaining[next]) continue;
                    remaining[next] = false;
                    queue.Enqueue(next);
                }
            }
            main = Math.Max(main, size);
        }
        return total - main;
    }
    public static string ComparisonStrip(Bitmap atlas, Rectangle[] cels, Rectangle crop)
    {
        using (var strip = new Bitmap(cels.Length * crop.Width, crop.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(strip))
        using (var bytes = new System.IO.MemoryStream())
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            for (int i = 0; i < cels.Length; i++)
            {
                var from = new Rectangle(cels[i].X + crop.X, cels[i].Y + crop.Y, crop.Width, crop.Height);
                var to = new Rectangle(i * crop.Width, 0, crop.Width, crop.Height);
                g.DrawImage(atlas, to, from, GraphicsUnit.Pixel);
            }
            g.Flush();
            strip.Save(bytes, System.Drawing.Imaging.ImageFormat.Png);
            return "data:image/png;base64," + Convert.ToBase64String(bytes.ToArray());
        }
    }

    public static void PairSheet(Bitmap candidate, Bitmap baseline, Rectangle[] cels, Rectangle crop, int zoom, string output)
    {
        const int pad = 9, label = 21;
        int columns = Math.Min(cels.Length, 4);
        int rows = (cels.Length + columns - 1) / columns;
        int profileRows = baseline == null ? 1 : 2;
        int w = crop.Width * zoom;
        int h = crop.Height * zoom;
        int stepX = 2 * w + pad * 3;
        int stepY = h + label + pad;
        using (var sheet = new Bitmap(columns * stepX, stepY * rows * profileRows))
        using (var g = Graphics.FromImage(sheet))
        using (var surface = new SolidBrush(Color.FromArgb(53, 47, 61)))
        using (var font = new Font("Consolas", 10))
        {
            g.Clear(Color.FromArgb(35, 30, 41));
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            for (int pass = 0; pass < profileRows; pass++)
            for (int i = 0; i < cels.Length; i++)
            {
                int next = (i + 1) % cels.Length;
                int x = stepX * (i % columns) + pad;
                int y = stepY * (i / columns + pass * rows);
                string name = (pass == 0 ? "Current " : "Baseline ") + (i + 1) + " > " + (next + 1);
                g.DrawString(name, font, Brushes.White, x, y + 3);
                Bitmap art = pass == 0 ? candidate : baseline;
                foreach (int k in new[] { i, next })
                {
                    var box = new Rectangle(cels[k].X + crop.X, cels[k].Y + crop.Y, crop.Width, crop.Height);
                    var to = new Rectangle(x, y + label, w, h);
                    g.FillRectangle(surface, to);
                    g.DrawImage(art, to, box, GraphicsUnit.Pixel);
                    x += w + pad;
                }
            }
            sheet.Save(output, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
"@
if (!('IrisLocomotionReview' -as [type])) {
    Add-Type -ReferencedAssemblies System.Drawing,System.Core -TypeDefinition $source
}

$cels = [Drawing.Rectangle[]]::new($count)
$image = [Drawing.Bitmap]::new((Resolve-Path -LiteralPath $atlas).Path)
$baseline = $null
try {
    if ($image.Width -ne $width * $count -or $image.Height -ne $height * $motion.atlas.rows) {
        throw 'Atlas dimensions do not match the frame grid.'
    }
    if ($BaselineAtlas) {
        $baseline = [Drawing.Bitmap]::new((Resolve-Path -LiteralPath $BaselineAtlas).Path)
        if ($baseline.Size -ne $image.Size) { throw 'Baseline atlas uses a different grid.' }
    }
    $crop = [Drawing.Rectangle]::Empty
    $fragmentPixels = @()
    for ($i = 0; $i -lt $count; $i++) {
        $cell = [Drawing.Rectangle]::new($i * $width, $row.row * $height, $width, $height)
        $cels[$i] = $cell
        $bounds = [IrisLocomotionReview]::Bounds($image, $cell)
        $crop = if ($crop.IsEmpty) { $bounds } else { [Drawing.Rectangle]::Union($crop, $bounds) }
        if ($baseline) {
            $crop = [Drawing.Rectangle]::Union($crop, [IrisLocomotionReview]::Bounds($baseline, $cell))
        }
        $fragmentPixels += [IrisLocomotionReview]::FragmentPixels($image, $cell)
        if ($row.expressionLock) {
            $x, $y, $w, $h = $row.frames[$i].expressionRectPx
            if (!$w -or !$h) { throw "Missing expression crop in frame $($i + 1)." }
            $expression = [Drawing.Rectangle]::new($x, $y, $w, $h)
            $actual = [IrisLocomotionReview]::ExpressionHash($image, $cell, $expression)
            if ($actual -ne $row.expressionLock.sha256) { throw "Expression mismatch in frame $($i + 1)." }
        }
    }

    function Get-HandTravel($First, $Second, [string]$Side) {
        if (!$First.armPose -or !$Second.armPose) { return }
        $a = $First.armPose.$Side.hand
        $b = $Second.armPose.$Side.hand
        [Math]::Round([Math]::Sqrt([Math]::Pow($a[0] - $b[0], 2) + [Math]::Pow($a[1] - $b[1], 2)), 2)
    }

    $pairs = for ($i = 0; $i -lt $count; $i++) {
        $next = ($i + 1) % $count
        $pixels = [IrisLocomotionReview]::ChangedPixels($image, $cels[$i], $cels[$next])
        if (!$pixels) { throw "Repeated cels $($i + 1) and $($next + 1)." }
        $right = Get-HandTravel $row.frames[$i] $row.frames[$next] 'right'
        $left = Get-HandTravel $row.frames[$i] $row.frames[$next] 'left'
        if (($null -ne $right -and $right -eq 0) -or ($null -ne $left -and $left -eq 0)) {
            Write-Warning "A tracked hand stays in place from $($i + 1) to $($next + 1). Inspect the adjacent pair."
        }
        [PSCustomObject]@{
            Pair = "$($i + 1) > $($next + 1)"
            ChangedPixels = $pixels
            RightHandPx = $right
            LeftHandPx = $left
            FragmentPixels = $fragmentPixels[$i]
        }
    }
    foreach ($crossing in $row.legRig.swingPasses) {
        $other = if ($crossing.side -eq 'right') { 'left' } else { 'right' }
        $from = $row.frames[$crossing.fromFrame - 1].legPose
        $to = $row.frames[$crossing.toFrame - 1].legPose
        $before = $from.($crossing.side).ankle[0] - $from.$other.ankle[0]
        $after = $to.($crossing.side).ankle[0] - $to.$other.ankle[0]
        if ($before -ge 0 -or $after -le 0) { throw "Expected passing pose for $($crossing.side) is missing." }
    }

    $crop.Inflate(4, 4)
    $crop.Intersect([Drawing.Rectangle]::new(0, 0, $width, $height))
    $output = [IO.Directory]::CreateDirectory($OutputDirectory).FullName
    $board = Join-Path $output "$Clip-$Direction-adjacent.png"
    [IrisLocomotionReview]::PairSheet($image, $baseline, $cels, $crop, $Zoom, $board)
    if ($baseline) {
        $directionLabel = ($manifest.directions | Where-Object id -eq $Direction).label
        $contacts = @(
            foreach ($frame in $row.frames) {
                $left = $frame.legPose.left
                $right = $frame.legPose.right
                if (!$left -and !$right) { '' }
                elseif ($left.grounded -and $right.grounded) { 'dual' }
                elseif ($left.grounded) { "left-$($left.contact)" }
                elseif ($right.grounded) { "right-$($right.contact)" }
                else { 'air' }
            }
        )
        $data = @{
            title = "$($manifest.character) $directionLabel $($motion.label)"
            width = $crop.Width
            height = $crop.Height
            frameCount = $count
            frameDurationMs = $motion.frameDurationMs
            candidate = [IrisLocomotionReview]::ComparisonStrip($image, $cels, $crop)
            before = [IrisLocomotionReview]::ComparisonStrip($baseline, $cels, $crop)
            contact = $contacts
        }
        $template = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Review-Comparison.template.html') -Raw -Encoding UTF8
        $json = (ConvertTo-Json -InputObject $data -Depth 4 -Compress).Replace('<', '\u003c')
        $html = $template.Replace('__REVIEW_DATA__', $json)
        $comparison = Join-Path $output "$Clip-$Direction-comparison.html"
        $utf8 = [Text.UTF8Encoding]::new($false)
        [IO.File]::WriteAllText($comparison, $html, $utf8)
        "Comparison saved to $comparison"
        if ($env:ORCA_CHAT_SCRATCH_DIR -and
            (Test-Path -LiteralPath $env:ORCA_CHAT_SCRATCH_DIR -PathType Container) -and
            $utf8.GetByteCount($html) -lt 512KB) {
            $fileName = 'iris-comparison-' + [Guid]::NewGuid().ToString('N').Substring(0, 8) + '.html'
            try {
                Copy-Item -LiteralPath $comparison -Destination (Join-Path $env:ORCA_CHAT_SCRATCH_DIR $fileName)
                "Orca preview $fileName"
            } catch {
                Write-Warning "Open the comparison from $comparison"
            }
        }
    }
    $pairs | Format-Table -AutoSize
    "Adjacent frames: $board"
    "Playback: index.html?clip=$Clip&facing=$Direction ($($motion.frameDurationMs) ms per cel)"
}
finally {
    if ($baseline) { $baseline.Dispose() }
    $image.Dispose()
}