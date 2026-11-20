# M1 端到端测试：仪表盘、系统 API、路由管理（含仅本机限制、热生效）
# 前提：数据库是干净的（首次运行会走安装向导）
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://127.0.0.1:8080',
    [string]$UserName = 'manifld',
    [string]$Password = 'test-password-9283'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$script:pass = 0
$script:fail = 0
$script:token = $null

function New-Client {
    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.AllowAutoRedirect = $false
    $client = New-Object System.Net.Http.HttpClient($handler)
    $client.BaseAddress = [Uri]$BaseUrl
    return $client
}

function Get-Page($client, [string]$Path, [hashtable]$Headers = @{}) {
    $request = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Get, $Path)
    foreach ($key in $Headers.Keys) { $request.Headers.TryAddWithoutValidation($key, $Headers[$key]) | Out-Null }
    $response = $client.SendAsync($request).GetAwaiter().GetResult()
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

Write-Host "`n== M1 端到端测试：$BaseUrl ==`n" -ForegroundColor Cyan

$client = New-Client

# ---- 初始化 ----
$health = Get-Page $client '/healthz'
if ($health.Body -notmatch '"initialized":false') {
    throw '数据库不是干净的：先停服务并删除 Main\database\genkan.db* 再跑。'
}

$setup = Get-Page $client '/setup'
$script:token = Get-Token $setup.Body
$form = "Display=$(Encode '测试站长')&UserName=$(Encode $UserName)&Password=$(Encode $Password)&Confirm=$(Encode $Password)&__RequestVerificationToken=$(Encode $script:token)"
$created = Post-Form $client '/setup' $form
Check '初始化管理员' ($created.Status -eq 302) "$($created.Status)"

# ---- 仪表盘 ----
$dashboard = Get-Page $client '/dashboard'
Check '仪表盘 200' ($dashboard.Status -eq 200) $dashboard.Status
Check '仪表盘含处理器卡片' ($dashboard.Body -match '处理器')
Check '仪表盘含进程表' ($dashboard.Body -match 'proc-body')

$sys = Get-Page $client '/api/sys'
Check '/api/sys 200' ($sys.Status -eq 200) $sys.Status
Check '/api/sys 返回 cpuPercent' ($sys.Body -match '"cpuPercent"')
Check '/api/sys 返回 disks' ($sys.Body -match '"disks"')

# ---- 路由管理 ----
$routes = Get-Page $client '/admin/routes'
Check '路由管理页 200' ($routes.Status -eq 200) $routes.Status
Check '路由页含现有挂载 chat' ($routes.Body -match 'chat')
$routesToken = Get-Token $routes.Body

$saveForm = "Id=notes&Title=$(Encode '测试挂载')&Route=/notes&Kind=static&Order=150&Enabled=true&__RequestVerificationToken=$(Encode $routesToken)"
$saved = Post-Form $client '/admin/routes?handler=Save' $saveForm
Check '新增挂载成功' ($saved.Status -eq 200 -and $saved.Body -match '已保存') $saved.Status

$notes = Get-Page $client '/notes/'
Check '新挂载立即可访问（无需重启）' ($notes.Status -eq 200 -and $notes.Body -match '挂载点已创建') "$($notes.Status)"

# 停用 → 404
$toggleToken = Get-Token (Get-Page $client '/admin/routes').Body
$disabled = Post-Form $client '/admin/routes?handler=Toggle' "id=notes&__RequestVerificationToken=$(Encode $toggleToken)"
Check '停用挂载' ($disabled.Status -eq 200 -and $disabled.Body -match '已停用') $disabled.Status
$afterDisable = Get-Page $client '/notes/'
Check '停用后访问 404' ($afterDisable.Status -eq 404) "$($afterDisable.Status)"

# 启用 → 200
$toggleToken2 = Get-Token (Get-Page $client '/admin/routes').Body
$enabled = Post-Form $client '/admin/routes?handler=Toggle' "id=notes&__RequestVerificationToken=$(Encode $toggleToken2)"
Check '重新启用挂载' ($enabled.Status -eq 200 -and $enabled.Body -match '已启用') $enabled.Status

# ---- 仅本机限制：伪造 Cloudflare 头 ----
$tunneled = Get-Page $client '/admin/routes' @{ 'Cf-Connecting-IP' = '203.0.113.9'; 'CF-Ray' = 'test' }
Check '带 CF 头访问管理页被 403' ($tunneled.Status -eq 403) "$($tunneled.Status)"

# ---- 清理测试挂载 ----
$deleteToken = Get-Token (Get-Page $client '/admin/routes').Body
$deleted = Post-Form $client '/admin/routes?handler=Delete' "id=notes&__RequestVerificationToken=$(Encode $deleteToken)"
Check '删除挂载' ($deleted.Status -eq 200 -and $deleted.Body -match '已删除') $deleted.Status
$afterDelete = Get-Page $client '/notes/'
Check '删除后访问 404' ($afterDelete.Status -eq 404) "$($afterDelete.Status)"

Write-Host "`n通过 $script:pass 项，失败 $script:fail 项`n" -ForegroundColor ($(if ($script:fail -eq 0) { 'Green' } else { 'Red' }))
if ($script:fail -gt 0) { exit 1 }
