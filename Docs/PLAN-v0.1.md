# Portal 个人站点门户 — 项目计划书

> 版本：v0.1（待校验）
> 日期：2026-11-20
> 站点根目录：`C:\Users\manifld\website`
> 主机：`DESKTOP-JGTGOHC` / Windows 10 22H2 (19044) / x64
> 状态：**等待你确认决策点（见第十五节）后进入 P0 开发**

---

## 一、勘察结论（已实测，非推测）

| 项目 | 实测结果 | 对方案的影响 |
|---|---|---|
| .NET SDK | **10.0.404**（`C:\Program Files\dotnet`），运行时 ASP.NET Core **10.0.15** + .NET 8.0.14 | 可直接用 .NET 10 开发，**无需下载任何东西** |
| Node / Python / Go | **均未安装** | 若选 Node/Go 需先联网安装（当前沙箱外网受限，见下） |
| 离线构建 | 在 `APPDATA` 重定向后 `dotnet new web` + `dotnet build` **成功**，零 NuGet 包 | 方案必须坚持**零第三方依赖**，否则构建会被网络卡死 |
| Kestrel 监听 | 沙箱内 `127.0.0.1:18080` 成功监听，用原始 TCP 请求拿到 `HTTP/1.1 200 OK` | 开发与本地冒烟测试在沙箱内可完成 |
| 沙箱外网 | HTTPS 出网失败（schannel `SEC_E_NO_CREDENTIALS`），TCP 443 可连但 TLS 握手被拦 | NuGet / CDN / xterm.js 等外部资源**默认拿不到**，需你批准一次 `danger-full-access` 或手动下载 |
| 管理员权限 | 当前会话**未提权**（Administrators 为 deny-only） | 注册服务必须由你在**管理员 PowerShell** 里执行我提供的脚本 |
| nssm | `C:\regser\nssm.exe`（2.24-101），已用于 OpenList 服务 | 直接复用，无需再装 |
| cloudflared | 服务 `Cloudflared` 运行中（LocalSystem），参数 `tunnel run --token-file C:\ProgramData\cloudflared\token` | **token 模式，入口规则在 Cloudflare 面板远程管理**，本机看不到主机名映射 |
| OpenList | 服务 `OpenList`，`C:\OpenList\openlist.exe server`，**5244** 端口已响应 v4.2.9 | 作为「挂载」模块的现成实例；文件模块不与之抢职责 |
| kopia | CLI 实为 `C:\Users\manifld\scoop\apps\kopiaui\current\resources\server\kopia.exe`（v0.23.1）；仓库 `H:\restore`（filesystem，500GB 容量，AES256-GCM，**当前 0 快照**） | 备份模块直接驱动该 CLI；注意 `current` 是软链，需固定版本目录 |
| 端口占用 | 5244 / 7897 / 9097 / 9993 / 61936-38 已用；**8080、8081、8090、3000、5000、8000 空闲** | 默认选 **8080** |
| 磁盘 | C: 62.8GB 空闲，H: 439.4GB 空闲，D: 26.7GB，F: 33.7GB，G: 38.7GB | 备份仓库在 H:；站点数据建议 C: 起步、可配置 |
| 其它已装 | git、7z、aria2、OpenSSH、VSCodium、qBittorrent、RustDesk、ZeroTier、mihomo(127.0.0.1:7897)、IIS（已装未运行） | 均可作为挂载对象；git 用于版本管理 |

**结论**：这台机器是「Windows 常开小服务器 + Cloudflare 隧道 + 一堆 scoop 工具」的形态。选 **.NET 10 + 零依赖**是唯一不需要额外下载、又能跑成 Windows 服务的稳妥路线。

---

## 二、目标与非目标

### 目标
1. 一个**统一入口**的轻量站点首页：侧边导航 + 仪表盘卡片，手机可用。
2. 模块化：文件收发、远程命令、远程同步、kopia 自动备份、用户权限、Markdown 展示，**外加随手可挂载任意 Web 服务**。
3. 可扩展：内置模块与外部插件走同一套契约；插件 = 丢一个文件夹进 `data/plugins/`。
4. 可运维：完整日志 / 审计、健康检查、`nssm.exe` 一键注册服务、可回滚升级。
5. 自描述：仓库内 `README.md`（自我陈述）+ `docs/` + 站点内「关于」页，都由 Markdown 渲染。

### 非目标（本期不做）
- 多租户 / 集群 / 水平扩展
- 邮件系统、即时 IM、移动端原生 App
- 取代 OpenList 的全部文件管理能力（它是挂载对象，不是竞品）
- 公网直连安全（明确依赖 Cloudflare 隧道，但仍在应用层做纵深防御）

### 量化指标
| 指标 | 目标值 |
|---|---|
| 空闲内存 | < 150 MB |
| 冷启动 | < 2 s |
| 发布物体积 | < 40 MB（framework-dependent，单 exe + 若干 dll） |
| 第三方依赖 | **0 个**（BCL + ASP.NET Core 共享框架） |
| 外部构建步骤 | 0（前端手写 ES Module，无打包器、无 CDN） |

---

## 三、技术选型

### 选定：C# / .NET 10 + ASP.NET Core Minimal API + 原生 JS

| 方案 | 优点 | 缺点 | 结论 |
|---|---|---|---|
| **.NET 10（选定）** | 已装 SDK；`UseWindowsService`/Kestrel 成熟；强类型、易扩展；可 P/Invoke 深度调用 Windows（ConPTY、DPAPI、系统信息）；framework-dependent 发布产物小 | 需要机器上有运行时（已有） | ✅ 采用 |
| Node.js + Fastify | 生态丰富、上手快 | 未安装，需联网；内存更高；Windows 服务需 wrapper | ❌ 备选（若你偏好 JS） |
| Go | 单文件静态二进制最轻 | 未安装，需联网下载 SDK；插件生态需要 gRPC/子进程 | ❌ 备选 |

### 关键取舍
1. **零 NuGet 包**。Markdown 渲染、TOTP、DPAPI、系统信息、ConPTY 全部自己实现（工作量可控，见第七节）。好处：断网环境可 `dotnet build` 到底，供应链风险≈0。
2. **前端无构建**。手写 `app.js` + 按模块拆分的 ES Module + 一个 `theme.css`；不用 React/Vue/打包器。图标用内联 SVG sprite，字体用系统字体。
3. **数据库用 JSON 文档 + JSONL 追写**，原子写（临时文件 + `File.Replace`）。定义 `IStore` 抽象，后续若要换 SQLite（需 NuGet）只换实现。
4. **发布方式**：`dotnet publish -c Release -r win-x64 --self-contained false`，产物目录 + apphost `Portal.exe` 直接给 nssm。
5. **端口 8080，仅绑 127.0.0.1**；cloudflared 走回环，无需开防火墙端口。

---

## 四、总体架构

```
                     浏览器 / 手机
                          │  HTTPS（Cloudflare 边缘 TLS + Access）
                  Cloudflare Edge
                          │  tunnel（token 托管，入口规则在面板改）
              cloudflared.exe（LocalSystem 服务，已存在）
                          │  http://127.0.0.1:8080
        ┌─────────────────┴──────────────────────────────┐
        │  Portal.exe（nssm 注册为 Windows 服务）          │
        │                                                │
        │  Core                                           │
        │   ├─ Config（appsettings.json，热读）           │
        │   ├─ Auth（会话 / PBKDF2 / TOTP / CF Access JWT）│
        │   ├─ RBAC（角色 → 权限点）                      │
        │   ├─ Audit（谁在何时对什么做了什么）             │
        │   ├─ Jobs（后台任务 + 进度 + 历史）              │
        │   ├─ Logs（结构化日志 + 轮转 + 在线查看）        │
        │   └─ Events（进程内事件总线，插件可订阅）        │
        │                                                │
        │  Modules（内置模块 ＝ 插件，走同一契约）          │
        │   dashboard · files · shell · sync · backup     │
        │   notes · mounts · users · logs · settings      │
        │                                                │
        │  Plugin Host                                    │
        │   └─ data/plugins/<id>/plugin.json + *.dll      │
        │      静态资源挂到 /m/<id>/assets/*               │
        │                                                │
        │  Storage                                        │
        │   data/*.json · data/files · data/notes         │
        │   data/logs · data/jobs · data/keys             │
        └───────────────┬────────────────────────────────┘
                        │ 子进程（受控、带超时与审计）
            kopia.exe   powershell.exe   rclone.exe（可选）
```

### 请求链路
1. 浏览器 → Cloudflare → 隧道 → `127.0.0.1:8080`
2. `Portal.exe` 先过 `ForwardedHeaders`（只信任回环来源）→ 认证中间件 → 权限中间件 → 模块 endpoint
3. 所有写操作写审计；长任务丢到 `Jobs` 后台执行，前端用 SSE/轮询拿进度
4. 模块通过 `IModuleContext` 拿服务：`Store`、`Logger`、`Jobs`、`Events`、`Paths`、`Secrets`、`Http`

### 插件模型（可扩展性的核心）
`Portal.Contracts.dll` 是唯一稳定契约，包含：

```csharp
public interface IPortalModule {
    string Id { get; }            // 全局唯一，小写短横线
    string Title { get; }         // 导航显示名
    string Icon { get; }          // 内置图标名或 SVG
    int Order { get; }            // 导航排序
    string? Permission { get; }   // 访问所需权限点，null=登录即可
    void Configure(IModuleContext ctx);   // 注册路由、导航、卡片、设置项、定时任务
}
```

一个插件目录：
```
data/plugins/hello/
├─ plugin.json        # {id,title,version,apiVersion,entry,permissions,nav:{...},static:true}
├─ Hello.dll          # 实现 IPortalModule
└─ assets/            # 可选，前端静态文件
```
加载规则：启动时扫描 → 版本校验（`apiVersion` 必须匹配）→ 独立 `AssemblyLoadContext` 加载 → 失败只记日志不影响主站。
插件可声明的**扩展点**：

| 扩展点 | 说明 |
|---|---|
| 路由 | `ctx.MapGet/MapPost(...)`，自动套用认证与权限 |
| 导航项 / 仪表盘卡片 | 由 `plugin.json` 声明，热生效（重启后） |
| 设置面板 | 声明 JSON Schema 子集，宿主自动渲染表单 |
| 定时任务 | `ctx.Schedule("0 3 * * *", handler)`，复用宿主调度器 |
| 事件订阅 | `FileUploaded`、`LoginSucceeded`、`BackupCompleted`、`JobFailed` |
| 权限点 | 插件自定义 `hello.run`，可在角色里勾选 |
| 存储区 | `ctx.Paths.ModuleData` → `data/modules/<id>/` |
| 嵌入式 UI | `view: { type: "iframe", url: "/m/<id>/ui" }`，插件可以是任何语言写的服务 |

---

## 五、目录结构

```
C:\Users\manifld\website\
├─ index.html                  # 将被替换为 SPA 外壳（保留同名，便于习惯）
├─ README.md                   # ★ 自我陈述（Markdown，站点 /about 直接渲染）
├─ PLAN.md                     # 本计划书
├─ CHANGELOG.md
├─ docs\
│  ├─ ARCHITECTURE.md          # 架构与数据流
│  ├─ PLUGIN-DEV.md            # 写一个插件：30 行上手
│  ├─ SECURITY.md              # 威胁模型与加固清单
│  └─ OPS.md                   # 部署、升级、排障、备份恢复演练
├─ src\
│  ├─ Portal.Contracts\        # 稳定契约（插件引用它）
│  ├─ Portal.Core\             # 配置/认证/RBAC/存储/日志/任务/事件
│  ├─ Portal.Modules\          # 内置模块
│  │  ├─ Dashboard\ Files\ Shell\ Sync\ Backup\ Notes\ Mounts\ Users\ Logs\ Settings\
│  ├─ Portal.Web\              # 静态前端（js/css/svg）
│  └─ Portal.App\              # 入口：Program.cs、publish 产物 → app\
├─ app\                        # 发布产物（Portal.exe + dll + wwwroot）
├─ data\                       # 运行数据（可迁到其它盘，见决策点 7）
│  ├─ appsettings.json
│  ├─ users.json  sessions.json  roles.json  mounts.json
│  ├─ shares.json  syncspaces.json  backups.json  jobs\*.json
│  ├─ files\  notes\  logs\  keys\  modules\  plugins\
├─ tools\
│  ├─ portal-sync.ps1          # 同步客户端（可被任务计划调用）
│  ├─ backup-now.cmd           # 手动触发备份
│  └─ export-config.ps1        # 导出配置包（不含密钥明文）
├─ scripts\
│  ├─ build.ps1                # 离线构建 + 发布到 app\
│  ├─ install-service.ps1      # ★ nssm 注册服务（需管理员）
│  ├─ uninstall-service.ps1
│  ├─ dev.ps1                  # 前台调试运行
│  └─ smoke-test.ps1           # 冒烟：健康检查 + 关键 API
└─ .git\                       # 版本管理（git 已装）
```

---

## 六、模块设计

### 6.1 仪表盘（Dashboard）
- 卡片：服务状态（自身 / cloudflared / OpenList / kopia）、磁盘余量、内存、CPU 负载、最近备份、最近任务、最近登录、快捷入口。
- 快捷操作：上传文件、跑命令、立即备份、发分享链接。
- 实现：`sysinfo` 用 P/Invoke（`GlobalMemoryStatusEx`、`GetSystemTimes`、`GetDiskFreeSpaceEx`），服务状态用 `sc query`，**不引入 System.Management/PerformanceCounter 包**。

### 6.2 文件收发（Files）
- **收发一体**：
  - 站内文件库：目录树、上传（拖拽/多选/粘贴）、下载（支持 Range 断点）、预览（图片/PDF/视频/文本/Office 走外部预览）、重命名/移动/删除、回收站（7 天）。
  - **分享链接**：有效期、提取码、下载次数上限、只读/可写。
  - **收件箱（Drop）**：生成一个「只能上传不能看」的链接给别人投递，文件落在指定目录。
  - **大文件分块**：`init → put chunk → complete(sha256)`，支持断点续传与秒传（同 hash 直接复用）。
- 安全：路径规范化 + 根目录约束 + 拒绝重解析点（junction/symlink）逃逸；文件名白/黑名单；单文件与总配额；上传速率限制；可选 Defender 扫描（`MpCmdRun.exe`）。
- 数据：`files.json` 元数据索引（路径、大小、hash、上传者、时间、标签），大目录分片存储。

### 6.3 远程命令（Shell）
- **v1 命令台**：表单（命令、工作目录、超时、环境变量）→ SSE 流式回传 stdout/stderr → 退出码。支持「保存为快捷命令」带参数模板，一键执行。
- 安全：默认**白名单**（可配置为仅管理员任意执行）；危险词二次确认；超时强杀进程树；输出截断 4MB；并发 2；全程审计；执行用户为服务账户（=LocalSystem，注意权限）。
- **v2 交互终端**（P3 阶段，可裁剪）：ConPTY（P/Invoke `CreatePseudoConsole`）+ WebSocket 双向转发 + 轻量 VT 渲染。若允许联网，可换 vendored xterm.js 获得完整终端体验。
- 任务：`jobs` 记录命令、耗时、退出码、输出摘要，可在「日志」页回看。

### 6.4 远程同步（Sync）
- **同步空间**：一个目录 + 权限 + 配额。用于手机/笔记本 ↔ 服务器双向或单向同步。
- 协议：`manifest`（路径/大小/mtime/sha256）+ 差异上传/下载 + 冲突副本（`.conflict-<host>-<ts>`）+ 分块传输。
- **客户端**：`tools\portal-sync.ps1`，用 `-Mode push|pull|two-way`、`-DryRun`、`-Include/-Exclude`；可注册计划任务定时跑（脚本自带 `-InstallTask`）。
- 兼容挂载：Syncthing / rclone / 任何 WebDAV 客户端 → 走「挂载」模块，不重复造轮子。可选 P4 增加只读 WebDAV 端点（让手机原生文件管理器直接挂）。

### 6.5 自动备份（Backup / kopia）
- 绑定现有仓库 `H:\restore`（AES256-GCM，500GB）。密码不落明文：DPAPI（`CryptProtectData`，机器作用域）加密存 `data/keys/kopia.dat`，UI 只写不可读。
- 能力：
  - 源清单（多个路径 + 是否含子目录 + 排除规则）
  - 计划：每日 HH:mm / 间隔 N 小时 / 每周某天；错过补跑
  - 保留策略：keep-latest / hourly / daily / weekly / monthly（透传 kopia policy）
  - 立即备份（后台任务，实时进度）
  - 历史：快照列表、大小、耗时、文件数、失败原因
  - 浏览与恢复：浏览快照目录树 → 勾选恢复到 `data/restore-staging/` → 再手动合并（**绝不直接覆盖线上目录**）
  - 维护：`kopia maintenance run`、`snapshot verify`、仓库状态
  - **自备份**：站点自身 `data\`（配置/用户/笔记）默认纳入每日快照
- 注意：kopia 以服务账户运行，`KOPIA_CONFIG_PATH` / `KOPIA_CACHE_DIRECTORY` / `KOPIA_LOG_DIR` 由站点显式指定到 `data\kopia\`，避免 %APPDATA% 差异导致找不到仓库。

### 6.6 用户与权限（Users）
- 账号：用户名、显示名、PBKDF2-SHA256(310k) 密码、启用/禁用、角色、配额、TOTP 可选、API Token（哈希存储、可吊销）、最近登录、登录设备。
- 角色：内置 `admin` / `operator` / `viewer`，支持自定义角色勾权限点（`files.read`、`shell.execute`、`backup.run`、`users.manage`、`mount.<id>` …）。
- 会话：随机 256bit → 哈希存库；`HttpOnly; Secure; SameSite=Lax`；滑动 7 天 / 绝对 30 天；可在线踢下线。
- 首次启动向导：创建管理员，**无默认密码**。
- 可选 SSO：信任 Cloudflare Access 的 `Cf-Access-Jwt-Assertion`（拉 JWKS 验签 + 校验 aud），按邮箱/域自动映射角色。

### 6.7 Markdown 展示（Notes）
- 内容根 `data\notes\`（也可挂到仓库 `docs\`），支持子目录、front-matter（title/order/tags/hidden）、目录树、TOC、代码块。
- 渲染器自研（CommonMark 常用子集）：标题、列表、任务列表、表格、引用、围栏代码、链接/图片、行内代码、粗斜体、删除线、脚注、`[[双链]]`、水平线；**默认不直通原始 HTML**（可配置开启），从根上避免 XSS。
- 代码高亮：内置迷你高亮（json / ps1 / bash / csharp / js / ini / yaml），避免引入 highlight.js。
- 编辑器：分栏实时预览（服务端渲染预览接口，保证与展示一致）、保存即产生 `.history/<file>/<ts>.md` 版本、可 diff、可回滚。
- **自我陈述**：`README.md` 与 `docs/*.md` 在「关于 / 文档」页渲染；站点首页也放一段 `data/notes/home.md` 欢迎语。

### 6.8 挂载（Mounts）——「随手挂任何东西」
三种形态：
| 类型 | 用途 | 实现 |
|---|---|---|
| `iframe` | 内嵌已允许被嵌的服务（OpenList、qBittorrent、VSCodium 等） | 外壳内 iframe，带顶栏、全屏、新窗口按钮 |
| `proxy` | 不允许被 iframe 或需注入鉴权头的服务 | 反向代理（含 WebSocket 中继），路径 `/m/<id>/` |
| `link` | 纯外链入口 | 新标签打开 |
- 记录字段：id、标题、URL、图标、类型、权限点、排序、健康检查 URL、注入请求头（如 `Authorization`）、是否隐藏。
- 现成挂载：OpenList（`http://127.0.0.1:5244`）、qBittorrent、KopiaUI 替代页、RustDesk 等。
- 代理忠实度说明：HTML 内绝对路径改写不可靠，**推荐优先 iframe**；proxy 主要用于纯 API/WebSocket 类服务。

### 6.9 日志与审计（Logs / Audit）
- 应用日志：JSONL（`ts, level, module, msg, ctx`）+ 人读文本日志，按天 + 10MB 轮转，保留 30 天。
- 审计：单独 `audit.jsonl`，记录 `who / action / target / ip / ua / result / duration`；不可从 UI 删除。
- 在线查看：按级别/模块/时间/关键字过滤，tail 500 行，一键下载；审计只读。
- 后台任务：`data\jobs\*.json` 持久化，重启后仍能看到历史与失败原因。

---

## 七、关键自研组件（零依赖清单）

| 组件 | 实现要点 | 估时 |
|---|---|---|
| Markdown 渲染器 | 行级状态机 + 块级解析；HTML 转义 + 白名单标签；表格/任务列表/代码块 | 0.5 天 |
| 密码学 | PBKDF2(`Rfc2898DeriveBytes`)、SHA256/HMAC、Base32 TOTP、常量时间比较 | 0.5 天 |
| DPAPI | `CryptProtectData`/`CryptUnprotectData` P/Invoke（kopia 密码、SMTP 等密钥） | 0.2 天 |
| 系统信息 | `GlobalMemoryStatusEx`、`GetSystemTimes`（CPU 差分）、`GetDiskFreeSpaceEx` | 0.3 天 |
| 会话与限流 | 内存字典 + 定时持久化；固定窗口限流（IP + 用户双维度） | 0.3 天 |
| 调度器 | 简易 cron（分 时 日 月 周）+ 错过补跑 + 任务锁 | 0.3 天 |
| 前端外壳 | 路由、fetch 封装、Toast、Modal、表格、树、上传器（分块+进度） | 1.5 天 |
| 迷你代码高亮 | 正则分词，6 种语言 | 0.3 天 |
| ConPTY（v2） | `CreatePseudoConsole` + `ResizePseudoConsole` + 管道读写 | 0.5 天 |

---

## 八、数据模型（示例）

```jsonc
// data/users.json
{ "users": [{
  "id": "u_01", "name": "manifld", "display": "站长",
  "pwd": { "algo": "pbkdf2-sha256", "iter": 310000, "salt": "b64", "hash": "b64" },
  "roles": ["admin"], "enabled": true, "quotaBytes": 0,
  "totp": null, "createdAt": "2026-11-20T10:00:00Z", "lastLogin": null
}]}

// data/mounts.json
{ "mounts": [{ "id": "openlist", "title": "文件柜", "url": "http://127.0.0.1:5244",
  "type": "iframe", "icon": "folder", "permission": "mount.openlist",
  "order": 20, "hidden": false, "health": "http://127.0.0.1:5244/ping" }]}

// data/backups.json
{ "repo": { "kind": "kopia", "exe": "...\\kopia.exe", "config": "data\\kopia\\repository.config",
            "storage": "H:\\restore", "passwordRef": "dpapi:kopia" },
  "sources": [ { "path": "C:\\Users\\manifld\\website\\data", "exclude": ["logs","*.tmp"] } ],
  "schedule": { "type": "daily", "at": "03:30", "catchUp": true },
  "retention": { "keepLatest": 10, "keepDaily": 14, "keepWeekly": 8, "keepMonthly": 6 } }

// data/jobs/j_20261120_0330.json
{ "id": "j_...", "kind": "backup", "state": "succeeded", "startedAt": "...",
  "endedAt": "...", "summary": { "files": 12843, "bytes": 7340032000 }, "log": "jobs/j_....log" }
```

---

## 九、API 概览（前缀 `/api`，全部鉴权 + CSRF 头）

| 组 | 方法/路径 | 说明 |
|---|---|---|
| Auth | `POST /auth/login`、`POST /auth/logout`、`GET /auth/me`、`POST /auth/totp` | 会话 |
| Meta | `GET /healthz`（无鉴权）、`GET /api/version`、`GET /api/nav`、`GET /api/dashboard` | 外壳与状态 |
| Files | `GET /files?path=`、`POST /files/upload`、`POST /files/chunk/{init,put,complete}`、`GET /files/download`、`PATCH /files`、`DELETE /files` | 文件 |
| Shares | `POST /shares`、`GET /shares`、`DELETE /shares/{id}`、公开端点 `/s/{token}`、`POST /d/{token}` | 分享/收件箱 |
| Shell | `POST /shell/exec`（SSE 回传）、`GET /shell/commands`、`POST /shell/commands`、`POST /shell/kill/{id}` | 命令 |
| Sync | `GET /sync/spaces`、`GET /sync/{id}/manifest`、`POST /sync/{id}/upload`、`GET /sync/{id}/download`、`DELETE /sync/{id}/file` | 同步 |
| Backup | `GET /backup/status`、`POST /backup/run`、`GET /backup/snapshots`、`GET /backup/browse?snap=&path=`、`POST /backup/restore`、`POST /backup/verify` | kopia |
| Notes | `GET /notes/tree`、`GET /notes/doc?path=`、`PUT /notes/doc`、`POST /notes/preview`、`GET /notes/history` | Markdown |
| Mounts | `GET /mounts`、`POST /mounts`、`PATCH /mounts/{id}`、`DELETE /mounts/{id}`、`ANY /m/{id}/{**rest}`（代理） | 挂载 |
| Users | `GET/POST /users`、`PATCH /users/{id}`、`POST /users/{id}/password`、`POST /users/{id}/tokens`、`GET /sessions` | 权限 |
| Logs | `GET /logs?level=&module=&q=`、`GET /logs/audit`、`GET /logs/download` | 日志 |
| Jobs | `GET /jobs`、`GET /jobs/{id}`、`POST /jobs/{id}/cancel` | 后台任务 |

约定：统一 `{ ok, data, error }`；时间 UTC ISO8601；分页 `?offset=&limit=`；错误码稳定（`E_AUTH`、`E_PERM`、`E_VALIDATION`、`E_NOTFOUND`、`E_CONFLICT`、`E_LIMIT`）。

---

## 十、安全设计（即使有 Cloudflare 也做纵深）

1. **网络**：只绑 `127.0.0.1:8080`；仅 cloudflared 可达；`ForwardedHeaders` 只信任回环。
2. **认证**：无默认账号；首次向导建管理员；登录限流（5 次/5 分钟/IP+账号）+ 锁定；可选 TOTP；可选 CF Access JWT 验签（RS256 + JWKS 缓存 + aud/exp 校验）。
3. **会话**：随机 256bit，哈希落库；Cookie `HttpOnly/Secure/SameSite=Lax`；写操作要求自定义头 `X-Portal-CSRF`（同步校验）。
4. **授权**：每个 endpoint 标 `[RequirePermission("files.write")]`；默认拒绝；挂载与模块可单独授权。
5. **文件**：根目录约束、拒绝重解析点、文件名净化、大小/数量/类型限制、ZIP 炸弹比例上限、上传并发限流。
6. **命令**：默认白名单；危险命令二次确认；超时杀进程树；输出截断；全程审计；只有管理员/操作员角色可用。
7. **密钥**：DPAPI 加密；日志脱敏（`password|token|secret|key` 模式）；配置导出不含明文。
8. **审计**：登录、权限变更、文件删除、命令执行、备份恢复、挂载修改全部留痕，UI 不可删改。
9. **恢复**：备份恢复一律先进 staging 目录，人工确认后再合并；提供「演练」按钮验证可恢复性。

---

## 十一、部署与运维

### 服务注册（`scripts\install-service.ps1`，需管理员）
```powershell
$nssm = "C:\regser\nssm.exe"
$app  = "C:\Users\manifld\website\app\Portal.exe"
& $nssm install  Portal $app
& $nssm set      Portal AppDirectory  "C:\Users\manifld\website\app"
& $nssm set      Portal AppParameters "--urls http://127.0.0.1:8080"
& $nssm set      Portal AppStdout     "C:\Users\manifld\website\data\logs\service.out.log"
& $nssm set      Portal AppStderr     "C:\Users\manifld\website\data\logs\service.err.log"
& $nssm set      Portal AppRotateFiles 1
& $nssm set      Portal AppRotateOnline 1
& $nssm set      Portal AppRotateBytes 10485760
& $nssm set      Portal AppStopMethodConsole 15000
& $nssm set      Portal AppThrottle    5000
& $nssm set      Portal Start           SERVICE_AUTO_START
Start-Service Portal
```
- 卸载：`uninstall-service.ps1`（stop → remove → 保留 data）。
- 前台调试：`scripts\dev.ps1`（`dotnet run` 或直接跑 app\Portal.exe）。
- 升级：`build.ps1` 构建到 `app\new` → 停服务 → 替换 → 启服务 → `smoke-test.ps1`；失败自动回滚上一份 `app.bak`。
- 健康检查：`GET /healthz` 返回 `{status,version,uptime,modules}`，可给 Cloudflare/外部监控探活。

### Cloudflare 侧
- 隧道已由服务托管（token 模式），你只需在面板把主机名指到 `http://127.0.0.1:8080`。
- 建议开启 Cloudflare Access（邮箱 OTP/OTP 策略）作为**第二道门**；应用内仍保留自己的登录，二者不冲突（可选启用 JWT 信任实现免二次登录）。
- 建议开 `Always Use HTTPS`；应用层不用自签证书。

### 备份自身
- 每日快照包含 `data\`（含用户、笔记、配置、密钥密文）；`H:\restore` 已 439GB 余量，按 10GB/日增量估算可留很久。计划里会给出保留策略默认值，可改。

---

## 十二、里程碑与交付物

| 阶段 | 内容 | 预估 | 交付物 |
|---|---|---|---|
| **P0 骨架** | 解决方案、配置、日志、认证、RBAC、外壳 UI、导航、健康检查、构建脚本 | 1.5 天 | 能登录的空壳站点 + `build.ps1` |
| **P1 内容** | Markdown 模块（渲染/编辑/预览/历史）、挂载模块（iframe/proxy/link）、仪表盘 | 1.5 天 | `/about` 自我陈述可见、OpenList 挂载可用 |
| **P2 文件与命令** | 文件库 + 分块上传 + 分享/收件箱；命令台（SSE + 审计 + 白名单）；日志页 | 2 天 | 可收发文件、可跑命令并留痕 |
| **P3 同步与备份** | 同步空间 + `portal-sync.ps1`；kopia 备份/历史/浏览/恢复 | 2 天 | 手机↔服务器同步；每日自动备份可见 |
| **P4 扩展与运维** | 插件加载器 + 示例插件 + 插件文档；用户/角色细化、SSO；部署脚本、冒烟测试、文档 | 2 天 | 丢 DLL 即扩展；`install-service.ps1` 一键注册 |
| **P5 打磨** | 终端 v2（ConPTY，可选）、性能、移动端适配、恢复演练 | 1.5 天 | 验收清单全绿 |

> 合计约 **10.5 个工作日**；若你只想要最小可用（P0–P2），约 5 天。

### 每阶段验收标准
- P0：`sc query Portal` 运行中；浏览器登录成功；`/healthz` 200；重启机器后自动起来。
- P1：README 在站内正确渲染（表格/代码块/中文）；三个挂载类型各跑通一个。
- P2：上传 1GB 文件断点续传成功；分享链接过期失效；命令台能跑 `git status` 且审计有记录；非管理员访问被拒。
- P3：`portal-sync.ps1 -DryRun` 只显示差异；定时备份产生快照并能在 UI 看到；从快照恢复单个文件到 staging。
- P4：`cp -r hello-plugin data\plugins\hello` 重启后导航出现新模块；`install-service.ps1` 在管理员窗口一次成功。
- P5：手机端布局正常；恢复演练通过；内存 < 150MB。

---

## 十三、风险与对策

| 风险 | 影响 | 对策 |
|---|---|---|
| 沙箱外网受限（无法 NuGet/CDN） | 不能引入第三方库 | 全零依赖方案；确需 xterm.js 等由你批准一次 `danger-full-access` 下载并 vendored 入库 |
| 当前会话非管理员 | 无法代你注册服务 | 脚本交你在管理员窗口执行；我只做构建与验证 |
| 服务账户=LocalSystem | 命令台权限过大 | 默认白名单 + 审计 + 二次确认；或改专用账户（决策点 3） |
| kopia 密码托管 | 丢失则仓库不可读 | DPAPI 加密 + 你手工另存一份；文档强制提示 |
| JSON 存储扩展性 | 海量文件索引变慢 | 分片 + 内存索引 + `IStore` 抽象，后期换 SQLite |
| proxy 挂载兼容性 | 部分站点白屏 | 优先 iframe；proxy 定位为 API/WS 场景 |
| 端口/隧道变更 | 外部访问中断 | 端口与绑定写死在 appsettings；面板改动后重启 cloudflared |
| 断电/崩溃 | 数据损坏 | 原子写 + 启动一致性检查 + 每日快照 |

---

## 十四、我打算在 P0 前先做的事

1. `git init` + 首次提交（当前只有占位 `index.html`，保留一份旧文件副本）。
2. 建立解决方案骨架与离线构建脚本（已验证可行）。
3. 写 `README.md` 初稿（自我陈述）与 `docs/ARCHITECTURE.md`。
4. 用占位数据把外壳 UI 跑起来给你看，再逐模块填充。

---

## 十五、待你确认的决策点（回复编号即可，例如「1A 2A 3B…」）

**1. 技术栈**
- A（推荐）.NET 10 + 原生 JS，零依赖，已装环境可直接开工
- B. Node.js（需联网装 Node，生态更花哨）
- C. Go（需联网装 SDK，单文件最轻）

**2. 端口/访问方式**
- A（推荐）`127.0.0.1:8080`，你在 Cloudflare 面板把主机名指过来
- B. 你指定端口；C. 直接绑 `0.0.0.0`（需额外开防火墙，不推荐）

**3. 服务运行账户**
- A（推荐）LocalSystem：备份/命令权限最大，最省事
- B. 专用低权限账户：更安全，但备份用户目录要额外授权

**4. 远程命令的边界**
- A（推荐）仅管理员可跑任意命令，全程审计 + 危险命令二次确认
- B. 严格白名单（只有列出的程序能跑）
- C. 还要交互式终端（ConPTY，额外 0.5 天 + 前端风险）

**5. 远程同步形态**
- A（推荐）自带 `portal-sync.ps1` + 同步空间（手机可用 HTTP 客户端）
- B. 同时集成 Syncthing（挂载即可，不写代码）
- C. 还要 WebDAV 端点（手机原生文件管理器直接挂）

**6. 备份策略（kopia）**
- 请给我要备份的目录清单（例如 `C:\Users\manifld\Documents`、`D:\项目`、站点 `data\`）
- 每日几点跑？默认 **03:30**；保留策略默认 `latest 10 / daily 14 / weekly 8 / monthly 6`
- kopia 仓库密码由站点托管（DPAPI 加密）是否同意？

**7. 数据落在哪**
- A（推荐）站点 `data\` 在 C 盘（62GB 空闲），大文件目录可指向 H 盘（439GB）
- B. 整个 `data\` 放 H 盘（需你确认 H 盘用途，现在是 kopia 仓库）

**8. 用户与访客**
- A（推荐）只给你一个管理员账号，分享链接对外
- B. 再开只读访客账号
- C. 接 Cloudflare Access SSO（免二次登录）

**9. 界面**
- 中文为主（可切英文）？深色默认还是跟随系统？名称用 **Portal / 门户** 还是你另取一个？

**10. 调度/名称等小项**
- 站点标题、Logo 文案；是否要我顺手把 `index.html` 占位页替换掉（推荐替换）。

---

## 附：开发环境注意事项（给未来的我）

1. 在 DSH 沙箱内构建需重定向环境变量，否则 NuGet 读 `%APPDATA%\NuGet\NuGet.Config` 被拒：
   ```powershell
   $env:APPDATA = "<workspace>\.build\appdata"
   $env:NUGET_PACKAGES = "<workspace>\.build\nuget"
   $env:DOTNET_CLI_HOME = "<workspace>\.build"
   ```
2. 沙箱内 `Invoke-WebRequest` 读子进程控制台会报 Win32 错误；验证 HTTP 用原始 `TcpClient` 或 `curl.exe`（HTTP 可用，HTTPS 会因 schannel 失败）。
3. kopia 在沙箱内需显式设置 `KOPIA_CACHE_DIRECTORY` / `KOPIA_LOG_DIR` 到工作区，否则缓存写入被拒。
4. 除工作区外的写操作、提权、联网，都需要你批准或由你手工执行。

---

*本计划书为 v0.1；你确认决策点后我会更新为 v0.2 并开始 P0。*
