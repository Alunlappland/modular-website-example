# 安装并注册 Syncthing 为 Windows 服务（需要管理员）
[CmdletBinding()]
param(
    [string]$ServiceName = 'Syncthing',
    [string]$Nssm = 'C:\regser\nssm.exe'
)

$ErrorActionPreference = 'Continue'
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

$syncthingHome = Join-Path $root 'Main\depend\syncthing'
New-Item -ItemType Directory -Force -Path $syncthingHome | Out-Null
$logs = Join-Path $root 'Main\logs'
New-Item -ItemType Directory -Force -Path $logs | Out-Null

if (-not (Test-Path $Nssm)) { throw "找不到 nssm：$Nssm" }

Write-Host '→ 注册服务 ...' -ForegroundColor Cyan
& $Nssm stop $ServiceName 2>$null | Out-Null
& $Nssm remove $ServiceName confirm 2>$null | Out-Null
Start-Sleep -Seconds 1

& $Nssm install $ServiceName $shim
if ($LASTEXITCODE -ne 0) { throw "nssm install 失败（$LASTEXITCODE）" }
& $Nssm set $ServiceName AppParameters "serve --home=`"$syncthingHome`" --no-browser --no-restart"
& $Nssm set $ServiceName AppDirectory $syncthingHome
& $Nssm set $ServiceName DisplayName 'Syncthing'
& $Nssm set $ServiceName Description 'Syncthing 文件同步（玄关集成）'
& $Nssm set $ServiceName Start SERVICE_AUTO_START
& $Nssm set $ServiceName AppStdout (Join-Path $logs 'syncthing.out.log')
& $Nssm set $ServiceName AppStderr (Join-Path $logs 'syncthing.err.log')
& $Nssm set $ServiceName AppRotateFiles 1
& $Nssm set $ServiceName AppRotateOnline 1
& $Nssm set $ServiceName AppRotateBytes 10485760

Write-Host '→ 启动 ...' -ForegroundColor Cyan
Start-Service $ServiceName -ErrorAction SilentlyContinue
Start-Sleep -Seconds 10

$service = Get-Service $ServiceName -ErrorAction SilentlyContinue
if (-not $service -or $service.Status -ne 'Running') {
    $errLog = Join-Path $logs 'syncthing.err.log'
    if (Test-Path $errLog) { Get-Content $errLog -Tail 10 }
    throw "Syncthing 服务没有起来（状态：$($service.Status)），看 $errLog"
}

$config = Join-Path $syncthingHome 'config.xml'
$apiKey = ''
if (Test-Path $config) {
    $apiKey = ([xml](Get-Content $config -Raw)).configuration.gui.apikey
}

if ($apiKey) {
    $confPath = Join-Path $root 'Main\conf\appsettings.json'
    try {
        Copy-Item $confPath "$confPath.bak" -Force
        $cfg = Get-Content $confPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if (-not $cfg.sync) {
            $cfg | Add-Member -NotePropertyName sync -NotePropertyValue ([pscustomobject]@{ syncthing = [pscustomobject]@{ url = 'http://127.0.0.1:8384'; apiKey = '' } })
        }
        if (-not $cfg.sync.syncthing) {
            $cfg.sync | Add-Member -NotePropertyName syncthing -NotePropertyValue ([pscustomobject]@{ url = 'http://127.0.0.1:8384'; apiKey = '' })
        }
        $cfg.sync.syncthing.apiKey = $apiKey
        $cfg | ConvertTo-Json -Depth 20 | Set-Content $confPath -Encoding UTF8
        Write-Host '  ✓ API Key 已写入 appsettings.json（原文件备份为 appsettings.json.bak）' -ForegroundColor Green
    }
    catch {
        Write-Host "  自动写入失败，请手动把这段填进 sync:syncthing:apiKey：$apiKey" -ForegroundColor Yellow
    }
}

Write-Host ''
Write-Host '✓ Syncthing 已启动' -ForegroundColor Green
Write-Host "  管理界面：http://127.0.0.1:8384"
Write-Host "  API Key ：$apiKey"
Write-Host ''
Write-Host '把它填进 Main\conf\appsettings.json 的 sync:syncthing:apiKey，然后刷新站点的 /sync 页面。' -ForegroundColor Yellow
