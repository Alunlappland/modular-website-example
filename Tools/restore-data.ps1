# 从快照恢复运行数据（会覆盖当前数据，先停掉站点）
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Snapshot
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = if (Test-Path $Snapshot) { $Snapshot } else { Join-Path $root $Snapshot }

if (-not (Test-Path $source)) {
    throw "找不到快照：$source"
}

$running = Get-Process Genkan -ErrorAction SilentlyContinue
if ($running) {
    throw '站点还在运行，先停止 Genkan 进程（或服务）再恢复。'
}

$pairs = @(
    @{ From = 'database'; To = 'Main\database' },
    @{ From = 'files'; To = 'Main\data\files' },
    @{ From = 'inbox'; To = 'Main\data\inbox' },
    @{ From = 'document'; To = 'Main\document' },
    @{ From = 'conf'; To = 'Main\conf' }
)

foreach ($pair in $pairs) {
    $from = Join-Path $source $pair.From
    if (-not (Test-Path $from)) { continue }
    $to = Join-Path $root $pair.To
    if (Test-Path $to) { Remove-Item $to -Recurse -Force }
    New-Item -ItemType Directory -Force -Path (Split-Path $to -Parent) | Out-Null
    Copy-Item $from $to -Recurse -Force
}

Write-Host "已从快照恢复：$source" -ForegroundColor Green
