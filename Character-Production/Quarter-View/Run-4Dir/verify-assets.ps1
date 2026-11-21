$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$manifestPath = Join-Path $PSScriptRoot 'roster.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.characters.Count -ne 12 -or ($manifest.directionRows -join ',') -ne 's,e,n,w' -or
    ($manifest.frameSizePx -join ',') -ne '128,160' -or $manifest.framesPerDirection -ne 8) {
    throw 'Unexpected roster, atlas grid, or direction order.'
}
$audit = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'pixel-audit.json') -Raw -Encoding UTF8 | ConvertFrom-Json

$inspect = @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

public static class AstellaRunAssets
{
    private const int W = 128, H = 160, Columns = 8, Rows = 4;

    public static void Check(string atlas, string preview, int[] duration)
    {
        CheckAtlas(atlas);
        using (var gif = Image.FromFile(preview))
        {
            if (gif.GetFrameCount(FrameDimension.Time) != Columns ||
                BitConverter.ToUInt16(gif.GetPropertyItem(0x5101).Value, 0) != 0)
                throw new InvalidDataException(preview + ": expected eight frames and infinite repeat.");
            byte[] ticks = gif.GetPropertyItem(0x5100).Value;
            for (int frame = 0; frame < Columns; frame++)
            {
                int milliseconds = BitConverter.ToInt32(ticks, 4 * frame) * 10;
                if (milliseconds != duration[frame])
                    throw new InvalidDataException(preview + ": timing in frame " + (frame + 1) + " differs.");
            }
        }
    }

    private static void CheckAtlas(string file)
    {
        using (var bitmap = new Bitmap(file))
        {
            if (bitmap.Width != W * Columns || bitmap.Height != H * Rows)
                throw new InvalidDataException(file + ": wrong atlas dimensions.");
            var pixels = new int[bitmap.Width * bitmap.Height];
            var bits = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < bitmap.Height; y++)
                    Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), pixels, y * bitmap.Width, bitmap.Width);
            }
            finally { bitmap.UnlockBits(bits); }

            for (int row = 0; row < Rows; row++)
            {
                var distinct = new HashSet<ulong>();
                for (int frame = 0; frame < Columns; frame++)
                {
                    string cell = file + " row " + (row + 1) + ", frame " + (frame + 1);
                    var ink = new bool[W * H];
                    int count = 0, first = -1;
                    ulong hash = 14695981039346656037UL;
                    for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int value = pixels[(row * H + y) * bitmap.Width + frame * W + x];
                        int alpha = (int)((uint)value >> 24);
                        int offset = y * W + x;
                        hash = unchecked((hash ^ ((uint)value)) * 1099511628211UL);
                        if (alpha == 0) continue;
                        if (alpha != 255 || x < 7 || x >= 121 || y < 22 || y >= 147)
                            throw new InvalidDataException(cell + ": partial alpha or clipped preview.");
                        ink[offset] = true;
                        if (first < 0) first = offset;
                        count++;
                    }

                    if (count < 1900 || !distinct.Add(hash) || Flood(ink,first) != count)
                        throw new InvalidDataException(cell + ": incomplete, duplicated or fragmented silhouette.");
                }
            }
        }
    }

    private static int Flood(bool[] ink, int first)
    {
        var queue = new Queue<int>();
        ink[first] = false;
        queue.Enqueue(first);
        int reached = 0;
        while (queue.Count != 0)
        {
            int offset = queue.Dequeue();
            int x = offset % W, y = offset / W;
            reached++;
            for (int yy = Math.Max(0,y - 1); yy <= Math.Min(H - 1,y + 1); yy++)
            for (int xx = Math.Max(0,x - 1); xx <= Math.Min(W - 1,x + 1); xx++)
            {
                int next = yy * W + xx;
                if (!ink[next]) continue;
                ink[next] = false;
                queue.Enqueue(next);
            }
        }
        return reached;
    }
}
'@
Add-Type -TypeDefinition $inspect -ReferencedAssemblies System.Drawing,System.Core
$unique = [Collections.Generic.HashSet[string]]::new()
foreach ($item in $manifest.characters) {
    $id = $item.id
    if ($id -notmatch '^[a-z]+(-[a-z]+)*$' -or !$unique.Add($id) -or
        $item.durationsMs.Count -ne 8 -or @($item.durationsMs | Where-Object {$_ -lt 10 -or $_ % 10 -ne 0}).Count) {
        throw "Invalid sprite id or timeline for $id."
    }
    $result = @($audit | Where-Object id -eq $id)
    if ($result.Count -ne 1 -or $result[0].sha256 -ne $item.sha256.atlas -or $result[0].checkedCels -ne 32 -or
        @($result[0].directions | Where-Object comparedTransitions -eq 8).Count -ne 4) {
        throw "Frame transition audit missing for $id."
    }
    $feet = @($result[0].directions | Where-Object {
        $_.minimumShinBootOverlapPx -ge 13 -and $_.minimumForefootPx -ge 25 -and
        $_.maxContactErrorPx -le 1 -and $_.contactSamples -eq 6 -and $_.maxPitchChangeDegrees -le 16 -and
        $_.minimumVisibleNearBootPx -gt 0
    })
    if ($feet.Count -ne 4) { throw "Incomplete ankle, forefoot or ground contact audit for $id." }
    $files = @{
        atlas = "$id/$($id)_Run_4Dir.png"
        frames = "$id/$($id)_Run_4Dir_Frames.png"
        preview = "$id/$($id)_Run_4Dir_Preview.gif"
    }
    foreach ($kind in $files.Keys) {
        if ($item.files.$kind -ne $files[$kind]) { throw "Invalid $kind path for $id." }
        $asset = Join-Path $PSScriptRoot $files[$kind]
        $hash = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash
        if ($hash -ne $item.sha256.$kind) { throw "Changed $kind file for $id." }
    }
    [AstellaRunAssets]::Check((Join-Path $PSScriptRoot $files.atlas),
        (Join-Path $PSScriptRoot $files.preview),[int[]]$item.durationsMs)
    Write-Output "$id passed 32 cels and 8 GIF frames"
}
Write-Output 'Verified 384 cels, their 768 boot joint measurements, file hashes, and 12 GIF timelines.'
