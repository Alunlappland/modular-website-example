# 给运行数据拍快照：数据库 + 上传文件 + 收件箱 + 文档
# 跑任何会动数据的测试之前先执行它。
[CmdletBinding()]
param([string]$Name = 'manual')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$target = Join-Path $root "Main\depend\snapshots\$Name-$stamp"

New-Item -ItemType Directory -Force -Path $target | Out-Null

$items = @(
    @{ Source = 'Main\database'; To = 'database' },
    @{ Source = 'Main\data\files'; To = 'files' },
    @{ Source = 'Main\data\inbox'; To = 'inbox' },
    @{ Source = 'Main\document'; To = 'document' },
    @{ Source = 'Main\conf'; To = 'conf' }
)

foreach ($item in $items) {
    $from = Join-Path $root $item.Source
    if (-not (Test-Path $from)) { continue }
    $to = Join-Path $target $item.To
    New-Item -ItemType Directory -Force -Path (Split-Path $to -Parent) | Out-Null
    Copy-Item $from $to -Recurse -Force
}

$size = (Get-ChildItem $target -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host "快照完成：$target（$([math]::Round($size / 1MB, 2)) MB）" -ForegroundColor Green
