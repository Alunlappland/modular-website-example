# 构建：还原依赖并发布到 Main\bin
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$Clean,
    [switch]$Run
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'Main\src\Genkan.App\Genkan.App.csproj'
$out = Join-Path $root 'Main\bin'
$tmp = Join-Path $root 'Main\depend\tmp'

$env:DOTNET_CLI_HOME = Join-Path $tmp 'dotnet'
$env:NUGET_PACKAGES = Join-Path $root 'Main\depend\nuget'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

# 在受限沙箱里构建时，需要把 APPDATA/LOCALAPPDATA 也重定向到工作区
if ($env:GENKAN_SANDBOX -eq '1') {
    $env:APPDATA = Join-Path $tmp 'appdata'
    $env:LOCALAPPDATA = Join-Path $tmp 'localappdata'
    New-Item -ItemType Directory -Force -Path $env:APPDATA, $env:LOCALAPPDATA | Out-Null
}

New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES, $out, $tmp | Out-Null

if ($Clean -and (Test-Path $out)) {
    Remove-Item -Recurse -Force $out
}

Write-Host "→ 构建中（$Configuration）..." -ForegroundColor Cyan
& dotnet publish $project -c $Configuration -o $out --nologo
if ($LASTEXITCODE -ne 0) {
    throw "构建失败（exit $LASTEXITCODE）"
}

$exe = Join-Path $out 'Genkan.exe'
if (-not (Test-Path $exe)) {
    throw "没有找到产物：$exe"
}

$size = [math]::Round(((Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host "✓ 完成：$exe（产物合计 $size MB）" -ForegroundColor Green

if ($Run) {
    $env:GENKAN_ROOT = $root
    & $exe
}
