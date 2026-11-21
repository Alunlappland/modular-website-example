namespace Genkan.Console;

/// <summary>
/// 命令提示库：只描述「怎么用」，不含任何执行逻辑。
/// 这个模块按设计出厂关闭执行能力——页面和接口都只返回文本。
/// </summary>
public static class CommandHints
{
    public sealed record Hint(
        string Name,
        string Category,
        string Usage,
        string Summary,
        string[] Examples,
        string[] Notes,
        string[] Tags);

    public static IReadOnlyList<Hint> All { get; } = new List<Hint>
    {
        new("ls / dir", "文件", "ls [-alh] [路径]　/　dir [/a] [路径]",
            "列出目录内容。Windows 下 PowerShell 用 Get-ChildItem（别名 ls/dir）。",
            new[] { "ls -alh /var/log", "dir C:\\Users", "Get-ChildItem -Force" },
            new[] { "PowerShell 里 ls、dir、gci 是同一个 cmdlet 的别名。" }, new[] { "list", "目录" }),

        new("cd", "文件", "cd <路径>",
            "切换当前目录。PowerShell 中 cd 也可跨盘符（Set-Location）。",
            new[] { "cd /etc/nginx", "cd C:\\Users\\manifld\\website", "cd .." },
            Array.Empty<string>(), new[] { "切换", "目录" }),

        new("cp / copy", "文件", "cp [-r] <源> <目标>　/　Copy-Item <源> <目标> -Recurse",
            "复制文件或目录。",
            new[] { "cp -r src backup/", "Copy-Item .\\a.txt .\\b.txt" },
            new[] { "覆盖前不会提示，谨慎使用 -f。" }, new[] { "复制" }),

        new("mv / move", "文件", "mv <源> <目标>　/　Move-Item <源> <目标>",
            "移动或重命名。",
            new[] { "mv old.md new.md", "Move-Item .\\a .\\b" },
            Array.Empty<string>(), new[] { "移动", "重命名" }),

        new("rm / del", "文件", "rm [-rf] <路径>　/　Remove-Item <路径> -Recurse -Force",
            "删除文件或目录。不可恢复，务必确认路径。",
            new[] { "rm -rf ./cache", "Remove-Item .\\tmp -Recurse -Force" },
            new[] { "本站的文件模块不会调用它；本页只给提示。" }, new[] { "删除" }),

        new("mkdir", "文件", "mkdir [-p] <目录>　/　New-Item -ItemType Directory <目录>",
            "创建目录，-p 会连同父目录一起建。",
            new[] { "mkdir -p a/b/c", "New-Item -ItemType Directory .\\logs" },
            Array.Empty<string>(), new[] { "目录" }),

        new("find / findstr", "文件", "find <路径> -name <模式>　/　findstr /s /i <文本> <文件…>",
            "按名字找文件 / 在文件里找文本。",
            new[] { "find . -name \"*.log\" -mtime -7", "findstr /s /i error *.log" },
            new[] { "PowerShell 里对应 Get-ChildItem -Recurse -Filter 和 Select-String。" }, new[] { "搜索" }),

        new("grep / Select-String", "文本", "grep [-rn] <模式> <文件…>　/　Select-String -Pattern <模式> -Path <文件…>",
            "按正则搜索文本并打印匹配行。",
            new[] { "grep -rn \"TODO\" src/", "Select-String -Pattern \"ERR\" -Path .\\logs\\*.log" },
            Array.Empty<string>(), new[] { "搜索", "正则" }),

        new("cat / Get-Content", "文本", "cat <文件>　/　Get-Content <文件> [-Tail 100] [-Wait]",
            "查看文件内容；-Tail 看末尾，-Wait 持续跟踪。",
            new[] { "cat /etc/hosts", "Get-Content .\\app.log -Tail 50 -Wait" },
            new[] { "大文件建议配合 less/分页工具。" }, new[] { "查看" }),

        new("head / tail", "文本", "head -n 20 <文件>　/　tail -n 50 -f <文件>",
            "看开头 / 看结尾并持续输出。",
            new[] { "head -n 20 access.log", "tail -f /var/log/syslog" },
            Array.Empty<string>(), new[] { "日志" }),

        new("ps / Get-Process", "进程", "ps aux　/　Get-Process [-Name <名字>]",
            "列出进程。",
            new[] { "ps aux | grep dotnet", "Get-Process -Name Genkan" },
            new[] { "本站仪表盘的进程列表就是读的等价数据。" }, new[] { "进程" }),

        new("kill / Stop-Process", "进程", "kill [-9] <PID>　/　Stop-Process -Id <PID> [-Force]",
            "结束进程。",
            new[] { "kill -9 12345", "Stop-Process -Id 12345 -Force" },
            new[] { "先确认 PID，误杀系统进程会掉线。" }, new[] { "进程" }),

        new("sc / Get-Service", "服务", "sc query <名字>　/　Get-Service <名字>　/　Start-Service <名字>",
            "查询与管理 Windows 服务。",
            new[] { "sc query Genkan", "Get-Service Genkan", "Start-Service Genkan" },
            new[] { "停服务需要管理员权限。" }, new[] { "服务" }),

        new("nssm", "服务", "nssm install <服务名> <exe>　/　nssm set <服务名> <参数> <值>",
            "把普通程序注册成 Windows 服务。",
            new[] { "nssm install Genkan C:\\path\\Genkan.exe", "nssm set Genkan AppDirectory C:\\path" },
            new[] { "本站就是用 nssm 注册的；见 Tools\\install-service.ps1。" }, new[] { "服务" }),

        new("netstat", "网络", "netstat -ano | findstr :<端口>",
            "查看端口占用与对应 PID。",
            new[] { "netstat -ano | findstr :8080" },
            new[] { "PowerShell 也可用 Get-NetTCPConnection -LocalPort 8080。" }, new[] { "端口" }),

        new("ipconfig / ip", "网络", "ipconfig [/all]　/　ip addr show",
            "查看本机网络配置。",
            new[] { "ipconfig /all", "ip addr show" },
            Array.Empty<string>(), new[] { "网络" }),

        new("ping", "网络", "ping [-n 4] <主机>",
            "测试连通性。",
            new[] { "ping -n 4 1.1.1.1", "ping 20061028.xyz" },
            new[] { "Windows 默认只发 4 个包，Linux 会一直发（-c 限制）。" }, new[] { "网络" }),

        new("curl", "网络", "curl [-sSL] [-o 文件] <URL>",
            "命令行 HTTP 客户端，用来测接口、下文件。",
            new[] { "curl -sSL https://example.com -o page.html", "curl -I https://nas.20061028.xyz" },
            new[] { "Windows 自带 curl.exe；PowerShell 的 curl 别名指向 Invoke-WebRequest。" }, new[] { "http" }),

        new("ssh", "远程", "ssh [-p 端口] <用户>@<主机> [-i 私钥]",
            "登录远程主机。",
            new[] { "ssh -p 22 manifld@192.168.196.228", "ssh -i ~/.ssh/id_ed25519 host" },
            new[] { "Windows 需先安装 OpenSSH 客户端。" }, new[] { "远程" }),

        new("scp", "远程", "scp [-r] <源> <用户>@<主机>:<目标>",
            "在本地和远程之间复制文件。",
            new[] { "scp .\\a.zip manifld@host:/home/manifld/", "scp -r host:/data ./backup" },
            Array.Empty<string>(), new[] { "远程", "复制" }),

        new("rsync", "远程", "rsync -av --delete <源>/ <目标>/",
            "增量同步目录，最常用的备份/部署工具。",
            new[] { "rsync -av ./src/ host:/srv/src/", "rsync -av --dry-run a/ b/" },
            new[] { "源路径末尾的斜杠决定是同步「目录内容」还是「目录本身」。" }, new[] { "同步" }),

        new("robocopy", "文件", "robocopy <源> <目标> /MIR /XD node_modules",
            "Windows 的目录镜像工具，支持排除项。",
            new[] { "robocopy .\\src \\\\host\\share\\src /MIR /XD bin obj" },
            new[] { "退出码 0-7 都算成功，≥8 才是失败。" }, new[] { "同步" }),

        new("tar", "归档", "tar -czf <包.tar.gz> <目录>　/　tar -xzf <包.tar.gz>",
            "打包 / 解包。",
            new[] { "tar -czf backup.tar.gz ./data", "tar -xzf backup.tar.gz -C /tmp" },
            Array.Empty<string>(), new[] { "压缩" }),

        new("zip / unzip", "归档", "zip -r <包.zip> <目录>　/　unzip <包.zip> -d <目录>",
            "ZIP 压缩与解压。",
            new[] { "zip -r site.zip ./Main", "unzip site.zip -d ./restore" },
            new[] { "Windows 可用 Compress-Archive / Expand-Archive。" }, new[] { "压缩" }),

        new("git", "开发", "git <子命令>",
            "版本控制。常用：status / add / commit / pull / push / log / diff。",
            new[] { "git status", "git add -A && git commit -m \"msg\"", "git push origin main" },
            new[] { "本站的提交记录见 DEVLOG.md。" }, new[] { "版本控制" }),

        new("dotnet", "开发", "dotnet <命令>",
            "构建、运行、发布 .NET 项目。",
            new[] { "dotnet build", "dotnet publish -c Release -o out" },
            new[] { "本项目用的是 .NET 10。" }, new[] { "构建" }),

        new("kopia", "备份", "kopia snapshot create <路径> --tags key:value",
            "快照备份，可增量、可加密。",
            new[] { "kopia snapshot create /data --tags app:site", "kopia snapshot list --all" },
            new[] { "本站的备份模块包装了它；标签必须写成 key:value。" }, new[] { "备份" }),

        new("rclone", "备份", "rclone sync <源> <远端:路径> [--dry-run]",
            "把目录同步到 S3/R2/网盘等远端。",
            new[] { "rclone sync H:\\restore r2:bucket/kopia --fast-list", "rclone lsd r2:bucket" },
            new[] { "桶级 token 通常没有 ListBuckets 权限，测桶而不是账号根。" }, new[] { "同步", "网盘" }),

        new("syncthing", "同步", "syncthing serve --home <目录> --no-browser",
            "点对点文件同步；设备间直连，不经中心服务器。",
            new[] { "syncthing serve --home C:\\data\\syncthing" },
            new[] { "只同步源码时在目录里放 .stignore 排除构建产物。" }, new[] { "同步" }),

        new("winget / scoop", "软件", "winget install <包>　/　scoop install <包>",
            "Windows 包管理器。",
            new[] { "scoop install rclone", "winget install OpenSSH" },
            new[] { "scoop 装在用户目录，不需要管理员。" }, new[] { "安装" }),

        new("Get-ChildItem", "PowerShell", "Get-ChildItem [-Recurse] [-Filter <模式>]",
            "列出目录内容（ls 的完整写法）。",
            new[] { "Get-ChildItem -Recurse -Filter *.cs" },
            Array.Empty<string>(), new[] { "powershell" }),

        new("Get-Content", "PowerShell", "Get-Content <文件> -Tail <n> -Wait",
            "读取文件，可跟踪新增内容。",
            new[] { "Get-Content .\\app.log -Tail 100 -Wait" },
            Array.Empty<string>(), new[] { "powershell", "日志" }),

        new("Select-String", "PowerShell", "Select-String -Pattern <正则> -Path <文件…>",
            "PowerShell 版的 grep。",
            new[] { "Select-String -Pattern \"error\" -Path .\\logs\\*.log" },
            Array.Empty<string>(), new[] { "powershell" }),

        new("Test-NetConnection", "PowerShell", "Test-NetConnection <主机> -Port <端口>",
            "测试目标端口是否可达。",
            new[] { "Test-NetConnection 20061028.xyz -Port 443" },
            Array.Empty<string>(), new[] { "powershell", "网络" }),

        new("Invoke-WebRequest", "PowerShell", "Invoke-WebRequest <URL> -OutFile <文件>",
            "下载文件；注意在部分受限会话里会读不到输出。",
            new[] { "Invoke-WebRequest https://example.com -OutFile a.html" },
            new[] { "遇到控制台缓冲相关报错时改用 curl.exe。" }, new[] { "powershell", "http" }),

        new("icacls", "权限", "icacls <路径> /grant <用户>:(权限)",
            "查看与修改 NTFS 权限。",
            new[] { "icacls C:\\data /grant \"Users:(OI)(CI)M\"" },
            new[] { "改错权限可能导致服务读不到文件。" }, new[] { "权限" }),

        new("schtasks", "计划", "schtasks /create /tn <名字> /tr <命令> /sc daily /st 12:00",
            "创建 Windows 计划任务。",
            new[] { "schtasks /query /tn GenkanBackup" },
            new[] { "本站的每日备份在进程内调度，不依赖它。" }, new[] { "定时" }),
    };

    public static IReadOnlyList<Hint> Search(string? query, int limit = 30)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return All.Take(limit).ToList();
        }

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return All
            .Select(hint => new { Hint = hint, Score = Score(hint, terms) })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Hint.Name.Length)
            .Take(limit)
            .Select(item => item.Hint)
            .ToList();
    }

    private static int Score(Hint hint, string[] terms)
    {
        var score = 0;

        foreach (var term in terms)
        {
            if (hint.Name.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 10;
            if (hint.Category.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 4;
            if (hint.Usage.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 6;
            if (hint.Summary.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 3;
            if (hint.Tags.Any(tag => tag.Contains(term, StringComparison.OrdinalIgnoreCase))) score += 5;
            if (hint.Examples.Any(example => example.Contains(term, StringComparison.OrdinalIgnoreCase))) score += 2;
        }

        return score;
    }
}
