# M2 端到端测试：博客写作、渲染、可见性、反应、评论、行级注释、预览
# 前提：数据库干净（首次运行会走安装向导），Main\document\blog 为空
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
    return [pscustomobject]@{ Status = [int]$response.StatusCode; Location = "$($response.Headers.Location)"; Body = $body }
}

function Post-Form($client, [string]$Path, [string]$Form) {
    $content = New-Object System.Net.Http.StringContent($Form, [Text.Encoding]::UTF8, 'application/x-www-form-urlencoded')
    $response = $client.PostAsync($Path, $content).GetAwaiter().GetResult()
    $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    return [pscustomobject]@{ Status = [int]$response.StatusCode; Location = "$($response.Headers.Location)"; Body = $body }
}

function Post-Json($client, [string]$Path, [string]$Json, [string]$Csrf) {
    $request = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Post, $Path)
    $request.Headers.TryAddWithoutValidation('RequestVerificationToken', $Csrf) | Out-Null
    $request.Content = New-Object System.Net.Http.StringContent($Json, [Text.Encoding]::UTF8, 'application/json')
    $response = $client.SendAsync($request).GetAwaiter().GetResult()
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

function Get-Csrf([string]$Html) {
    return [regex]::Match($Html, 'name="csrf" content="([^"]+)"').Groups[1].Value
}

function Encode([string]$Value) { return [System.Net.WebUtility]::UrlEncode($Value) }

Write-Host "`n== M2 端到端测试：$BaseUrl ==`n" -ForegroundColor Cyan

$admin = New-Client

# ---- 初始化 ----
$health = Get-Page $admin '/healthz'
if ($health.Body -notmatch '"initialized":false') {
    throw '数据库不是干净的：先停服务并删除 Main\database\genkan.db* 再跑。'
}

$setup = Get-Page $admin '/setup'
$token = Get-Token $setup.Body
$created = Post-Form $admin '/setup' "Display=$(Encode '测试站长')&UserName=$(Encode $UserName)&Password=$(Encode $Password)&Confirm=$(Encode $Password)&__RequestVerificationToken=$(Encode $token)"
Check '初始化管理员' ($created.Status -eq 302) $created.Status

# ---- 写文章 ----
$writePage = Get-Page $admin '/write'
Check '写作页 200' ($writePage.Status -eq 200) $writePage.Status
$writeToken = Get-Token $writePage.Body

$body = "# 第一篇`n`n这是**测试**正文。`n`n- 列表项一`n- 列表项二`n`n``````powershell`nGet-Date`n``````"
$saveForm = "Slug=hello-world&Title=$(Encode '第一篇文章')&Summary=$(Encode '测试摘要')&Tags=测试, 生活&Visibility=public&Draft=false&Body=$(Encode $body)&__RequestVerificationToken=$(Encode $writeToken)"
$saved = Post-Form $admin '/write' $saveForm
Check '保存文章 302' ($saved.Status -eq 302) "$($saved.Status) $($saved.Location)"

$list = Get-Page $admin '/blog'
Check '博客列表 200' ($list.Status -eq 200) $list.Status
Check '列表含文章标题' ($list.Body -match '第一篇文章')

$post = Get-Page $admin '/blog/hello-world'
Check '文章页 200' ($post.Status -eq 200) $post.Status
Check '正文渲染出 h1' ($post.Body -match '<h1')
Check '正文渲染出列表' ($post.Body -match '<li>')
Check '正文渲染出代码块' ($post.Body -match '<pre>')
Check '块带 data-block' ($post.Body -match 'data-block="0"')
$csrf = Get-Csrf $post.Body
Check '拿到 CSRF 令牌' ($csrf.Length -gt 20)

# ---- 访客视角 ----
$guest = New-Client
$guestList = Get-Page $guest '/blog'
Check '访客可看博客列表' ($guestList.Status -eq 200 -and $guestList.Body -match '第一篇文章')
$guestPost = Get-Page $guest '/blog/hello-world'
Check '访客可看公开文章' ($guestPost.Status -eq 200)

# ---- 私密 + 草稿 ----
$writeToken2 = Get-Token (Get-Page $admin '/write').Body
$privateForm = "Slug=secret-post&Title=$(Encode '私密文章')&Summary=x&Tags=&Visibility=private&Draft=true&Body=$(Encode 'only for me')&__RequestVerificationToken=$(Encode $writeToken2)"
$savedPrivate = Post-Form $admin '/write' $privateForm
Check '保存私密草稿 302' ($savedPrivate.Status -eq 302) $savedPrivate.Status

$guestPrivate = Get-Page $guest '/blog/secret-post'
Check '访客看不到私密草稿（404）' ($guestPrivate.Status -eq 404) "$($guestPrivate.Status)"
$adminPrivate = Get-Page $admin '/blog/secret-post'
Check '站长能看到私密草稿' ($adminPrivate.Status -eq 200) "$($adminPrivate.Status)"

# ---- 反应 ----
$reaction = Post-Json $admin '/api/blog/hello-world/react' '{"kind":"like"}' $csrf
Check '点赞成功' ($reaction.Status -eq 200 -and $reaction.Body -match '"likes":1') $reaction.Body
$reactionAgain = Post-Json $admin '/api/blog/hello-world/react' '{"kind":"like"}' $csrf
Check '同指纹重复点赞只计一次' ($reactionAgain.Body -match '"likes":1') $reactionAgain.Body
$dislike = Post-Json $admin '/api/blog/hello-world/react' '{"kind":"dislike"}' $csrf
Check '点踩成功' ($dislike.Body -match '"dislikes":1') $dislike.Body

# ---- 评论（访客 → 待审）----
$commentToken = Get-Token (Get-Page $guest '/blog/hello-world').Body
$comment = Post-Form $guest "/blog/hello-world?handler=Comment" "CommentAuthor=$(Encode '路人甲')&CommentBody=$(Encode '写得不错')&__RequestVerificationToken=$(Encode $commentToken)"
Check '访客评论提交 302' ($comment.Status -eq 302) $comment.Status

$adminPost = Get-Page $admin '/blog/hello-world'
Check '站长能看到待审评论' ($adminPost.Body -match '路人甲' -and $adminPost.Body -match '待审|pending|通过')
$guestPost2 = Get-Page $guest '/blog/hello-world'
Check '访客看不到待审评论' ($guestPost2.Body -notmatch '路人甲')

# 审核通过
$approveToken = Get-Token $adminPost.Body
$commentId = [regex]::Match($adminPost.Body, 'name="id" value="(\d+)"').Groups[1].Value
$approved = Post-Form $admin "/blog/hello-world?handler=Approve" "id=$commentId&__RequestVerificationToken=$(Encode $approveToken)"
Check '审核通过 302' ($approved.Status -eq 302) "$($approved.Status) $commentId"
$guestPost3 = Get-Page $guest '/blog/hello-world'
Check '通过后访客可见' ($guestPost3.Body -match '路人甲')

# ---- 行级注释 ----
$anno = Post-Json $admin '/api/blog/hello-world/annotations' '{"blockIndex":0,"blockHash":"abc123","author":"批注人","body":"这一块有点意思"}' $csrf
Check '站长注释直接通过' ($anno.Status -eq 200 -and $anno.Body -match '"status":"approved"') $anno.Body

$guestCsrf = Get-Csrf (Get-Page $guest '/blog/hello-world').Body
$guestAnno = Post-Json $guest '/api/blog/hello-world/annotations' '{"blockIndex":1,"blockHash":"def456","author":"路人乙","body":"访客的注释"}' $guestCsrf
Check '访客注释进入待审' ($guestAnno.Status -eq 200 -and $guestAnno.Body -match '"status":"pending"') $guestAnno.Body

$annoList = Get-Page $admin '/api/blog/hello-world/annotations'
Check '注释接口返回列表' ($annoList.Status -eq 200 -and $annoList.Body -match '这一块有点意思') $annoList.Body

# ---- 预览接口 ----
$preview = Post-Json $admin '/api/blog/preview' '{"markdown":"## 预览标题"}' $csrf
Check '预览接口 200' ($preview.Status -eq 200 -and $preview.Body -match '<h2') $preview.Body

Write-Host "`n通过 $script:pass 项，失败 $script:fail 项`n" -ForegroundColor ($(if ($script:fail -eq 0) { 'Green' } else { 'Red' }))
if ($script:fail -gt 0) { exit 1 }
