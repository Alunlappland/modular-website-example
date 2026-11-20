# 一次性配置：让手机（dsh-go）通过 ZeroTier + SSH 连回这台电脑
# 用法：管理员 PowerShell → powershell -ExecutionPolicy Bypass -File .\Tools\enable-ssh-remote.ps1
[CmdletBinding()]
param(
    [int]$SshPort = 22
)

$ErrorActionPreference = 'Stop'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent())
    .IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw '请用「管理员身份」打开 PowerShell 再运行本脚本。'
}

Write-Host '→ 检查 OpenSSH Server 能力 ...' -ForegroundColor Cyan
$capability = Get-WindowsCapability -Online -Name 'OpenSSH.Server*' | Select-Object -First 1
if ($null -eq $capability) {
    throw '系统里没有 OpenSSH.Server 能力，可能需要联网或系统版本不支持。'
}

if ($capability.State -ne 'Installed') {
    Write-Host '→ 安装 OpenSSH Server（需要联网）...' -ForegroundColor Cyan
    Add-WindowsCapability -Online -Name $capability.Name | Out-Null
}

Write-Host '→ 设置为开机自启并启动 ...' -ForegroundColor Cyan
Set-Service -Name sshd -StartupType Automatic
Start-Service -Name sshd

Write-Host '→ 放行防火墙（仅 TCP 22）...' -ForegroundColor Cyan
if (-not (Get-NetFirewallRule -DisplayName 'Genkan SSH' -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName 'Genkan SSH' -Direction Inbound -Protocol TCP `
        -LocalPort $SshPort -Action Allow -Profile Any | Out-Null
}

Write-Host '→ 默认 shell 设为 PowerShell ...' -ForegroundColor Cyan
New-Item -Path 'HKLM:\SOFTWARE\OpenSSH' -Force | Out-Null
New-ItemProperty -Path 'HKLM:\SOFTWARE\OpenSSH' -Name DefaultShell `
    -Value (Get-Command powershell.exe).Source -PropertyType String -Force | Out-Null

$zeroTierIp = (Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.InterfaceAlias -like 'ZeroTier*' } |
    Select-Object -First 1).IPAddress

$lanIps = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
    Select-Object -ExpandProperty IPAddress

Write-Host ''
Write-Host '✓ SSH 已就绪' -ForegroundColor Green
Write-Host "  端口     : $SshPort"
Write-Host "  用户名   : $env:USERNAME"
Write-Host "  ZeroTier : $zeroTierIp"
Write-Host "  本机地址 : $($lanIps -join ', ')"
Write-Host ''
Write-Host '在 dsh-go 里添加远程主机：' -ForegroundColor Yellow
Write-Host "  主机地址：$zeroTierIp   （手机需先加入同一个 ZeroTier 网络）"
Write-Host "  端口：$SshPort    用户名：$env:USERNAME    认证：密码或密钥"
Write-Host ''
Write-Host '本机测试：ssh $env:USERNAME@localhost' -ForegroundColor DarkGray
