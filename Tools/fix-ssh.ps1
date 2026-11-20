# 修复 / 安装 SSH 服务（当系统自带 OpenSSH.Server 损坏时使用）
# 用法：管理员 PowerShell → powershell -ExecutionPolicy Bypass -File .\Tools\fix-ssh.ps1
[CmdletBinding()]
param(
    [switch]$TryNativeRepair,
    [int]$SshPort = 22
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw '请用「管理员身份」打开 PowerShell 再运行本脚本。'
}

function Test-SshdBinary([string]$exe) {
    if (-not (Test-Path $exe)) { return $false }
    & $exe -V *> $null
    return ($LASTEXITCODE -eq 0)
}

$systemExe = Join-Path $env:SystemRoot 'System32\OpenSSH\sshd.exe'
$msiExe = 'C:\Program Files\OpenSSH\sshd.exe'

Write-Host '→ 检查现有 sshd 是否能运行 ...' -ForegroundColor Cyan
$ok = (Test-SshdBinary $systemExe) -or (Test-SshdBinary $msiExe)
Write-Host "  系统自带: $(Test-SshdBinary $systemExe)   独立安装: $(Test-SshdBinary $msiExe)"

if (-not $ok -and $TryNativeRepair) {
    Write-Host '→ 尝试用 Windows 可选功能重新安装（可能较慢）...' -ForegroundColor Cyan
    $cap = Get-WindowsCapability -Online -Name 'OpenSSH.Server*' | Select-Object -First 1
    if ($cap) {
        try {
            if ($cap.State -eq 'Installed') { Remove-WindowsCapability -Online -Name $cap.Name | Out-Null }
            Add-WindowsCapability -Online -Name $cap.Name | Out-Null
        }
        catch {
            Write-Host "  可选功能修复失败：$($_.Exception.Message)" -ForegroundColor Yellow
        }
    }
    $ok = Test-SshdBinary $systemExe
}

if (-not $ok) {
    $msi = Join-Path $root 'Main\depend\openssh\OpenSSH-Win64-v10.4.0.0.msi'
    if (-not (Test-Path $msi)) {
        throw "找不到安装包：$msi（需要先把它放到 Main\depend\openssh\）"
    }

    Write-Host '→ 安装官方 Win32-OpenSSH（静默）...' -ForegroundColor Cyan
    $proc = Start-Process msiexec.exe -ArgumentList "/i `"$msi`" /qn /norestart" -Wait -PassThru
    if ($proc.ExitCode -ne 0) {
        throw "MSI 安装失败，退出码 $($proc.ExitCode)"
    }

    $ok = Test-SshdBinary $msiExe
}

if (-not $ok) {
    throw 'sshd 仍然无法运行。把这段完整输出发我，我再换方案。'
}

# ---- 配置目录与主机密钥 ----
$confDir = Join-Path $env:ProgramData 'ssh'
$confFile = Join-Path $confDir 'sshd_config'
New-Item -ItemType Directory -Force -Path $confDir | Out-Null

$installDir = if (Test-Path $msiExe) { Split-Path $msiExe } else { Join-Path $env:SystemRoot 'System32\OpenSSH' }

if (-not (Test-Path $confFile)) {
    $template = Join-Path $installDir 'sshd_config_default'
    if (-not (Test-Path $template)) { throw "缺少配置模板：$template" }
    Copy-Item $template $confFile -Force
    Write-Host '  ✓ 已生成 sshd_config'
}

$keygen = Join-Path $installDir 'ssh-keygen.exe'
$keys = Get-ChildItem $confDir -Filter 'ssh_host_*_key' -ErrorAction SilentlyContinue
if (-not $keys) {
    Write-Host '→ 生成主机密钥 ...' -ForegroundColor Cyan
    & $keygen -A
}

# ---- 启动服务 ----
Write-Host '→ 启动 sshd ...' -ForegroundColor Cyan
Set-Service -Name sshd -StartupType Automatic
$svc = Get-Service sshd
if ($svc.Status -ne 'Running') {
    Start-Service sshd
    $svc = Get-Service sshd
}

# ---- 防火墙 ----
if (-not (Get-NetFirewallRule -DisplayName 'Genkan SSH' -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName 'Genkan SSH' -Direction Inbound -Protocol TCP `
        -LocalPort $SshPort -Action Allow -Profile Any | Out-Null
    Write-Host '  ✓ 已放行防火墙'
}

# ---- 默认 shell ----
New-Item -Path 'HKLM:\SOFTWARE\OpenSSH' -Force | Out-Null
New-ItemProperty -Path 'HKLM:\SOFTWARE\OpenSSH' -Name DefaultShell `
    -Value (Get-Command powershell.exe).Source -PropertyType String -Force | Out-Null

# ---- 结果 ----
$listen = netstat.exe -ano | Select-String ":$SshPort\s+.*LISTENING"
$zeroTierIp = (Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.InterfaceAlias -like 'ZeroTier*' } |
    Select-Object -First 1).IPAddress

Write-Host ''
Write-Host "✓ sshd 状态：$($svc.Status)" -ForegroundColor Green
Write-Host "  监听 22  : $(if ($listen) { '是' } else { '否' })"
Write-Host "  用户名   : $env:USERNAME"
Write-Host "  ZeroTier : $zeroTierIp"
Write-Host ''
Write-Host '本机自测：ssh localhost' -ForegroundColor DarkGray
Write-Host '手机端  ：dsh-go → 远程主机 → SSH → 上面那个地址:22' -ForegroundColor DarkGray
