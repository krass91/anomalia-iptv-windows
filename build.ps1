$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
Add-Type -AssemblyName System.Drawing
$image = New-Object System.Drawing.Bitmap 256,256
$g = [System.Drawing.Graphics]::FromImage($image)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::FromArgb(14,18,28))
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(77,211,179)),10
$g.DrawRectangle($pen,42,60,172,126)
$brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(77,211,179))
$g.FillPolygon($brush,[System.Drawing.Point[]]@((New-Object System.Drawing.Point 106,88),(New-Object System.Drawing.Point 106,157),(New-Object System.Drawing.Point 165,123)))
$g.DrawLine($pen,94,209,162,209)
$ico = [System.Drawing.Icon]::FromHandle($image.GetHicon())
$out = [IO.File]::Create((Join-Path $projectRoot 'build\icon.ico'))
$ico.Save($out); $out.Dispose(); $g.Dispose(); $image.Dispose(); $pen.Dispose(); $brush.Dispose()
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path $projectRoot 'release\Anomalia-IPTV-1.0.0-Windows-Portable.exe'
$sources = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object {$_.FullName}
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$output" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Xml.Linq.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll "/win32icon:$projectRoot\build\icon.ico" "/resource:$projectRoot\build\icon.ico,icon.ico" "/resource:$projectRoot\vendor\vlc-3.0.24-win64.zip,vlc.zip" $sources
if ($LASTEXITCODE -ne 0) {throw 'C# compilation failed'}
Get-Item -LiteralPath $output | Select-Object FullName,Length
