# Makes assets\stt.ico (red circle + white microphone, same drawing as Icons.Draw in src\Media.cs).
# Run once with Windows PowerShell: powershell -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing

function Draw-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $s = $size / 16.0
    $bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(214, 60, 60))
    $g.FillEllipse($bg, 0, 0, $size - 1, $size - 1)
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([Math]::Max(1.0, 1.3 * $s))
    $w = 4 * $s; $h = 7 * $s; $x = 6 * $s; $y = 2.5 * $s
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($x, $y, $w, $w, 180, 180)
    $path.AddArc($x, $y + $h - $w, $w, $w, 0, 180)
    $path.CloseFigure()
    $g.FillPath($white, $path)
    $g.DrawArc($pen, 4.5 * $s, 4.5 * $s, 7 * $s, 6.5 * $s, 0, 180)
    $g.DrawLine($pen, 8 * $s, 11 * $s, 8 * $s, 13 * $s)
    $g.DrawLine($pen, 6 * $s, 13.2 * $s, 10 * $s, 13.2 * $s)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 256
$images = @($sizes | ForEach-Object { ,(Draw-Icon $_) })
$out = Join-Path $PSScriptRoot '..\assets\stt.ico'
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $len = $images[$i].Length
    $b = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([byte]$b); $bw.Write([byte]$b); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Close()
Write-Host "wrote $out"
