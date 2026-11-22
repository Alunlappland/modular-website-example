# 允许内网/虚拟网卡直连 8080（需要管理员）
# 只在「专用网络」放行；公用网络不放开，避免在陌生 Wi-Fi 上暴露。
[CmdletBinding()]
param(
    [int]$Port = 8080,
    [string]$RuleName = 'Genkan 8080 (LAN)'
)

$ErrorActionPreference = 'Continue'

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw '请用「管理员身份」打开 PowerShell 再运行本脚本。'
}

Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule

New-NetFirewallRule `
    -DisplayName $RuleName `
    -Direction Inbound `
    -Action Allow `
    -Protocol TCP `
    -LocalPort $Port `
    -Profile Private `
    -Description '玄关：允许 ZeroTier/EasyTier/局域网直接访问，绕过 Cloudflare 隧道' | Out-Null

Write-Host "✓ 已添加防火墙规则：$RuleName（仅专用网络，TCP $Port）" -ForegroundColor Green
Write-Host ''
Write-Host '直连地址（手机装好 ZeroTier/EasyTier 并加入同一网络后可用）：' -ForegroundColor Cyan

Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.IPAddress -notlike '127.*' } |
    ForEach-Object { Write-Host ("  http://" + $_.IPAddress + ":" + $Port + "  (" + $_.InterfaceAlias + ")") }

Write-Host ''
Write-Host '提示：管理页（/admin/*）仍然只允许本机回环访问，直连只能看公开页面和自己的账号内容。' -ForegroundColor DarkGray
