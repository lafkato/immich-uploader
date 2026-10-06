param()
# Rebuild the installer artwork from the existing application icon and drawn layout.
Add-Type -AssemblyName System.Drawing
$brandingPath = Join-Path $PSScriptRoot 'branding'
[System.IO.Directory]::CreateDirectory($brandingPath) | Out-Null
$appIconPath = Join-Path $PSScriptRoot '..\src\ImmichUploaderApp\Resources\icon.ico'
$appIcon = [System.Drawing.Icon]::new([System.IO.Path]::GetFullPath($appIconPath))
$sidebar = [System.Drawing.Bitmap]::new(656, 1256)
$canvas = [System.Drawing.Graphics]::FromImage($sidebar)
$canvas.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$canvas.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$background = [System.Drawing.Drawing2D.LinearGradientBrush]::new([System.Drawing.Rectangle]::new(0,0,656,1256), [System.Drawing.Color]::FromArgb(26,39,61), [System.Drawing.Color]::FromArgb(13,19,31), 90.0)
$canvas.FillRectangle($background, 0, 0, 656, 1256)
$white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(234,240,250))
$muted = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(148,164,189))
$blue = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(139,167,255))
$brandFont = [System.Drawing.Font]::new('Segoe UI', 70, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$captionFont = [System.Drawing.Font]::new('Segoe UI', 34, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$canvas.DrawString('immich', $brandFont, $white, 65, 85)
$canvas.DrawString('UPLOADER', $captionFont, $muted, 72, 180)
$canvas.FillRectangle($blue, 72, 253, 94, 6)
$canvas.DrawIcon($appIcon, [System.Drawing.Rectangle]::new(155, 365, 346, 340))
$line = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(42,58,83), 4)
$accentLine = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(139,167,255), 7)
$mintLine = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(85,211,181), 7)
# A restrained two-direction transfer motif uses the same accents as the desktop dashboard.
$canvas.DrawRectangle($line, 80, 898, 155, 155)
$canvas.DrawRectangle($line, 423, 898, 155, 155)
$canvas.DrawLine($line, 90, 1043, 142, 975)
$canvas.DrawLine($line, 142, 975, 175, 1007)
$canvas.DrawLine($line, 175, 1007, 223, 960)
$canvas.DrawEllipse($line, 188, 916, 20, 20)
$canvas.DrawLine($accentLine, 267, 944, 385, 944)
$canvas.DrawLine($accentLine, 365, 924, 385, 944)
$canvas.DrawLine($accentLine, 365, 964, 385, 944)
$canvas.DrawLine($mintLine, 267, 1002, 385, 1002)
$canvas.DrawLine($mintLine, 267, 1002, 287, 982)
$canvas.DrawLine($mintLine, 267, 1002, 287, 1022)
$canvas.DrawRectangle($line, 452, 926, 96, 61)
$canvas.DrawLine($line, 499, 987, 499, 1025)
$canvas.DrawLine($line, 473, 1025, 525, 1025)
$canvas.DrawString('WINDOWS', $captionFont, $muted, 72, 1140)
$sidebar.Save((Join-Path $brandingPath 'wizard-sidebar.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$small = [System.Drawing.Bitmap]::new(220, 220)
$smallCanvas = [System.Drawing.Graphics]::FromImage($small)
$smallCanvas.Clear([System.Drawing.Color]::Transparent)
$smallCanvas.DrawIcon($appIcon, [System.Drawing.Rectangle]::new(14,16,192,189))
$small.Save((Join-Path $brandingPath 'wizard-icon.png'), [System.Drawing.Imaging.ImageFormat]::Png)
foreach ($resource in @($smallCanvas,$small,$background,$white,$muted,$blue,$brandFont,$captionFont,$line,$accentLine,$mintLine,$canvas,$sidebar,$appIcon)) { $resource.Dispose() }
