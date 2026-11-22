# 给关键服务配置「失败自动重启」与失败计数（需要管理员）
# 用法：powershell -ExecutionPolicy Bypass -File .\Tools\harden-services.ps1
[CmdletBinding()]
param(
    [string[]]$Services = @('Genkan', 'Syncthing', 'Cloudflared'),
    [string]$Nssm = 'C:\regser\nssm.exe'
)

$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw '请用「管理员身份」打开 PowerShell 再运行本脚本。'
}

foreach ($name in $Services) {
    $service = Get-Service $name -ErrorAction SilentlyContinue
    if (-not $service) {
        Write-Host "— $name 不存在，跳过" -ForegroundColor DarkGray
        continue
    }

    Write-Host "→ $name" -ForegroundColor Cyan

    # 启动类型：自动
    & sc.exe config $name start= auto | Out-Null

    # SCM 层：失败后 5 秒、15 秒、60 秒各重启一次，之后每天重置计数
    & sc.exe failure $name reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null

    # nssm 层（如果这个服务是 nssm 装的）：进程自己退出也重启
    if (Test-Path $Nssm) {
        & $Nssm set $name AppExit Default Restart 2>$null | Out-Null
        & $Nssm set $name AppRestartDelay 5000 2>$null | Out-Null
        & $Nssm set $name AppThrottle 5000 2>$null | Out-Null
    }

    # 确保当前是运行的
    if ($service.Status -ne 'Running') {
        Start-Service $name -ErrorAction SilentlyContinue
    }

    $state = (Get-Service $name -ErrorAction SilentlyContinue).Status
    Write-Host "  $name → $state" -ForegroundColor Green
}

Write-Host ''
Write-Host '✓ 完成。查看恢复策略：' -ForegroundColor Green
Write-Host '  sc qfailure Genkan' -ForegroundColor DarkGray
Write-Host '  nssm dump Genkan' -ForegroundColor DarkGray
Write-Host ''
Write-Host "日志：$root\Main\logs\service.err.log / app-*.log" -ForegroundColor DarkGray
