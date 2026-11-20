# 端到端测试：初始化 / 登录 / 权限 / 主题 / 退出（需要干净数据库）
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://127.0.0.1:8080',
    [string]$UserName = 'manifld',
    [string]$Password = 'test-password-9283',
    [string]$Display = '测试站长'
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

function Invoke-Get($client, [string]$Path) {
    $response = $client.GetAsync($Path).GetAwaiter().GetResult()
    $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    return [pscustomobject]@{ Status = [int]$response.StatusCode; Location = "$($response.Headers.Location)"; Body = $body }
}

function Invoke-Post($client, [string]$Path, [string]$Form) {
    $content = New-Object System.Net.Http.StringContent($Form, [Text.Encoding]::UTF8, 'application/x-www-form-urlencoded')
    $response = $client.PostAsync($Path, $content).GetAwaiter().GetResult()
    $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    return [pscustomobject]@{ Status = [int]$response.StatusCode; Location = "$($response.Headers.Location)"; Body = $body }
}

function Check([string]$Name, [bool]$Ok, [string]$Detail = '') {
    if ($Ok) {
        $script:pass++
        Write-Host ("  ✓ {0}" -f $Name) -ForegroundColor Green
    }
    else {
        $script:fail++
        Write-Host ("  ✗ {0}  {1}" -f $Name, $Detail) -ForegroundColor Red
    }
}

function Get-Token([string]$Html) {
    $match = [regex]::Match($Html, '__RequestVerificationToken[^>]*value="([^"]+)"')
    return $match.Groups[1].Value
}

function Encode([string]$Value) { return [System.Net.WebUtility]::UrlEncode($Value) }

Write-Host "`n== 玄关端到端测试：$BaseUrl ==`n" -ForegroundColor Cyan

$anon = New-Client

$health = Invoke-Get $anon '/healthz'
Check '健康检查 200' ($health.Status -eq 200) $health.Status
Check '未初始化状态' ($health.Body -match '"initialized":false') $health.Body

$homePage = Invoke-Get $anon '/'
Check '/ 重定向到 /setup' ($homePage.Status -eq 302 -and $homePage.Location -match '/setup') "$($homePage.Status) $($homePage.Location)"

$setupPage = Invoke-Get $anon '/setup'
Check '安装页 200' ($setupPage.Status -eq 200) $setupPage.Status
$token = Get-Token $setupPage.Body
Check '拿到防伪令牌' ($token.Length -gt 20)

$form = "Display=$(Encode $Display)&UserName=$(Encode $UserName)&Password=$(Encode $Password)&Confirm=$(Encode $Password)&__RequestVerificationToken=$(Encode $token)"
$created = Invoke-Post $anon '/setup' $form
Check '创建管理员 302' ($created.Status -eq 302) "$($created.Status) $($created.Body)"

$afterSetup = Invoke-Get $anon '/'
Check '登录后首页 200' ($afterSetup.Status -eq 200) $afterSetup.Status
Check '首页显示站点名' ($afterSetup.Body -match '情绪流形的小屋')
Check '首页显示显示名' ($afterSetup.Body -match $Display)

$me = Invoke-Get $anon '/me'
Check '/me 200' ($me.Status -eq 200) $me.Status
Check '/me 显示角色 admin' ($me.Body -match 'admin')

$theme = Invoke-Get $anon '/theme?name=inner&returnUrl=/'
Check '主题切换 302' ($theme.Status -eq 302)
$themedHome = Invoke-Get $anon '/'
Check '主题生效 data-theme=inner' ($themedHome.Body -match 'data-theme="inner"')

$guest = New-Client
$guestChat = Invoke-Get $guest '/chat/'
Check '访客访问 /chat/ 被要求登录' ($guestChat.Status -eq 302 -and $guestChat.Location -match '/login') "$($guestChat.Status) $($guestChat.Location)"
$guestMe = Invoke-Get $guest '/me'
Check '访客访问 /me 被要求登录' ($guestMe.Status -eq 302)

$logoutToken = Get-Token (Invoke-Get $anon '/me').Body
$logout = Invoke-Post $anon '/logout' "__RequestVerificationToken=$(Encode $logoutToken)"
Check '退出登录 302' ($logout.Status -eq 302) $logout.Status
$afterLogout = Invoke-Get $anon '/me'
Check '退出后 /me 需要登录' ($afterLogout.Status -eq 302)

$loginPage = Invoke-Get $anon '/login'
$loginToken = Get-Token $loginPage.Body
$badLogin = Invoke-Post $anon '/login' "UserName=$(Encode $UserName)&Password=$(Encode 'wrong-password')&returnUrl=/&__RequestVerificationToken=$(Encode $loginToken)"
Check '错误密码被拒' ($badLogin.Status -eq 200 -and $badLogin.Body -match '用户名或密码不对')

$loginPage2 = Invoke-Get $anon '/login'
$loginToken2 = Get-Token $loginPage2.Body
$goodLogin = Invoke-Post $anon '/login' "UserName=$(Encode $UserName)&Password=$(Encode $Password)&returnUrl=/&__RequestVerificationToken=$(Encode $loginToken2)"
Check '正确密码登录 302' ($goodLogin.Status -eq 302) $goodLogin.Status
$me2 = Invoke-Get $anon '/me'
Check '重新登录后 /me 200' ($me2.Status -eq 200) $me2.Status
Check '上次登录时间已记录' ($me2.Body -notmatch '上次登录</dt>\s*<dd>—')

Write-Host "`n通过 $script:pass 项，失败 $script:fail 项`n" -ForegroundColor ($(if ($script:fail -eq 0) { 'Green' } else { 'Red' }))
if ($script:fail -gt 0) { exit 1 }
