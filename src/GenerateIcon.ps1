param([string]$Output)
$ErrorActionPreference = 'Stop'
$scriptDirectory = Split-Path $MyInvocation.MyCommand.Path -Parent
if([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $scriptDirectory '..\build'
}
$Output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path $Output | Out-Null

function New-RoundPath([single]$x,[single]$y,[single]$width,[single]$height,[single]$radius) {
    $shape=[Drawing.Drawing2D.GraphicsPath]::new(); $d=$radius*2
    $shape.AddArc($x,$y,$d,$d,180,90)
    $shape.AddArc(($x+$width-$d),$y,$d,$d,270,90)
    $shape.AddArc(($x+$width-$d),($y+$height-$d),$d,$d,0,90)
    $shape.AddArc($x,($y+$height-$d),$d,$d,90,90); $shape.CloseFigure()
    return $shape
}

function New-Ribbon([bool]$left) {
    $path=[Drawing.Drawing2D.GraphicsPath]::new()
    if($left) { $path.AddBezier(146,157,169,198,209,237,257,263) }
    else {
        # A taller right arm and a curved tail give the Y its own silhouette.
        $path.AddBezier(367,132,344,183,309,226,268,258)
        $path.AddBezier(268,258,254,281,246,309,247,337)
        $path.AddBezier(247,337,248,369,267,383,306,383)
    }
    return $path
}

function Paint-Glow($g,[single]$x,[single]$y,[single]$w,[single]$h,[Drawing.Color]$color) {
    $ellipse=[Drawing.Drawing2D.GraphicsPath]::new(); $ellipse.AddEllipse($x,$y,$w,$h)
    $brush=[Drawing.Drawing2D.PathGradientBrush]::new($ellipse)
    $brush.CenterColor=$color
    $brush.SurroundColors=[Drawing.Color[]]@([Drawing.Color]::FromArgb(0,$color.R,$color.G,$color.B))
    $g.FillPath($brush,$ellipse); $brush.Dispose(); $ellipse.Dispose()
}

function Paint-Ribbon($g,$path,[single]$width,$brush) {
    $pen=[Drawing.Pen]::new($brush,$width)
    $pen.StartCap=[Drawing.Drawing2D.LineCap]::Round; $pen.EndCap=[Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin=[Drawing.Drawing2D.LineJoin]::Round
    $g.DrawPath($pen,$path); $pen.Dispose()
}

function New-IconBitmap([int]$size) {
    $factor=4; if($size -ge 256) { $factor=2 }
    $canvas=[Drawing.Bitmap]::new(($size*$factor),($size*$factor),[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g=[Drawing.Graphics]::FromImage($canvas); $g.Clear([Drawing.Color]::Transparent)
    $g.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.ScaleTransform(($size*$factor/512.0),($size*$factor/512.0))
    $detail=$size -ge 32
    $background=New-RoundPath 24 24 464 464 112
    $brush=[Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Point]::new(60,24),[Drawing.Point]::new(442,488),[Drawing.Color]::FromArgb(40,49,73),[Drawing.Color]::FromArgb(13,19,33))
    $brush.WrapMode=[Drawing.Drawing2D.WrapMode]::TileFlipXY
    $g.FillPath($brush,$background); $brush.Dispose()
    if($detail) {
        $clip=$g.Save(); $g.SetClip($background)
        Paint-Glow $g 75 56 330 330 ([Drawing.Color]::FromArgb(38,152,127,255))
        Paint-Glow $g 146 192 320 300 ([Drawing.Color]::FromArgb(28,64,241,219)); $g.Restore($clip)
        $pen=[Drawing.Pen]::new([Drawing.Color]::FromArgb(54,185,208,247),2)
        $g.DrawPath($pen,$background); $pen.Dispose()
    }
    $left=New-Ribbon $true; $main=New-Ribbon $false
    [single]$width=68; if($size -le 24) { $width=76 }
    if($detail) {
        # One combined shadow keeps the ribbon junction visually clean.
        $shape=[Drawing.Drawing2D.GraphicsPath]::new([Drawing.Drawing2D.FillMode]::Winding)
        foreach($ribbon in @($left,$main)) {
            $outline=$ribbon.Clone(); $pen=[Drawing.Pen]::new([Drawing.Color]::Black,$width)
            $pen.StartCap=[Drawing.Drawing2D.LineCap]::Round; $pen.EndCap=[Drawing.Drawing2D.LineCap]::Round; $pen.LineJoin=[Drawing.Drawing2D.LineJoin]::Round
            $outline.Widen($pen); $pen.Dispose(); $shape.AddPath($outline,$false); $outline.Dispose()
        }
        $matrix=[Drawing.Drawing2D.Matrix]::new(); $matrix.Translate(0,10); $shape.Transform($matrix); $matrix.Dispose()
        $brush=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(66,0,6,19))
        $g.FillPath($brush,$shape); $brush.Dispose(); $shape.Dispose()
    }
    $lavender=[Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Point]::new(125,131),[Drawing.Point]::new(279,282),[Drawing.Color]::FromArgb(209,198,255),[Drawing.Color]::FromArgb(139,110,240))
    $lavender.WrapMode=[Drawing.Drawing2D.WrapMode]::TileFlipXY
    Paint-Ribbon $g $left $width $lavender; $lavender.Dispose()
    $mint=[Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Point]::new(390,60),[Drawing.Point]::new(230,450),[Drawing.Color]::FromArgb(145,255,224),[Drawing.Color]::FromArgb(46,174,209))
    $mint.WrapMode=[Drawing.Drawing2D.WrapMode]::TileFlipXY
    $blend=[Drawing.Drawing2D.ColorBlend]::new(3)
    $blend.Colors=[Drawing.Color[]]@([Drawing.Color]::FromArgb(145,255,224),[Drawing.Color]::FromArgb(72,218,216),[Drawing.Color]::FromArgb(46,174,209))
    $blend.Positions=[single[]]@(0,.62,1); $mint.InterpolationColors=$blend
    Paint-Ribbon $g $main $width $mint; $mint.Dispose()
    if($size -ge 48) {
        $highlight=[Drawing.Drawing2D.GraphicsPath]::new(); $highlight.AddBezier(139,145,163,187,196,222,225,240)
        $brush=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(88,243,236,255))
        Paint-Ribbon $g $highlight 3 $brush; $brush.Dispose(); $highlight.Dispose()
        $highlight=[Drawing.Drawing2D.GraphicsPath]::new(); $highlight.AddBezier(358,121,336,173,300,218,260,247)
        $brush=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(100,232,255,250))
        Paint-Ribbon $g $highlight 3 $brush; $brush.Dispose(); $highlight.Dispose()
        # The coral stitch disappears deliberately in the smallest sizes.
        $stitch=[Drawing.Drawing2D.GraphicsPath]::new(); $stitch.AddLine(350,139,340,157)
        $brush=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255,178,166))
        Paint-Ribbon $g $stitch 10 $brush; $brush.Dispose(); $stitch.Dispose()
    }
    $left.Dispose(); $main.Dispose(); $background.Dispose(); $g.Dispose()
    $result=[Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g=[Drawing.Graphics]::FromImage($result)
    $g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.CompositingQuality=[Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($canvas,0,0,$size,$size); $g.Dispose(); $canvas.Dispose()
    return $result
}

$sizes=@(16,20,24,32,40,48,64,96,128,256); $frames=@()
foreach($size in $sizes) {
    $bitmap=New-IconBitmap $size; $memory=[IO.MemoryStream]::new()
    $bitmap.Save($memory,[Drawing.Imaging.ImageFormat]::Png); $frames+=,@{Size=$size;Bytes=$memory.ToArray()}
    $memory.Dispose(); $bitmap.Dispose()
}
$iconFile=Join-Path $Output 'Yarnelo.ico'
$writer=[IO.BinaryWriter]::new([IO.File]::Create($iconFile))
$writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$sizes.Count); $offset=6+16*$sizes.Count
foreach($frame in $frames) {
    $dimension=0; if($frame.Size -lt 256) { $dimension=$frame.Size }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([UInt16]1); $writer.Write([UInt16]32); $writer.Write([UInt32]$frame.Bytes.Length); $writer.Write([UInt32]$offset)
    $offset+=$frame.Bytes.Length
}
foreach($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }; $writer.Dispose()
$versionMatch=[regex]::Match([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Program.cs')),'AssemblyFileVersion\("([0-9.]+)"\)')
if(!$versionMatch.Success) { throw 'Не удалось определить версию значка' }
$version=[Version]$versionMatch.Groups[1].Value
$versionName='Yarnelo-{0}.{1}.{2}.ico' -f $version.Major,$version.Minor,$version.Build
Copy-Item -LiteralPath $iconFile -Destination (Join-Path $Output $versionName) -Force
foreach($size in @(16,24,32,48,256,512)) {
    $bitmap=New-IconBitmap $size; $name='Yarnelo-Icon-{0}.png' -f $size
    if($size -eq 512) { $name='Yarnelo-Icon.png' }
    $bitmap.Save((Join-Path $Output $name),[Drawing.Imaging.ImageFormat]::Png); $bitmap.Dispose()
}
$svg=@'
<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512">
<title>Yarnelo — flowing Y</title>
<desc>Original asymmetrical Y ribbon mark for Yarnelo. Rounded glass tile, lavender arm, mint-cyan flowing thread and coral stitch.</desc>
<defs>
 <linearGradient id="bg" x1="60" y1="24" x2="442" y2="488" gradientUnits="userSpaceOnUse"><stop stop-color="#283149"/><stop offset="1" stop-color="#0d1321"/></linearGradient>
 <linearGradient id="lavender" x1="125" y1="131" x2="279" y2="282" gradientUnits="userSpaceOnUse"><stop stop-color="#d1c6ff"/><stop offset="1" stop-color="#8b6ef0"/></linearGradient>
 <linearGradient id="mint" x1="390" y1="60" x2="230" y2="450" gradientUnits="userSpaceOnUse"><stop stop-color="#91ffe0"/><stop offset=".62" stop-color="#48dad8"/><stop offset="1" stop-color="#2eaed1"/></linearGradient>
 <radialGradient id="purpleGlow"><stop stop-color="#987fff" stop-opacity=".15"/><stop offset="1" stop-color="#987fff" stop-opacity="0"/></radialGradient>
 <radialGradient id="mintGlow"><stop stop-color="#40f1db" stop-opacity=".11"/><stop offset="1" stop-color="#40f1db" stop-opacity="0"/></radialGradient>
 <clipPath id="tile"><rect x="24" y="24" width="464" height="464" rx="112"/></clipPath>
</defs>
<rect x="24" y="24" width="464" height="464" rx="112" fill="url(#bg)" stroke="#b9d0f7" stroke-opacity=".21" stroke-width="2"/>
<g clip-path="url(#tile)"><ellipse cx="240" cy="221" rx="165" ry="165" fill="url(#purpleGlow)"/><ellipse cx="306" cy="342" rx="160" ry="150" fill="url(#mintGlow)"/></g>
<g transform="translate(0 10)" fill="none" stroke="#000613" stroke-opacity=".26" stroke-width="68" stroke-linecap="round" stroke-linejoin="round"><path d="M146 157C169 198 209 237 257 263M367 132C344 183 309 226 268 258C254 281 246 309 247 337C248 369 267 383 306 383"/></g>
<g fill="none" stroke-width="68" stroke-linecap="round" stroke-linejoin="round"><path d="M146 157C169 198 209 237 257 263" stroke="url(#lavender)"/><path d="M367 132C344 183 309 226 268 258C254 281 246 309 247 337C248 369 267 383 306 383" stroke="url(#mint)"/></g>
<g fill="none" stroke-width="3" stroke-linecap="round"><path d="M139 145C163 187 196 222 225 240" stroke="#f3ecff" stroke-opacity=".35"/><path d="M358 121C336 173 300 218 260 247" stroke="#e8fffa" stroke-opacity=".39"/></g>
<path d="M350 139L340 157" fill="none" stroke="#ffb2a6" stroke-width="10" stroke-linecap="round"/>
</svg>
'@
[IO.File]::WriteAllText((Join-Path $Output 'Yarnelo-Icon.svg'),$svg,[Text.UTF8Encoding]::new($false))
Write-Output $iconFile
