# 注册为 Windows 服务（需要管理员权限）
[CmdletBinding()]
param(
    [string]$ServiceName = 'Genkan',
    [string]$Nssm = 'C:\regser\nssm.exe',
    [string]$Url = 'http://localhost:8080'
)

$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'Main\bin\Genkan.exe'

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw '请用「管理员身份」打开 PowerShell 再运行本脚本。'
}

if (-not (Test-Path $exe)) {
    throw "找不到 $exe，请先运行 Tools\build.ps1"
}
if (-not (Test-Path $Nssm)) {
    throw "找不到 nssm：$Nssm"
}

$logs = Join-Path $root 'Main\logs'
New-Item -ItemType Directory -Force -Path $logs | Out-Null

Write-Host '→ 停掉手动启动的 Genkan 进程（如果有）...' -ForegroundColor DarkGray
Get-Process Genkan -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Host '→ 清理旧服务（不存在也不报错）...' -ForegroundColor DarkGray
& $Nssm stop $ServiceName 2>$null | Out-Null
& $Nssm remove $ServiceName confirm 2>$null | Out-Null
Start-Sleep -Seconds 1

Write-Host "→ 注册服务 $ServiceName ..." -ForegroundColor Cyan
& $Nssm install $ServiceName $exe
if ($LASTEXITCODE -ne 0) { throw "nssm install 失败（$LASTEXITCODE）" }

& $Nssm set $ServiceName AppDirectory (Join-Path $root 'Main\bin')
& $Nssm set $ServiceName AppParameters ""
& $Nssm set $ServiceName AppEnvironmentExtra "GENKAN_ROOT=$root"
& $Nssm set $ServiceName DisplayName '玄关 Genkan'
& $Nssm set $ServiceName Description '个人站点门户：玄关。http://localhost:8080'
& $Nssm set $ServiceName Start SERVICE_AUTO_START
& $Nssm set $ServiceName AppStdout (Join-Path $logs 'service.out.log')
& $Nssm set $ServiceName AppStderr (Join-Path $logs 'service.err.log')
& $Nssm set $ServiceName AppRotateFiles 1
& $Nssm set $ServiceName AppRotateOnline 1
& $Nssm set $ServiceName AppRotateBytes 10485760
& $Nssm set $ServiceName AppStopMethodConsole 15000
& $Nssm set $ServiceName AppThrottle 5000

Write-Host '→ 启动服务 ...' -ForegroundColor Cyan
Start-Service $ServiceName -ErrorAction Stop
Start-Sleep -Seconds 2

$service = Get-Service $ServiceName
Write-Host "✓ 服务状态：$($service.Status)" -ForegroundColor Green
Write-Host "  访问：$Url" -ForegroundColor Green
Write-Host "  健康检查：$Url/healthz" -ForegroundColor DarkGray
Write-Host "  日志：$logs\app-*.log / service.out.log" -ForegroundColor DarkGray
Write-Host ''
Write-Host '提示：装好服务后，把启动目录里的 Genkan.vbs 删掉，避免重复启动。' -ForegroundColor Yellow
