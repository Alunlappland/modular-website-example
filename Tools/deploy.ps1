# 构建 + 部署 + 重启服务（需要管理员：要停/启 Windows 服务）
# 用法：powershell -ExecutionPolicy Bypass -File .\Tools\deploy.ps1
[CmdletBinding()]
param(
    [string]$ServiceName = 'Genkan',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'Main\src\Genkan.App\Genkan.App.csproj'
$live = Join-Path $root 'Main\bin'
$next = Join-Path $root 'Main\bin.next'
$old = Join-Path $root 'Main\bin.old'

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $NoBuild) {
    Write-Host '→ 构建到 Main\bin.next ...' -ForegroundColor Cyan
    $env:DOTNET_CLI_HOME = Join-Path $root 'Main\depend\tmp\dotnet'
    $env:NUGET_PACKAGES = Join-Path $root 'Main\depend\nuget'
    $env:DOTNET_NOLOGO = '1'
    if (Test-Path $next) { Remove-Item $next -Recurse -Force }
    & dotnet publish $project -c Release -o $next --nologo
    if ($LASTEXITCODE -ne 0) { throw "构建失败（$LASTEXITCODE）" }
}

if (-not (Test-Path (Join-Path $next 'Genkan.exe'))) {
    throw "没有可部署的产物：$next\Genkan.exe"
}

$service = Get-Service $ServiceName -ErrorAction SilentlyContinue

if ($service) {
    if (-not $isAdmin) { throw '停服务需要管理员权限，请用管理员 PowerShell 运行。' }
    Write-Host "→ 停止服务 $ServiceName ..." -ForegroundColor Cyan
    Stop-Service $ServiceName -Force
    Start-Sleep -Seconds 2
}
else {
    Write-Host '→ 停止手动启动的 Genkan 进程 ...' -ForegroundColor DarkGray
    Get-Process Genkan -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
}

Write-Host '→ 交换 Main\bin ...' -ForegroundColor Cyan
if (Test-Path $old) { Remove-Item $old -Recurse -Force }
Rename-Item $live $old
Rename-Item $next $live

if ($service) {
    Write-Host "→ 启动服务 $ServiceName ..." -ForegroundColor Cyan
    Start-Service $ServiceName
}
else {
    Start-Process -FilePath (Join-Path $live 'Genkan.exe') -WindowStyle Hidden
}

Start-Sleep -Seconds 3

$healthy = $false
try {
    $client = New-Object System.Net.Sockets.TcpClient('127.0.0.1', 8080)
    $stream = $client.GetStream()
    $bytes = [Text.Encoding]::ASCII.GetBytes("GET /healthz HTTP/1.1`r`nHost: localhost`r`nConnection: close`r`n`r`n")
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Flush()
    $text = (New-Object System.IO.StreamReader($stream)).ReadToEnd()
    $client.Close()
    $healthy = $text -match '200 OK'
}
catch {
    $healthy = $false
}

if ($healthy) {
    Write-Host '✓ 部署完成，/healthz 正常' -ForegroundColor Green
    Write-Host "  旧版本保留在 $old，确认没问题后可删除。" -ForegroundColor DarkGray
    exit 0
}

Write-Host '✗ 启动后健康检查失败，正在回滚 ...' -ForegroundColor Red
if ($service) { Stop-Service $ServiceName -ErrorAction SilentlyContinue }
if (Test-Path $live) { Remove-Item $live -Recurse -Force -ErrorAction SilentlyContinue }
Rename-Item $old $live
if ($service) { Start-Service $ServiceName -ErrorAction SilentlyContinue }
throw '已回滚到上一版本，请检查 Main\logs 下的日志。'
