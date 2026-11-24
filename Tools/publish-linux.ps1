# 发布 Linux 版本（在 Windows 上交叉发布到目录）
# 用法：.\Tools\publish-linux.ps1 -Runtime linux-arm64
[CmdletBinding()]
param(
    [ValidateSet('linux-arm64', 'linux-x64')]
    [string]$Runtime = 'linux-arm64',
    [string]$Output = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'Main\src\Genkan.App\Genkan.App.csproj'

if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $root ("Main\dist-" + $Runtime)
}

if (Test-Path $Output) {
    Remove-Item $Output -Recurse -Force
}

$env:DOTNET_CLI_HOME = Join-Path $root 'Main\depend\tmp\dotnet'
$env:NUGET_PACKAGES = Join-Path $root 'Main\depend\nuget'
$env:DOTNET_NOLOGO = '1'

Write-Host "→ 发布 $Runtime 到 $Output" -ForegroundColor Cyan
& dotnet publish $project -c Release -r $Runtime --self-contained false -o $Output --nologo
if ($LASTEXITCODE -ne 0) {
    throw "发布失败（$LASTEXITCODE）"
}

Write-Host ''
Write-Host '✓ 完成。拷到服务器后需要：' -ForegroundColor Green
Write-Host '  1. 装 ASP.NET Core 10 运行时'
Write-Host '  2. 带上 Main/conf/appsettings.json、document、data、database、Extend'
Write-Host '  3. 重新填 kopia 密码 / AI Key / R2 凭据 / TOTP（Windows 的 DPAPI 密钥不可迁移）'
Write-Host '  4. 用 systemd 或 Caddy 起服务，详见 Docs/DEPLOY-LINUX.md'
