# Regenerate the ICO using the same geometry as Assets/app.svg.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repo = Split-Path -Parent $PSScriptRoot
$target = Join-Path $repo 'src/SshManager.App/Assets/app.ico'
$frames = @()
foreach ($size in @(16, 20, 24, 32, 48, 64, 128, 256)) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $canvas = [System.Drawing.Graphics]::FromImage($bitmap)
    $canvas.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $canvas.Clear([System.Drawing.Color]::Transparent)
    $canvas.ScaleTransform(($size / 256.0), ($size / 256.0))
    $shape = New-Object System.Drawing.Drawing2D.GraphicsPath
    $shape.AddArc(8, 8, 104, 104, 180, 90)
    $shape.AddArc(144, 8, 104, 104, 270, 90)
    $shape.AddArc(144, 144, 104, 104, 0, 90)
    $shape.AddArc(8, 144, 104, 104, 90, 90)
    $shape.CloseFigure()
    $navy = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(20, 36, 61))
    $teal = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(56, 214, 197))
    $whitePen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 16)
    $tealPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(56, 214, 197), 15)
    foreach ($pen in @($whitePen, $tealPen)) {
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    }
    $canvas.FillPath($navy, $shape)
    $canvas.DrawLines($whitePen, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF(56,75)), (New-Object System.Drawing.PointF(86,100)), (New-Object System.Drawing.PointF(56,125))))
    $canvas.DrawLine($whitePen, 104, 125, 130, 125)
    $canvas.DrawLine($tealPen, 61, 166, 193, 166)
    $canvas.DrawLines($tealPen, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF(170,145)), (New-Object System.Drawing.PointF(193,166)), (New-Object System.Drawing.PointF(170,187))))
    $canvas.FillEllipse($teal, 176, 54, 32, 32)
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += @{ Size = $size; Data = $stream.ToArray() }
    foreach ($disposable in @($stream, $shape, $navy, $teal, $whitePen, $tealPen, $canvas, $bitmap)) { $disposable.Dispose() }
}
$output = [System.IO.File]::Create($target)
$writer = New-Object System.IO.BinaryWriter($output)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Data.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Data.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Data) }
} finally { $writer.Dispose(); $output.Dispose() }
Write-Output "Generated $target"
