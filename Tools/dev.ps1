# 开发模式：前台运行，改代码热重启
[CmdletBinding()]
param(
    [string]$Url = 'http://127.0.0.1:8080'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'Main\src\Genkan.App\Genkan.App.csproj'

$env:GENKAN_ROOT = $root
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_CLI_HOME = Join-Path $root 'Main\depend\tmp\dotnet'
$env:NUGET_PACKAGES = Join-Path $root 'Main\depend\nuget'
$env:DOTNET_NOLOGO = '1'

Write-Host "→ 玄关开发模式：$Url" -ForegroundColor Cyan
Write-Host "  站点根目录：$root" -ForegroundColor DarkGray
& dotnet run --project $project --no-launch-profile --urls $Url
