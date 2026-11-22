# 开发日志 DEVLOG

> 规则：每次动手都追加一条，写清 **时间 / 做到哪 / 遇到什么问题 / 怎么解决 / 下一步**。
> 时间统一用本机时间（Asia/Shanghai，UTC+8）。倒序排列，最新在上。

---

## 2026-11-21 23:10 — 笔记、剪贴板、文章内搜索

- `/notes`：网页里写 Markdown 或导入 `.md`（≤2MB），文件落在 `Main\document\notes\*.md`，
  用任何编辑器都能改、能直接拷走；渲染复用博客的 Markdig 管线。
- `/clip`：跨设备文字剪贴板，存在 SQLite `clips` 表，列表里一键复制 / 删除。
- 博客文章页：`Ctrl+F` 打开页面内搜索条，高亮全部命中、Enter / Shift+Enter 跳转、Esc 关闭；
  关闭时还原原始 HTML，不污染留言锚点。
- 隔离实例验证：剪贴板保存后列表可见；笔记保存生成 `测试笔记.md` 并能正确渲染标题与正文。
---

## 2026-11-21 22:50 — AI 页增强：模型覆盖 / 停止 / 复制

- 供应商旁边可临时填模型名（按供应商记在浏览器 localStorage），不改配置文件；
  服务端 AiChatRequest.Model 优先于配置里的 model，并写进对话文件。
- 生成中「发送」变「停止」，走 AbortController；中止时保留已生成的部分内容（服务端也会落盘）。
- 每条助手回复带「复制」按钮。
- ai.js / files.js 用 node --check 做了语法校验。
---

## 2026-11-21 22:35 — 自诊断页

- DiagnosticsService：数据库（账号/文件数/体积）、系统盘与备份盘剩余、kopia 状态与上次快照年龄、
  R2 镜像就绪度、Syncthing 设备与文件夹、AI 已配 Key 家数、当天 ERR 行数。
- /diagnostics（仅管理员）：一次跑完，用「正常 / 注意 / 失败」三档展示，可重新检查。
- 隔离实例验证：匿名 302，登录后 200，七类检查都有实际数值（如「H:\ 剩余 439.4 GB」「0 / 9 家已配置 Key」）。
---

## 2026-11-21 15:45 — AI 统一前端、多供应商余额、TOTP 提权、里主题

**AI（`/ai`）**
- 一家页面，十家供应商：OpenAI 兼容（DeepSeek / OpenAI / Kimi / GLM / 通义 / 硅基流动 / OpenRouter / 本地 Ollama）、
  Anthropic、Gemini。Key 存在服务器（SecretStore 加密），浏览器只拿模型名和余额。
- 两种协议分开适配：
  - OpenAI：`POST /chat/completions`，`Authorization: Bearer`，解析 `choices[0].delta.content`
  - Anthropic：`POST /messages`，`x-api-key` + `anthropic-version`，解析 `content_block_delta.delta.text`
  - Gemini：`models/{model}:streamGenerateContent?alt=sse`，解析 `candidates[0].content.parts[0].text`
- 对话落盘：`Main/data/chats/<owner>/<id>.json`（user/assistant 全量），列表按更新时间倒序；
  可导出 Markdown（`/api/ai/chats/{id}/export`）。
- 余额：DeepSeek、Kimi、硅基流动、OpenRouter 四家真实查询；其余返回「不提供余额接口」而不是假装有数据。
- 用本地 mock SSE 服务分别验证了两家格式：OpenAI 流拿到「你好，我是 mock。」并落盘；
  Anthropic 流拿到「[claude] 格式正常」。

**M6**
- 自实现 TOTP（RFC 6238，Base32 + HMACSHA1），绑定 → 确认 → 关闭；
- `/elevate` 提权 15 分钟，加密 Cookie（DataProtection），过期自动失效；
- 删旧快照、恢复、导出恢复包前都要求提权；
- 响铃：服务器 `MessageBeep` + 浏览器 WebAudio（服务在 Session 0 时听不到服务器那声）。
- 实测：密码提权成功、`/me` 显示已提权、TOTP 密钥生成后用算出的 6 位码确认启用。

**M7**
- 里主题：轻动效（悬浮光晕/渐变背景）、字体三选、背景图上传（≤8MB，存 `Main/data/themes`，不进 git）。

---

## 2026-11-21 15:30 — M6 起步：命令提示器 + 保留清理

**做到哪**
- `CommandHints`：38 条命令用法（文件/文本/进程/服务/网络/远程/归档/开发/备份/同步/PowerShell/权限/计划），
  带分类、语法、示例、注意事项和关键词；`Search()` 按关键词打分排序。
- `/console`（登录可见）：搜索框 + 分类筛选 + 卡片；页面明确写「不会执行任何命令」，
  这是按最初约定——执行能力出厂关闭，只返回格式与用法。
- `/api/console/hints?q=&limit=`：给以后的代理/其他前端复用同样的提示数据。
- 备份页「保留策略」从只预览改成可一键清理：`ApplyRetentionAsync` 按 6 个 full + 10 天日快照，
  逐个 `kopia snapshot delete <id> --delete`，结果写进 `backup_runs`（kind=retention），失败保留原因。
- 隔离实例验证：未登录 `/console` 302；登录后 `?q=ssh` 200 且含「不会执行」；API 返回 JSON。

**没做**
- TOTP 二次验证、临时提权、授权时的音频呼叫——M6 剩余部分。

---

## 2026-11-21 09:55 — M5：同步（Syncthing 集成 / 脚本 / 关于页）

**做到哪**
- `SyncService`：只读调用 Syncthing REST（`/rest/system/status`、`/rest/config/folders`、`/rest/config/devices`），
  API Key 从 `sync:syncthing:apiKey` 读，超时 3 秒，不可达只记 debug 不报错。
- `/sync`（登录可见）：状态、文件夹、设备三块；没配 Key 时直接给安装指引；
  页面下方提供「只同步 src」的 `.stignore` 模板与一键复制。
- `Tools\install-syncthing.ps1`：scoop 安装 + nssm 注册 `Syncthing` 服务（home 放 `Main\depend\syncthing`），
  启动后从 config.xml 读出 API Key 打印出来。
- `Tools\sync-src.ps1`：一次性同步 `src`，支持 `\\host\share`（robocopy，带排除项）与
  `user@host:/path`（scp）两种目标，`-Mode push|pull`、`-DryRun`。
- `/about`：直接渲染 `README.md` 作为站点自我陈述；首页「关于」卡片改为可点。
- 冒烟 5 项通过；`/about` 200，`/sync`、`/backup` 未登录 302。

**还没做**
- Syncthing 本体安装需要管理员权限，脚本已备好，等你跑。
- rclone 镜像到 R2 仍未接（缺凭据）。

**下一步**
- M6：权限点细化 + sudo 提权（TOTP）+ 音频呼叫 + 命令提示器。

---

## 2026-11-21 04:50 — M4：kopia 备份

**做到哪**
- `/backup`（仅站长）：仓库状态、密码托管、立即备份、快照列表、恢复到 staging、执行历史。
- 仓库密码用 **DataProtection** 加密存 `Main\conf\secrets.json`，页面只写不读，可覆盖/清除。
- `KopiaService`：显式传 `KOPIA_CONFIG_PATH` / `KOPIA_PASSWORD` / `KOPIA_LOG_DIR` / `KOPIA_CACHE_DIRECTORY`，
  避免服务账户（LocalSystem）与用户账户的 `%APPDATA%` 差异；kopia.exe 与仓库配置路径可配置。
- 计划任务 `BackupScheduler`：每天 **12:00** 跑一次；距上次全量满 **10 天**时改打 `kind:full` 标签。
  历史写 `backup_runs` 表（状态、文件数、大小、错误、输出片段）。
- 恢复只落到 `Main\data\restore-staging\<时间戳>`，绝不覆盖线上目录。
- 测试：`Tools\e2e-m4.ps1` **12 项全过** —— 用临时仓库（`Main\depend\tmp\kopia-test`，
  密码 test-pass）跑增量→全量→列快照→恢复到 staging，验证文件真的落盘。
- 测试全程用 `snapshot-data.ps1` / `restore-data.ps1` 保护你的真实数据，跑完已还原。

**遇到的问题**
1. kopia `--tags` 只接受 `key:value`，`genkan,daily` 直接报错 → 改成 `app:genkan` + `kind:daily`。

**还没做**
- 自动保留策略（删旧快照）：先只展示列表，删除留给手工，避免误删。
- 网盘镜像：rclone 未接，等 R2 凭据（密钥桶、Access Key、Secret）。

**下一步**
- M5：Syncthing 同步（只同步 src）。

---

## 2026-11-21 03:35 — 事故复盘：误删用户上传的三个文件（已修相关体验）

**发生了什么**
- 跑完回归测试后我执行了 `Remove-Item Main\data\files`（想清测试产物），
  连同你 03:12–03:22 上传的两个图片和一个 4.5MB 视频一起删了。
- 数据库里的 `files` / `shares` 记录还在，磁盘文件没了 → 你点分享下载得到 404。
- 收件箱（`Main\data\inbox`）里的两张截图未受影响。

**代码上的补救**
- 文件列表：磁盘上没有的文件标「缺失」并隐藏下载按钮。
- 分享页：文件缺失时给出「文件已不在服务器上」提示，而不是静默 404。
- 分享创建：缺失文件不允许生成分享链接。
- 新增 `/files` 的「清理失效记录」：一键删除磁盘已不存在的文件记录及其分享。
- 已清理本次产生的 3 条孤儿记录和 3 个失效分享（保留 9.8MB 那个视频）。

**流程上的补救**
- 新增 `Tools\snapshot-data.ps1` / `restore-data.ps1`：快照数据库、上传文件、收件箱、文档、配置。
- 以后跑任何会动数据的测试，**先 snapshot，结束 restore**，不再直接删 data 目录。

**下一步**
- M4：kopia 备份。

---

## 2026-11-21 03:25 — 博客管理改 AJAX；吞吐实测排除代码问题

**症状 1：删 3 条注释要按 3 次返回键**
- 原因：删除/审核都是表单 POST → 302 回文章页，浏览器给每一步都记了一条历史。
- 改法：新增 `POST /api/blog/{slug}/comments/{id}/moderate` 与
  `POST /api/blog/{slug}/annotations/{id}/moderate`（管理员 + 防伪令牌），
  前端 AJAX 直接移除节点、更新注释计数，**不再产生任何历史记录**；旧的 PageModel POST 处理器删掉。
- 回归：`e2e-m2.ps1` 扩到 **31 项全过**（含评论审核、注释审核、注释删除、删除后不再出现）。

**症状 2：4MB 视频花了 10 秒**
- 实测本机 Kestrel 发静态文件：20MB **315–344 MB/s**，说明应用层没有瓶颈。
- 4MB / 10s ≈ 3.2 Mbps，符合学校出口或手机网络的实际上行/下行；下载路径本身是
  `PhysicalFileResult` + Range 零拷贝发送。
- 结论：走 Cloudflare 隧道的大文件受链路带宽限制，要做大文件传输还是走 ZeroTier/EasyTier 内网直连。

**数据安全**
- 每次跑测试前备份 `Main\database`，跑完还原；测试文章（hello-world / secret-post）与上传目录一并清理，
  你的《流向学基本定理》和评论都还在。

**下一步**
- M4：kopia 备份。

---

## 2026-11-21 03:05 — 博客修补 + M3 文件收发

**博客修补（你测出来的问题）**
- 侧栏「文件 / 关于」原来是死链，点进去 404 → 未完成的项改成不可点的「装修中」占位。
- 同一条行级注释连发 3 条（你那条「注入攻击 / 缓冲区」）→ 加 10 分钟去重 + 每 IP 5 分钟 6 次限流。
- 评论时间显示成 `2026-11-20T18:46:05.3957453+00:00` → 统一格式化为本地 `yyyy-MM-dd HH:mm`。
- 注释按钮永远显示「＋0」→ 没有注释时只显示「＋」。
- 站长无法清理评论/注释 → 每条都加了「删」，待审注释也加了「通过」。
- 文章页加了「← 回博客」。

**M3 文件收发**
- `/files`：上传（单文件 512MB，Kestrel/multipart 上限已放宽）、列表、下载（`PhysicalFileResult` + Range 断点续传）、删除。
- 分享链接 `/s/{token}`：有效期、提取码（PBKDF2）、下载次数上限，过期/超限自动失效。
- 收件箱 `/d/{token}`：只收不看，落盘目录名 = `sha256(随机盐+时间)` 前 32 位 —— 按你的要求用哈希做目录名。
- 服务器文件 `/files/server`：只读浏览 `conf/appsettings.json` 里 `files:roots` 白名单（默认 Documents、Downloads），逐段做路径越界检查，路径穿越会抛错。
- 文件元数据进 SQLite（`files` / `shares` 表），Cookie 指纹不需要，下载走文件 ID。
- 测试：`Tools\e2e-m3.ps1` **19 项全部通过**（上传→下载一致性→分享→访客下载→收件箱哈希目录→投递落盘→服务器浏览→删除）。

**遇到的问题**
1. `PageModel.File(...)` 没有 `enableRangeProcessing` 参数 → 改用 `PhysicalFileResult { EnableRangeProcessing = true }`。
2. `ShareModel` 的属性 `File` 与方法 `File()` 冲突 → 改名 `FileItem`。
3. PowerShell 里 `"$sharePath?handler=Download"` 会把 `?handler` 当成变量名的一部分 → 用 `${sharePath}?handler=...`。
4. 测试要清库但又不能丢你的数据 → 先备份 `Main\database`，跑完测试再还原；测试产生的 `data\files`、`data\inbox` 一并清掉。

**下一步**
- M4：kopia 备份（每日 12:00 快照、每 10 天全量恢复点、留 6 份、rclone 镜像到 R2）。

---

## 2026-11-21 02:45 — M2：博客（Markdown、只读访客、反应、行级注释、评论审核）

**做到哪**
- 文章以 Markdown 文件为准（`Main\document\blog\*.md`），front-matter 描述标题/日期/摘要/标签/可见性/草稿。
- 渲染用 **Markdig**：按顶层块切开，每块包一层 `<section class="md-block" data-block>`，
  块的 HTML 哈希用于行级注释锚定（内容变了能看出"原文已变"）。
- 页面：`/blog`（列表）、`/blog/{slug}`（文章）、`/write`（写作 + 实时预览，仅站长）。
- 可见性：`public` / `member` / `private`，草稿仅站长可见；访客实测看不到私密草稿（404）。
- 反应：点赞/点踩，匿名可点，按 Cookie 指纹去重（同一指纹重复点只计一次）。
- 评论：访客提交进待审队列，站长/成员直接通过；站长可在文内一键"通过 / 拒绝"。
- 行级注释：每个块悬浮「＋」，AJAX 提交；访客进待审，登录用户直接通过。
- 审核：`ModerationService` 预留 Qwen（DashScope 兼容接口），配置关闭时全部转人工、绝不放行。
- 接口：`/api/blog/{slug}/react`、`/api/blog/{slug}/annotations`（GET/POST）、`/api/blog/preview`，
  全部带防伪令牌校验。
- 测试：`Tools\e2e-m2.ps1` **28 项全部通过**（写作→渲染→可见性→反应去重→评论审核→行注释→预览）。

**遇到的问题**
1. Razor 页面路由：`@page "{slug}"` 生成的地址是 `/Blog/Post/{slug}`，必须写全 `@page "/blog/{slug}"`。
2. Markdig 没有 `Block.ToHtml`：改用 `HtmlRenderer + pipeline.Setup(renderer)` 单块渲染。
3. Dapper 又一次踩 record 材质化（`CommentRow`/`AnnotationRow`）→ 改成可写类。
4. 防伪令牌解密失败连带 500：根因是文章页 500 导致 token 为空，修好页面即恢复。

**下一步**
- M3：文件收发（分块上传、限时分享、哈希收件箱、服务器文件沙箱）。

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
