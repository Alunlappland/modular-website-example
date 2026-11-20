# 冒烟测试：健康检查 + 静态资源 + 挂载（原始 TCP，不依赖 curl）
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://127.0.0.1:8080'
)

$ErrorActionPreference = 'Stop'

function Invoke-RawGet {
    param([string]$Path)

    $uri = [Uri]($BaseUrl.TrimEnd('/') + $Path)
    $client = New-Object System.Net.Sockets.TcpClient
    $client.Connect($uri.Host, $uri.Port)
    $stream = $client.GetStream()
    $request = "GET $Path HTTP/1.1`r`nHost: $($uri.Host):$($uri.Port)`r`nConnection: close`r`nAccept: */*`r`n`r`n"
    $bytes = [Text.Encoding]::ASCII.GetBytes($request)
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Flush()
    $reader = New-Object System.IO.StreamReader($stream)
    $text = $reader.ReadToEnd()
    $client.Close()
    return $text
}

# Expect 支持正则，例如 '200' 或 '200|302'
$tests = @(
    @{ Name = '健康检查'; Path = '/healthz'; Expect = '200' },
    @{ Name = '玄关首页'; Path = '/'; Expect = '200|302' },
    @{ Name = '基础样式'; Path = '/assets/css/base.css'; Expect = '200' },
    @{ Name = '对话挂载'; Path = '/chat/'; Expect = '200|302' },
    @{ Name = '对话静态资源'; Path = '/chat/static/css/main.27198ffe.css'; Expect = '200|302' }
)

$failed = 0
foreach ($t in $tests) {
    try {
        $resp = Invoke-RawGet -Path $t.Path
        $status = ($resp -split "`r`n")[0]
        if ($status -match $t.Expect) {
            Write-Host ("  ✓ {0,-14} {1,-38} {2}" -f $t.Name, $t.Path, $status) -ForegroundColor Green
        }
        else {
            Write-Host ("  ✗ {0,-14} {1,-38} {2}" -f $t.Name, $t.Path, $status) -ForegroundColor Red
            $failed++
        }
    }
    catch {
        Write-Host ("  ✗ {0,-14} {1,-38} {2}" -f $t.Name, $t.Path, $_.Exception.Message) -ForegroundColor Red
        $failed++
    }
}

if ($failed -gt 0) {
    Write-Host "`n有 $failed 项失败。" -ForegroundColor Red
    exit 1
}

Write-Host "`n全部通过。" -ForegroundColor Green
