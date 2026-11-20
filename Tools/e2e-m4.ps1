# M4 端到端测试：kopia 备份、快照列表、恢复、历史
# 前提：appsettings 的 backup 指向临时仓库、schedule 关闭、sources 指向测试目录；数据库干净
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://127.0.0.1:8080',
    [string]$UserName = 'manifld',
    [string]$Password = 'test-password-9283',
    [string]$RepoPassword = 'test-pass-1234'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$script:pass = 0
$script:fail = 0

function New-Client {
    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.AllowAutoRedirect = $false
    $client = New-Object System.Net.Http.HttpClient($handler)
    $client.BaseAddress = [Uri]$BaseUrl
    return $client
}

function Get-Page($client, [string]$Path) {
    $response = $client.GetAsync($Path).GetAwaiter().GetResult()
    $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    return [pscustomobject]@{ Status = [int]$response.StatusCode; Body = $body }
}

function Post-Form($client, [string]$Path, [string]$Form) {
    $content = New-Object System.Net.Http.StringContent($Form, [Text.Encoding]::UTF8, 'application/x-www-form-urlencoded')
    $response = $client.PostAsync($Path, $content).GetAwaiter().GetResult()
    $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    return [pscustomobject]@{ Status = [int]$response.StatusCode; Body = $body }
}

function Check([string]$Name, [bool]$Ok, [string]$Detail = '') {
    if ($Ok) { $script:pass++; Write-Host ("  ✓ {0}" -f $Name) -ForegroundColor Green }
    else { $script:fail++; Write-Host ("  ✗ {0}  {1}" -f $Name, $Detail) -ForegroundColor Red }
}

function Get-Token([string]$Html) {
    return [regex]::Match($Html, '__RequestVerificationToken[^>]*value="([^"]+)"').Groups[1].Value
}

function Encode([string]$Value) { return [System.Net.WebUtility]::UrlEncode($Value) }

Write-Host "`n== M4 端到端测试：$BaseUrl ==`n" -ForegroundColor Cyan

$admin = New-Client
$health = Get-Page $admin '/healthz'
if ($health.Body -notmatch '"initialized":false') {
    throw '数据库不是干净的：先停服务并删除 Main\database\genkan.db* 再跑。'
}

$setup = Get-Page $admin '/setup'
$token = Get-Token $setup.Body
$created = Post-Form $admin '/setup' "Display=$(Encode '测试站长')&UserName=$(Encode $UserName)&Password=$(Encode $Password)&Confirm=$(Encode $Password)&__RequestVerificationToken=$(Encode $token)"
Check '初始化管理员' ($created.Status -eq 302) $created.Status

$page = Get-Page $admin '/backup'
Check '备份页 200' ($page.Status -eq 200) $page.Status
Check '页面显示就绪状态区' ($page.Body -match 'kopia\.exe') ''

$csrf = Get-Token $page.Body
$saved = Post-Form $admin '/backup?handler=Password' "Password=$(Encode $RepoPassword)&__RequestVerificationToken=$(Encode $csrf)"
Check '保存仓库密码' ($saved.Status -eq 200 -and $saved.Body -match '加密保存') $saved.Status

$page2 = Get-Page $admin '/backup'
$csrf2 = Get-Token $page2.Body
Check '密码显示为已保存' ($page2.Body -match '已保存（加密）') ''

$run = Post-Form $admin '/backup?handler=Run' "kind=daily&__RequestVerificationToken=$(Encode $csrf2)"
Check '执行增量备份' ($run.Status -eq 200 -and $run.Body -match '备份完成') $run.Status

$page3 = Get-Page $admin '/backup'
$csrf3 = Get-Token $page3.Body
Check '历史出现 success' ($page3.Body -match 'success') ''
Check '快照列表非空' ($page3.Body -match 'data-snapshot=') ''

$full = Post-Form $admin '/backup?handler=Run' "kind=full&__RequestVerificationToken=$(Encode $csrf3)"
Check '执行全量备份' ($full.Status -eq 200 -and $full.Body -match '备份完成') $full.Status

$page4 = Get-Page $admin '/backup'
$csrf4 = Get-Token $page4.Body
$snapshotId = [regex]::Match($page4.Body, 'data-snapshot="([^"]+)"').Groups[1].Value
Check '拿到快照 ID' ($snapshotId.Length -ge 16) $snapshotId

$restore = Post-Form $admin '/backup?handler=Restore' "SnapshotId=$(Encode $snapshotId)&__RequestVerificationToken=$(Encode $csrf4)"
Check '恢复成功' ($restore.Status -eq 200 -and $restore.Body -match '已恢复到') $restore.Status

$staging = "C:\Users\manifld\website\Main\data\restore-staging"
$restored = Get-ChildItem $staging -Recurse -File -ErrorAction SilentlyContinue
Check 'staging 里出现恢复的文件' ($restored.Count -ge 1) "$($restored.Count)"

Write-Host "`n通过 $script:pass 项，失败 $script:fail 项`n" -ForegroundColor ($(if ($script:fail -eq 0) { 'Green' } else { 'Red' }))
if ($script:fail -gt 0) { exit 1 }
