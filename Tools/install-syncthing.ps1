# 安装并注册 Syncthing 为 Windows 服务（需要管理员）
[CmdletBinding()]
param(
    [string]$ServiceName = 'Syncthing',
    [string]$Nssm = 'C:\regser\nssm.exe'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw '请用「管理员身份」打开 PowerShell 再运行本脚本。'
}

$shim = Join-Path $env:USERPROFILE 'scoop\shims\syncthing.exe'
if (-not (Test-Path $shim)) {
    Write-Host '→ 通过 scoop 安装 Syncthing ...' -ForegroundColor Cyan
    & scoop install syncthing
    if ($LASTEXITCODE -ne 0) { throw 'scoop 安装 Syncthing 失败。' }
}

$home = Join-Path $root 'Main\depend\syncthing'
New-Item -ItemType Directory -Force -Path $home | Out-Null
$logs = Join-Path $root 'Main\logs'
New-Item -ItemType Directory -Force -Path $logs | Out-Null

if (-not (Test-Path $Nssm)) { throw "找不到 nssm：$Nssm" }

Write-Host '→ 注册服务 ...' -ForegroundColor Cyan
& $Nssm stop $ServiceName 2>$null | Out-Null
& $Nssm remove $ServiceName confirm 2>$null | Out-Null

& $Nssm install $ServiceName $shim
& $Nssm set $ServiceName AppParameters "serve --home=`"$home`" --no-browser --no-restart"
& $Nssm set $ServiceName AppDirectory $home
& $Nssm set $ServiceName DisplayName 'Syncthing'
& $Nssm set $ServiceName Description 'Syncthing 文件同步（玄关集成）'
& $Nssm set $ServiceName Start SERVICE_AUTO_START
& $Nssm set $ServiceName AppStdout (Join-Path $logs 'syncthing.out.log')
& $Nssm set $ServiceName AppStderr (Join-Path $logs 'syncthing.err.log')
& $Nssm set $ServiceName AppRotateFiles 1
& $Nssm set $ServiceName AppRotateOnline 1
& $Nssm set $ServiceName AppRotateBytes 10485760

Write-Host '→ 启动 ...' -ForegroundColor Cyan
Start-Service $ServiceName
Start-Sleep -Seconds 8

$config = Join-Path $home 'config.xml'
$apiKey = ''
if (Test-Path $config) {
    $apiKey = ([xml](Get-Content $config -Raw)).configuration.gui.apikey
}

Write-Host ''
Write-Host '✓ Syncthing 已启动' -ForegroundColor Green
Write-Host "  管理界面：http://127.0.0.1:8384"
Write-Host "  API Key ：$apiKey"
Write-Host ''
Write-Host '把它填进 Main\conf\appsettings.json 的 sync:syncthing:apiKey，然后刷新站点的 /sync 页面。' -ForegroundColor Yellow
