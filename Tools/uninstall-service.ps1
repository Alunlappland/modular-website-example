# 卸载 Windows 服务（保留数据）
[CmdletBinding()]
param(
    [string]$ServiceName = 'Genkan',
    [string]$Nssm = 'C:\regser\nssm.exe'
)

$ErrorActionPreference = 'Stop'

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw '请用「管理员身份」打开 PowerShell 再运行本脚本。'
}

Write-Host "→ 停止服务 $ServiceName ..." -ForegroundColor Cyan
& $Nssm stop $ServiceName 2>$null | Out-Null
& $Nssm remove $ServiceName confirm
Write-Host '✓ 已卸载（Main\data、Main\conf、Main\database 全部保留）' -ForegroundColor Green
