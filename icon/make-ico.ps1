# PinFolder のアイコン（.ico）を作る手順（magnet_16.svg・magnet_48.svg を直したら実行する。Edge が要る）
# 1. Edge で render.html を透明の背景で撮る
# 2. 16・24・32・48 px を切り出し、32bit（透過つき）の BMP 形式で 1 つの .ico にまとめる
param(
    [string]$OutIco = (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'PinFolder.ico')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
# 撮った画像・切り出した画像は一時フォルダに置き、ソースのフォルダを汚さない
$work = Join-Path $env:TEMP 'pinfolder_ico'
New-Item -ItemType Directory -Force $work | Out-Null
$shot = Join-Path $work 'shot.png'
$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Edge が見つかりません' }

$uri = ([System.Uri](Join-Path $here 'render.html')).AbsoluteUri
& $edge --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 `
    --default-background-color=00000000 --window-size=200,80 --virtual-time-budget=2000 `
    "--screenshot=$shot" $uri | Out-Null
Start-Sleep -Seconds 2
if (-not (Test-Path $shot)) { throw '撮影に失敗しました' }

# 切り出す位置（render.html の left と大きさ）
$frames = @(
    @{ Size = 16; Left = 0 },
    @{ Size = 24; Left = 20 },
    @{ Size = 32; Left = 50 },
    @{ Size = 48; Left = 90 }
)

$src = [System.Drawing.Bitmap]::FromFile($shot)
$images = @()
foreach ($f in $frames) {
    $s = $f.Size
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    for ($y = 0; $y -lt $s; $y++) {
        for ($x = 0; $x -lt $s; $x++) {
            $bmp.SetPixel($x, $y, $src.GetPixel($f.Left + $x, $y))
        }
    }
    $bmp.Save((Join-Path $work ("magnet_{0}.png" -f $s)), [System.Drawing.Imaging.ImageFormat]::Png)

    # BMP 形式の本体（BITMAPINFOHEADER＋下から上の BGRA＋AND マスク）
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([int]40); $bw.Write([int]$s); $bw.Write([int]($s * 2))
    $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]0)
    $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
    for ($y = $s - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $s; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
        }
    }
    # AND マスク（1 行は 4 バイト単位。透過は色の A で表すので、すべて 0）
    $maskRow = [int]([math]::Ceiling($s / 32.0) * 4)
    $bw.Write((New-Object byte[] ($maskRow * $s)))
    $bw.Flush()
    $images += , @{ Size = $s; Data = $ms.ToArray() }
    $bw.Dispose(); $bmp.Dispose()
}
$src.Dispose()

# .ico の見出し（ICONDIR）と各画像の目次（ICONDIRENTRY）
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $w.Write([byte]$img.Size); $w.Write([byte]$img.Size); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]$img.Data.Length); $w.Write([int]$offset)
    $offset += $img.Data.Length
}
foreach ($img in $images) { $w.Write($img.Data) }
$w.Flush()
[System.IO.File]::WriteAllBytes($OutIco, $out.ToArray())
$w.Dispose()
Write-Output ("作りました: {0}（{1} バイト）" -f $OutIco, (Get-Item $OutIco).Length)
