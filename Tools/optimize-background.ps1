# 压缩背景图：长边缩到 1920，JPEG 质量 82，原图留 .orig 备份
# 用法：powershell -ExecutionPolicy Bypass -File .\Tools\optimize-background.ps1
[CmdletBinding()]
param(
    [string]$Path = "C:\Users\manifld\website\Main\data\themes",
    [int]$MaxSide = 1920,
    [int]$Quality = 82
)

$ErrorActionPreference = 'Stop'
$python = "C:\Users\manifld\.dsh\dsh-runtimes\dsh-next-primary-runtime\dependencies\python\python.exe"

if (-not (Test-Path $python)) {
    throw "找不到 Python：$python"
}

$files = Get-ChildItem $Path -Filter "background.*" -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -ne '.orig' }

if (-not $files) {
    throw "在 $Path 下没找到 background.*"
}

$file = $files | Select-Object -First 1
$before = [math]::Round($file.Length / 1KB)

$script = @'
import sys
from PIL import Image

source, target, max_side, quality = sys.argv[1], sys.argv[2], int(sys.argv[3]), int(sys.argv[4])
image = Image.open(source).convert("RGB")
width, height = image.size
scale = min(1.0, max_side / max(width, height))
if scale < 1.0:
    image = image.resize((int(width * scale), int(height * scale)), Image.LANCZOS)
image.save(target, "JPEG", quality=quality, optimize=True, progressive=True)
print(f"{width}x{height} -> {image.size[0]}x{image.size[1]}")
'@

$tempScript = Join-Path $env:TEMP "genkan-optimize-bg.py"
Set-Content $tempScript -Value $script -Encoding UTF8

$target = Join-Path $Path "background.jpg"
$backup = Join-Path $Path ("background." + $file.Extension.TrimStart('.') + ".orig")

if (-not (Test-Path $backup)) {
    Copy-Item $file.FullName $backup -Force
}

& $python $tempScript $file.FullName $target $MaxSide $Quality
if ($LASTEXITCODE -ne 0) {
    throw "压缩失败（$LASTEXITCODE）"
}

if ($file.FullName -ne $target) {
    Remove-Item $file.FullName -Force
}

$after = [math]::Round((Get-Item $target).Length / 1KB)
Write-Host "✓ 背景图已压缩：$before KB → $after KB" -ForegroundColor Green
Write-Host "  原图备份：$backup" -ForegroundColor DarkGray
