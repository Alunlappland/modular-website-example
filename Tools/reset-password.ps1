# 重置某个账号的密码（忘记密码时在服务器本机用）
# 用法：powershell -ExecutionPolicy Bypass -File .\Tools\reset-password.ps1 -UserName manifld
[CmdletBinding()]
param(
    [string]$UserName = 'manifld',
    [string]$Password
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$db = Join-Path $root 'Main\database\genkan.db'

if (-not (Test-Path $db)) {
    throw "找不到数据库：$db"
}

$sqlite = Get-Command sqlite3.exe -ErrorAction SilentlyContinue
if (-not $sqlite) {
    throw '需要 sqlite3：scoop install sqlite'
}

$exists = & sqlite3 $db "select count(*) from users where name='$UserName';"
if ($exists.Trim() -ne '1') {
    throw "找不到账号：$UserName"
}

if ([string]::IsNullOrWhiteSpace($Password)) {
    $secure = Read-Host "给 $UserName 设置新密码（至少 10 位）" -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { $Password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

if ($Password.Length -lt 10) {
    throw '密码至少 10 位。'
}

$iterations = 210000
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
$salt = New-Object byte[] 16
$rng.GetBytes($salt)
$derive = [Security.Cryptography.Rfc2898DeriveBytes]::new(
    $Password, $salt, $iterations, [Security.Cryptography.HashAlgorithmName]::SHA256)
$hash = $derive.GetBytes(32)

$saltB64 = [Convert]::ToBase64String($salt)
$hashB64 = [Convert]::ToBase64String($hash)

& sqlite3 $db "update users set pwd_salt='$saltB64', pwd_hash='$hashB64', pwd_iter=$iterations where name='$UserName';"

Write-Host "✓ 已重置 $UserName 的密码" -ForegroundColor Green
Write-Host '  登录后请到 /me 页面自己改掉。'
