# 开发日志 DEVLOG

> 规则：每次动手都追加一条，写清 **时间 / 做到哪 / 遇到什么问题 / 怎么解决 / 下一步**。
> 时间统一用本机时间（Asia/Shanghai，UTC+8）。倒序排列，最新在上。

---

## 2026-11-21 02:10 — M1：系统仪表盘 + 路由/挂载管理

**做到哪**
- `/dashboard`：仿 Windows 任务管理器——CPU、内存、网络吞吐、进程数四张卡 + 磁盘用量条 +
  进程 Top8（内存排序）+ 关键服务状态；前端每 2 秒拉 `/api/sys` 局部刷新。
- 系统信息全部零依赖：`GetSystemTimes` / `GlobalMemoryStatusEx` 走 kernel32 P/Invoke，
  磁盘用 `DriveInfo`，网络用 `NetworkInterface`，服务状态用 `sc query`（10 秒缓存）。
- `/admin/routes`：挂载增删改、启停、按 `static / iframe / link` 三种类型；
  保存后**立即生效**——挂载从"启动时 Map"改成"每次请求按当前注册表解析"。
- **仅本机**：带 `Cf-Connecting-IP` / `CF-Ray` / `Cf-Visitor` 的请求直接 403（隧道来的就是）。
- 新增 `Tools\e2e-m1.ps1`：**17 项全部通过**（含热生效、停用 404、伪造 CF 头 403、删除清理）；
  `smoke-test.ps1` 5 项通过。

**遇到的问题**
1. Razor 的 `PageModel` 已有 `Url` 属性，自定义同名属性触发 CS0108 警告 → 改名 `TargetUrl`。
2. `ExtendStore` 里 `Path.Combine` 的可空警告 → 先取本地非空变量再拼。
3. PS 5.1 的 `Start-Process` 没有 `-Environment` 参数 → 测试时改用后台作业启动服务。

**下一步**
- M2：博客（Markdown 渲染、文章管理、访客只读、点赞点踩、行级注释、评论 + Qwen 审核）。

---

## 2026-11-21 01:20 — M0.2：登录、账号、双端布局、主题、SQLite

**做到哪**
- 数据库：SQLite（`Microsoft.Data.Sqlite` 10 + `Dapper`），`users` / `audit` / `settings` 三张表，
  启动幂等建表，WAL 模式；库文件 `Main\database\genkan.db`。
- 认证：PBKDF2-SHA256（210,000 次）+ 随机盐；Cookie 认证 7 天滑动过期；
  登录失败同 IP+账号 5 次锁 5 分钟；`genkan.auth` Cookie（HttpOnly、SameSite=Lax、随请求协议决定 Secure）。
- 首次启动强制进 `/setup` 建管理员（用户名格式/密码长度/二次确认校验），建完自动登录。
- 页面：`/`（玄关）、`/login`、`/setup`、`/me`、`/logout`。
- 双端布局：桌面 = 右侧固定竖导航；手机 = 顶部标题栏 + 底部标签栏（同一套 Layout，CSS 断点切换，含安全区内边距）。
- 主题：表 / 里 / 中性三套令牌，`/theme` 写 Cookie 记一年；「里」的紫调已生效。
- 权限：`Permissions` 角色表（admin/member/guest）；`/chat/` 按 `chat.use` 拦截——未登录跳登录，无权限 403。
- 测试：新增 `Tools\e2e-test.ps1`，**21 项全部通过**（初始化→登录→权限→主题→退出→错误密码→重新登录）；
  `smoke-test.ps1` 改为兼容 302，5 项通过。
- 实测空闲内存 **74MB**（目标 <150MB），产物 33.3MB（含 SQLite 原生库）。

**遇到的问题 / 解决**
1. Dapper + `record` 材质化失败：`MatchNamesWithUnderscores` 只映射属性，不映射 record 构造参数名
   （报 `pwd_algo` 之类签名不匹配）→ `UserRecord` 改为可写类 + 无参构造。
2. Razor Pages 全部 404，日志 `No action descriptors found`：运行时把 `RootDirectory` 改成
   `/Main/src/Genkan.App/Pages`，与编译期页面路径 `/Pages/...` 对不上 → 去掉覆盖，用默认根目录。
3. 中文全部输出成 `&#x6D4B;`：Razor 默认编码器只放行 Basic Latin →
   配置 `WebEncoderOptions.TextEncoderSettings = UnicodeRanges.All`。
4. `dotnet publish` 报错但看不到原因：正在运行的 `Genkan.exe` 占用了输出文件 → 先停进程再构建。
5. PowerShell 变量 `$home` 与只读 `$HOME` 冲突 → 改名 `$homePage`。
6. `Microsoft.Data.Sqlite` 带的 `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 有高危漏洞公告
   （GHSA-2m69-gcr7-jv3q）→ 显式引用 2.1.14，重新构建后无告警。

**下一步**
- M1：`/admin/routes` 路由与挂载管理（仅本机）、Windows 风格仪表盘（CPU/内存/磁盘/进程/服务）。

---

## 2026-11-21 00:35 — 推送改用 SSH；网盘定为 R2

**做到哪**
- 你两台机器的推送都失败，定位如下：
  - GitHub：`Empty reply from server` —— 直连 443 被干扰，不是账号问题。
  - Gitee：`Incorrect username or password (access token)` —— Gitee 不接受账户密码，只认私人令牌。
  - 第二次运行卡住：停在 GitHub 那一步等凭据弹窗（非交互环境下等不到）。
- 方案改为 **SSH**：生成 ed25519 密钥 `C:\Users\manifld\.ssh\genkan_ed25519`（无口令），
  `~/.ssh/config` 里把 `github.com` 指到 `ssh.github.com:443`（绕开干扰），Gitee 走 22。
  远端已改为 `git@github.com:...` / `git@gitee.com:...`。
- 连通性已验证：两个 SSH 端点都能建立连接，当前返回 `Permission denied (publickey)` —— 只差把公钥加进账号。
- 读了 `回复3.md`：网盘决定 **先用 Cloudflare R2 试水**；站点约 1GB，落在 10GB 免费额度内；
  个人数据几十 GB 暂不上云。已写入 `Docs/CLOUD-BACKUP.md` 第 0 节与 v0.3 增补第 11 节。
- 运维窗口记录：学校供电/网络 24h；12:00 与 18:00 后通常有人 —— 备份仍定 12:00。

**遇到的问题 / 解决**
- SSH 公钥需要你在 GitHub、Gitee 各粘贴一次（一次性动作），之后推送不再需要密码或令牌。
- Cloudflare R2 的账单需要可用的国际支付方式（Visa/Mastercard），银联单币卡可能绑不上，需实测。

**下一步**
- 你加完公钥 → 跑 `Tools\push.ps1` → 我确认远端同步。
- 我继续 M0.2：SQLite + 登录 + 双端布局 + 主题框架。

---

## 2026-11-20 23:50 — M0.1：站点跑通（玄关 + /chat + 日志 + 服务脚本）

**做到哪**
- 读了 `回复2.md`（你写的是「回复1.md」，实际文件名是 `回复2.md`），把新增要求整理成
  `Docs\PLAN-v0.3-ADDENDUM.md`：玄关定义、里主题（私密背景/特效/字体/图标）、Qwen 审核、
  日志 90 天进备份、错误自诊断、命令提示器、Docs 鉴权、服务器文件沙箱、chat 多端同步方案、
  dsh 代理站长、网盘调研结论。
- 代码：`Main\src\Genkan.App`（ASP.NET Core 10 + Serilog），实现
  玄关欢迎页、Extend 挂载发现、`/chat/` 静态挂载（尾斜杠重定向 + SPA 回退）、`/healthz`、`/api/extends`。
- 前端：`Main\website\pages\home.html` + `assets\css\home.css` + `assets\js\home.js`
  （表/里主题切换、在线状态灯、装修中提示、移动端单列）。
- 脚本：`Tools\build.ps1`、`dev.ps1`、`install-service.ps1`、`uninstall-service.ps1`、`smoke-test.ps1`。
- 构建成功（产物 0.7MB），冒烟测试 4/4 通过，日志落 `Main\logs\app-20261120.log`。

**遇到的问题**
1. PowerShell 执行策略禁止运行脚本（`UnauthorizedAccess`）→ 用 `-ExecutionPolicy Bypass` 运行；
   已写进使用说明。
2. 脚本里的中文导致 PS 5.1 解析报错：`write` 写出的是无 BOM UTF-8，PS 5.1 按 ANSI 读。
   → 全部 `.ps1` 转成 **UTF-8 with BOM**，验证 BOM=True。
3. `git push` 失败：本机没有 GitHub/Gitee 凭据，GCM 非交互模式无法弹窗。
   → 需要你在自己终端里交互授权一次，之后我就能推。
4. 沙箱下 NuGet 读 `%APPDATA%\NuGet\NuGet.Config` 被拒 → `build.ps1` 支持 `GENKAN_SANDBOX=1`
   时重定向 APPDATA/LOCALAPPDATA。
5. 网盘方案调研交给子 agent（cloud-backup-scout）完成 → `Docs\CLOUD-BACKUP.md`（215 行）。

**下一步**
- M0.2：SQLite + 登录 + 双端 Razor 布局 + 主题框架（表/里/中性）。
- 等你回答 `Docs\CLOUD-BACKUP.md` 第 9 节的 4 个问题，再定网盘。

---

## 2026-11-20 22:30 — v0.2 计划定稿 + 首个挂载物就位

**做到哪**
- 读完 `回复.md`，把 15 条意见逐条落进 `Docs\PLAN.md`（v0.2），v0.1 存档为 `Docs\PLAN-v0.1.md`。
- 目录重构为 `Main\`（src/document/depend/website/data/database/conf/logs/bin）+ `Extend\` + `Tools\` + `Docs\`。
- **AI 对话前端已就位**：下载官方 chat-demo v0.1.3 静态包 → 校验 sha256 → 解压到 `Extend\chat\www\`，
  清单写入 `Extend\chat\extend.json`（路由 `/chat`，权限 `chat.use`）。
- 建立 `README.md`（自我陈述）与本日志。

**遇到的问题**
1. 沙箱默认模式下 HTTPS 出网失败（schannel `SEC_E_NO_CREDENTIALS`），NuGet / GitHub 都连不上。
   - 解决：以完全访问权限重试，实测 GitHub / NuGet 均返回 200；后续联网操作合并成一次授权完成。
2. 计划里"按卷标定位 H 盘"行不通：实测 F 与 H 的卷标都叫「软件」，C/D 无标签。
   - 解决：改为**卷 GUID + 相对路径**定位大文件区（盘符变化不影响）。
3. `Invoke-WebRequest` 在沙箱里读子进程控制台会报 Win32 错误，无法用它验证本地 HTTP。
   - 解决：用原始 `TcpClient` 发 HTTP 请求验证（已确认 Kestrel 在沙箱内可正常监听回包）。

**下一步**
- 等第 16 节 5 个问题的答复（网盘、dsh-go 方式、仓库地址、API Key 策略、主题细节）。
- 答复到位后进入 M0：解决方案骨架、SQLite、登录、双端布局、主题框架。

---

## 2026-11-20 19:00 — 环境勘察 + v0.1 计划书

**做到哪**
- 摸清机器：.NET 10 SDK、kopia 0.23.1（仓库 H:\restore）、cloudflared 服务、OpenList（5244）、
  nssm（C:\regser）、空闲端口 8080、磁盘余量。
- 产出 v0.1 计划书（技术选型、模块设计、里程碑、风险）。

**遇到的问题**
- 当前会话未提权，无法代为注册服务 → 安装脚本交由管理员窗口执行。

**下一步**
- 等用户校验。
