# M3 端到端测试：上传、下载、分享链接、收件箱、服务器文件、删除
# 前提：数据库干净；备份原库后运行，结束后恢复
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
    return [pscustomobject]@{ Status = [int]$response.StatusCode; Body = $body; Location = "$($response.Headers.Location)" }
}

function Post-Form($client, [string]$Path, [string]$Form) {
    $content = New-Object System.Net.Http.StringContent($Form, [Text.Encoding]::UTF8, 'application/x-www-form-urlencoded')
    $response = $client.PostAsync($Path, $content).GetAwaiter().GetResult()
    $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    return [pscustomobject]@{ Status = [int]$response.StatusCode; Body = $body }
}

function Post-File($client, [string]$Path, [string]$FileName, [string]$Text, [string]$Token, [string]$Password = '') {
    $form = [System.Net.Http.MultipartFormDataContent]::new()
    $bytes = [Text.Encoding]::UTF8.GetBytes($Text)
    $fileContent = [System.Net.Http.ByteArrayContent]::new($bytes)
    $fileContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('text/plain')
    $form.Add($fileContent, 'Upload', $FileName)
    $form.Add((New-Object System.Net.Http.StringContent($Token)), '__RequestVerificationToken')
    if ($Password) { $form.Add((New-Object System.Net.Http.StringContent($Password)), 'Password') }

    $response = $client.PostAsync($Path, $form).GetAwaiter().GetResult()
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

Write-Host "`n== M3 端到端测试：$BaseUrl ==`n" -ForegroundColor Cyan

$admin = New-Client

$health = Get-Page $admin '/healthz'
if ($health.Body -notmatch '"initialized":false') {
    throw '数据库不是干净的：先停服务并删除 Main\database\genkan.db* 再跑。'
}

$setup = Get-Page $admin '/setup'
$token = Get-Token $setup.Body
$created = Post-Form $admin '/setup' "Display=$(Encode '测试站长')&UserName=$(Encode $UserName)&Password=$(Encode $Password)&Confirm=$(Encode $Password)&__RequestVerificationToken=$(Encode $token)"
Check '初始化管理员' ($created.Status -eq 302) $created.Status

# ---- 页面权限 ----
$guest = New-Client
$guestFiles = Get-Page $guest '/files'
Check '访客访问 /files 被要求登录' ($guestFiles.Status -eq 302 -and $guestFiles.Location -match '/login') "$($guestFiles.Status)"

$filesPage = Get-Page $admin '/files'
Check '文件页 200' ($filesPage.Status -eq 200) $filesPage.Status
$csrf = Get-Token $filesPage.Body

# ---- 上传 ----
$content = "玄关 M3 测试文件`n$(Get-Date -Format o)"
$uploaded = Post-File $admin '/files?handler=Upload' 'hello.txt' $content $csrf
Check '上传文件 200' ($uploaded.Status -eq 200) $uploaded.Status
Check '列表出现文件' ($uploaded.Body -match 'hello\.txt') ''

$fileId = [regex]::Match($uploaded.Body, '/files\?handler=Download&id=([a-f0-9]+)').Groups[1].Value
Check '拿到文件 ID' ($fileId.Length -eq 32) $fileId

# ---- 下载 ----
$download = $admin.GetAsync("/files?handler=Download&id=$fileId").GetAwaiter().GetResult()
$downloadedText = $download.Content.ReadAsStringAsync().GetAwaiter().GetResult()
Check '下载内容一致' ([int]$download.StatusCode -eq 200 -and $downloadedText -eq $content) "$([int]$download.StatusCode)"

# ---- 分享链接 ----
$sharePage = Get-Page $admin '/files'
$shareToken = Get-Token $sharePage.Body
$shared = Post-Form $admin '/files?handler=Share' "id=$fileId&ShareDays=7&__RequestVerificationToken=$(Encode $shareToken)"
Check '生成分享链接' ($shared.Status -eq 200 -and $shared.Body -match '/s/[a-f0-9]{32}') ''
$sharePath = [regex]::Match($shared.Body, '/s/[a-f0-9]{32}').Value

$guestShare = Get-Page $guest $sharePath
Check '访客打开分享页 200' ($guestShare.Status -eq 200) $guestShare.Status
$guestShareToken = Get-Token $guestShare.Body
$guestDownload = Post-Form $guest "${sharePath}?handler=Download" "__RequestVerificationToken=$(Encode $guestShareToken)"
Check '访客通过分享下载 200' ($guestDownload.Status -eq 200 -and $guestDownload.Body -eq $content) "$($guestDownload.Status)"

# ---- 收件箱 ----
$dropPage = Get-Page $admin '/files'
$dropToken = Get-Token $dropPage.Body
$drop = Post-Form $admin '/files?handler=Drop' "ShareDays=3&__RequestVerificationToken=$(Encode $dropToken)"
Check '生成收件箱链接' ($drop.Status -eq 200 -and $drop.Body -match '/d/[a-f0-9]{32}') ''
$dropPath = [regex]::Match($drop.Body, '/d/[a-f0-9]{32}').Value

$guestDrop = Get-Page $guest $dropPath
Check '访客打开收件箱 200' ($guestDrop.Status -eq 200) $guestDrop.Status
$guestDropToken = Get-Token $guestDrop.Body
$dropUpload = Post-File $guest "${dropPath}?handler=Upload" 'from-guest.txt' 'guest payload' $guestDropToken
Check '访客投递成功' ($dropUpload.Status -eq 200 -and $dropUpload.Body -match '已收到') $dropUpload.Status

$inboxDirs = Get-ChildItem "C:\Users\manifld\website\Main\data\inbox" -Directory -ErrorAction SilentlyContinue
$hashedDirs = $inboxDirs | Where-Object { $_.Name -match '^[a-f0-9]{32}$' }
$inboxFile = Get-ChildItem "C:\Users\manifld\website\Main\data\inbox" -Recurse -Filter 'from-guest.txt' -ErrorAction SilentlyContinue
Check '收件箱目录名为哈希' ($hashedDirs.Count -ge 1) "$($hashedDirs.Count)"
Check '文件落在收件箱' ($null -ne $inboxFile) ''

# ---- 服务器文件 ----
$server = Get-Page $admin '/files/server'
Check '服务器文件页 200' ($server.Status -eq 200) $server.Status
Check '展示白名单目录' ($server.Body -match 'Documents|Downloads') ''

# ---- 删除 ----
$deletePage = Get-Page $admin '/files'
$deleteToken = Get-Token $deletePage.Body
$deleted = Post-Form $admin '/files?handler=Delete' "id=$fileId&__RequestVerificationToken=$(Encode $deleteToken)"
Check '删除文件 200' ($deleted.Status -eq 200 -and $deleted.Body -notmatch [regex]::Escape("/files?handler=Download&id=$fileId")) $deleted.Status
$afterDelete = $admin.GetAsync("/files?handler=Download&id=$fileId").GetAwaiter().GetResult()
Check '删除后下载 404' ([int]$afterDelete.StatusCode -eq 404) "$([int]$afterDelete.StatusCode)"

Write-Host "`n通过 $script:pass 项，失败 $script:fail 项`n" -ForegroundColor ($(if ($script:fail -eq 0) { 'Green' } else { 'Red' }))
if ($script:fail -gt 0) { exit 1 }
