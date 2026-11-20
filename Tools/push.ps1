# 一键推送：GitHub + Gitee（首次会弹浏览器登录，之后凭据存在本机）
[CmdletBinding()]
param(
    [string]$Message = '',
    [switch]$SkipGitee,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "仓库：$root" -ForegroundColor DarkGray

if ($Message) {
    Write-Host "→ 提交改动：$Message" -ForegroundColor Cyan
    & git add -A
    & git commit -m $Message
}

Write-Host "`n当前状态：" -ForegroundColor DarkGray
& git status -sb
Write-Host ""

if ($DryRun) {
    Write-Host '（DryRun）不做实际推送。' -ForegroundColor Yellow
    exit 0
}

$failed = 0

Write-Host '→ 推送到 GitHub ...' -ForegroundColor Cyan
& git push -u origin main
if ($LASTEXITCODE -ne 0) { Write-Host '  ✗ GitHub 推送失败（可能未登录）' -ForegroundColor Red; $failed++ }
else { Write-Host '  ✓ GitHub 完成' -ForegroundColor Green }

if (-not $SkipGitee) {
    Write-Host '→ 推送到 Gitee ...' -ForegroundColor Cyan
    & git push -u gitee main
    if ($LASTEXITCODE -ne 0) { Write-Host '  ✗ Gitee 推送失败（可能未登录）' -ForegroundColor Red; $failed++ }
    else { Write-Host '  ✓ Gitee 完成' -ForegroundColor Green }
}

Write-Host ''
if ($failed -gt 0) {
    Write-Host '有仓库推送失败。首次使用请在弹出的浏览器窗口里完成登录；成功后凭据会保存在本机。' -ForegroundColor Yellow
    exit 1
}

Write-Host '两份远端都已同步。' -ForegroundColor Green
