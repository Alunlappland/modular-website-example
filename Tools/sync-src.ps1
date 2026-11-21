# 一次性同步项目的 src 目录（SMB 或 SSH 两种目标）
# 用法示例：
#   .\sync-src.ps1 -Project D:\work\proj -Remote \\192.168.196.228\work\proj -Mode push
#   .\sync-src.ps1 -Project D:\work\proj -Remote manifld@192.168.196.228:/home/manifld/proj -Mode pull
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Project,
    [Parameter(Mandatory = $true)][string]$Remote,
    [ValidateSet('push', 'pull')][string]$Mode = 'push',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$source = Join-Path $Project 'src'
if ($Mode -eq 'push' -and -not (Test-Path $source)) {
    throw "找不到 $source"
}

$excludeDirs = @('bin', 'obj', 'node_modules', '.git', 'target', 'dist', 'build', '.vs', '.idea', '.venv', '__pycache__')
$excludeFiles = @('*.user', '*.suo', '*.tmp', 'Thumbs.db')

if ($Remote -like '\\\\*' -or $Remote -like '*:\\*') {
    # SMB / 本地路径：robocopy
    if ($Mode -eq 'push') {
        $arguments = @($source, $Remote) + '/MIR /XJ /R:2 /W:2 /NFL /NDL /NP'
        foreach ($dir in $excludeDirs) { $arguments += '/XD'; $arguments += $dir }
        foreach ($file in $excludeFiles) { $arguments += '/XF'; $arguments += $file }
    }
    else {
        $arguments = @($Remote, $source) + '/MIR /XJ /R:2 /W:2 /NFL /NDL /NP'
        foreach ($dir in $excludeDirs) { $arguments += '/XD'; $arguments += $dir }
        foreach ($file in $excludeFiles) { $arguments += '/XF'; $arguments += $file }
    }

    if ($DryRun) { $arguments += '/L' }

    Write-Host "robocopy $($arguments -join ' ')" -ForegroundColor DarkGray
    & robocopy @arguments
    $code = $LASTEXITCODE
    if ($code -ge 8) { throw "robocopy 失败（$code）" }
    Write-Host '完成。' -ForegroundColor Green
    exit 0
}

if ($Remote -match '^[^@]+@[^:]+:.+$') {
    # SSH：scp
    $target = if ($Mode -eq 'push') { "$Remote/" } else { "$Remote/src" }
    $origin = if ($Mode -eq 'push') { $source } else { "$Remote/src" }
    $destination = if ($Mode -eq 'push') { "$Remote/" } else { (Join-Path $Project 'src') }

    $ssh = Get-Command ssh.exe -ErrorAction SilentlyContinue
    if (-not $ssh) { throw '找不到 ssh.exe（先装 OpenSSH 客户端）。' }

    $sshArgs = @($origin, $destination, '-r')
    if ($DryRun) {
        Write-Host "（预览）scp $($sshArgs -join ' ')" -ForegroundColor DarkGray
        exit 0
    }

    Write-Host "scp $($sshArgs -join ' ')" -ForegroundColor DarkGray
    & ssh.exe -V *> $null
    & scp.exe @sshArgs
    if ($LASTEXITCODE -ne 0) { throw "scp 失败（$LASTEXITCODE）" }
    Write-Host '完成。' -ForegroundColor Green
    exit 0
}

throw 'Remote 只能填 \\主机\共享\路径 或 user@host:/path'
