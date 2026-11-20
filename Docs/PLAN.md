# 玄关 Genkan — 项目计划书 v0.2

> 站点根目录：`C:\Users\manifld\website`
> 主机 `DESKTOP-JGTGOHC` / Windows 10 22H2 19044 / x64
> 基准：v0.1 计划书 + `回复.md` 全部意见
> 日期：2026-11-20
> 状态：已进入 M0（M0.1 完成）。v0.3 增补见 `Docs/PLAN-v0.3-ADDENDUM.md`

---

## 0. 变更摘要（对 `回复.md` 的逐条落实）

| 你的意见 | 落实方式 |
|---|---|
| 小型网站、用户不多 | 单进程 + SQLite，不做集群/多租户；访客与站内账号分离 |
| **尽量复用成熟项目，少造轮子** | 见第 4 节：Markdown 用 Markdig、数据库用 SQLite、日志用 Serilog、调度用 Cronos、同步用 Syncthing、网盘用 rclone、前端交互用 htmx+Alpine，只有"胶水"自己写 |
| **尽快上线 AI 对话前端（chat-demo）** | 官方提供纯静态发行包 `chat-demo-v0.1.3.zip`（1.88MB，校验 sha256），解压即用，放 `Extend\chat\`，M1 第一个交付 |
| 模块化易维护 | 每个板块一个独立项目/目录 + 统一契约 + 可在界面里挂载/卸载 |
| **移动端与 Web 端分开设计** | 两套布局：桌面**右侧**导航栏；手机**顶部标题栏 + 底部标签栏**，各自维护 CSS 与页面结构 |
| 允许与 dsh-go 联动 | 方案 B：部署 ds-harness-remote 通道，让手机端 dsh-go 经 Cloudflare 域名连回本机 DSH（第 8.12 节，待确认） |
| 类 unix bash 与 terminal ❌ | 不做。远程命令模块只做"命令执行"，且默认关闭 |
| Windows 风格系统状态仪表盘 ✔ | 首页仪表盘：CPU/内存/磁盘/网络/进程/服务，仿任务管理器视觉 |
| **临时管理员强认证（2FA + 验证码）** ✔ | "sudo 模式"：二次认证（TOTP）后获得 15 分钟临时管理员权限，可音频呼叫提醒 |
| 全部功能可挂载/卸载 + 权限管理 | 每个模块有 `enabled` 开关与独立权限点，挂载物在界面里增删 |
| **主站放 Main，旁边 Extend 管挂载物** | 目录重构为 `Main\` + `Extend\`（第 5 节） |
| **可视化路由/挂载管理，仅本机执行** | `/admin/routes` 页面，检测到请求来自隧道（存在 `Cf-Connecting-IP`）即拒绝，只允许本机回环访问 |
| 收件箱用哈希值做文件夹名 | 收件目录 = `sha256(收件箱ID + 时间戳)` 前 32 位，防路径猜测与遍历 |
| 远程命令实现后暂时关闭 | 代码与权限点齐全，`enabled: false` 出厂，界面上一键开 |
| 避免大文件走 TCP；有 ZeroTier/EasyTier | 大文件走虚拟局域网直连（第 11 节）；公网隧道只走小文件与页面 |
| 远程编程只同步 src | 同步模块预设"仅 src"模板，一键为项目生成同步配置 |
| 每 10 天全量、每天增量、留 6 份（60 天） | kopia 每日快照 + 每 10 天"全量恢复点"标记，保留 6 个恢复点；网盘异地副本（第 12 节） |
| 网盘防灾害 | rclone 把 kopia 仓库镜像到网盘（目标待你选） |
| 博客化：访客只读、点赞点踩、分行注释、评论 | 博客模块 + 行级批注 + 评论 + 反应（第 8.3 节） |
| 少造轮子（关键自研模块） | 自研部分压缩到：权限/挂载管理胶水、大文件分块、音频呼叫。其余全部用现成库 |
| ~~MongoDB~~ | 不引入；SQLite 足够，且单文件易备份 |
| 九~十二节同意 | 接口、安全、部署、里程碑沿用并更新 |
| 忙，需音频呼叫授权 | 提权/危险操作请求 → 服务器 TTS 播报 + 浏览器提示音 + 站内待办 |
| GitHub + Gitee 各一份；留开发日志 | 双远端推送；`DEVLOG.md` 按"时间/做到哪/什么问题/怎么解决"记录 |
| 1A 2A 3A 4A 5AB | .NET / 8080 回环 / LocalSystem / 管理员任意命令（默认关）/ 自带脚本 + Syncthing |
| 6 只备份站点相关；每天 12:00；保留策略同意 | 备份 `Main\src document depend website data database conf` + `Extend\*` 配置 |
| 7A 小文件在 data、大文件在 H 盘；盘符可能变 | 大文件区用**卷 GUID** 定位（本机 F/H 卷标都叫"软件"，卷标不可靠） |
| 8B 访客只读博客，不能碰数据；限时分享链接 | 访客角色只有 `blog.read`；文件分享走独立 token 链接 |
| 9 中文、多主题、表/里形态、名称"玄关" | 站点名「情绪流形的小屋o(*￣▽￣*)ブ」，程序名「玄关 Genkan」，主题 3 套 + 表/里模式 |
| 替换 index.html | 根 `index.html` 换成落地页（引导到门户/博客），保留跳转 |

---

## 1. 环境补充勘察（v0.2 新增实测）

| 项目 | 结果 | 影响 |
|---|---|---|
| 出网 | 沙箱默认拦 TLS；**经授权以完全访问模式实测 GitHub / NuGet 均 200** | 可以复用 NuGet 包与开源前端库；但每次联网需你点一次授权 |
| chat-demo | 官方仓库 `deepseek-ai/chat-demo`，最新 **v0.1.3**（2026-11-20 发布），资产 `chat-demo-v0.1.3.zip` 1,882,350 字节，sha256 `5e8d69c2…d64d3`；纯静态，浏览器直连 API，无需后端 | "尽快上线"可在 M1 当天完成 |
| dsh-go | 手机端 App（Android），电脑端经 **SSH** 或 **DSHarness Remote** 连接本机 DSH | 联动 = 反向通道，见 8.12 |
| 卷标 | C、D 无标签；**F 与 H 都是「软件」**；G「文档」；E/I 为可移动盘 | 不能用卷标定位，改用卷 GUID + 相对路径 |
| 已装/未装 | Syncthing ✗、rclone ✗、EasyTier ✗（ZeroTier ✔ 已在跑） | 用 scoop 安装（网络已确认可用） |
| scoop | buckets：main、extras（extras 走 gitee 镜像） | 安装顺利 |

---

## 2. 命名与身份

| 项 | 值 |
|---|---|
| 项目名 | **玄关（Genkan）** —— 进门第一眼，通往各个房间 |
| 站点名 | **情绪流形的小屋o(*￣▽￣*)ブ** |
| 服务名 | `Genkan`（nssm 服务标识，纯 ASCII） |
| 图标 | 占位符（内联 SVG「门/玄关」图形），后续可替换 `Main\website\assets\logo.svg` |
| 语言 | 仅简体中文（预留 i18n 结构，不投入翻译） |
| 根落地页 | `index.html` 替换为轻量落地页：站点名 + 进入门户 / 进入博客 |

---

## 3. 范围与优先级

### 必做（打✔部分）
1. 统一入口首页 + 仪表盘（Windows 风格系统状态）
2. AI 对话（chat-demo 静态挂载）—— **优先级最高，尽快可见**
3. 博客 / Markdown：文章、访客只读、点赞点踩、**分行注释**、评论
4. 文件收发：上传下载、限时分享链接、哈希目录收件箱
5. 路由与挂载管理（仅本机）+ Extend 目录自动发现
6. 自动备份（kopia：10 日全量 / 日增量 / 留 6 份 / 网盘副本 / 恢复包）
7. 项目同步（Syncthing 集成 + 仅 src 模板 + 一键脚本）
8. 用户 / 访客 / 权限 / **临时提权 sudo 模式（2FA + 验证码 + 音频呼叫）**
9. 远程命令（实现，**默认关闭**）
10. 日志、审计、开发日志、双远端备份、nssm 服务化
11. dsh-go 联动

### 不做
- 类 unix bash / 交互式 terminal
- MongoDB、多租户、集群、移动原生 App、邮件系统
- 公网直连安全（依赖 Cloudflare 隧道 + 应用层纵深）

---

## 4. 技术选型（复用优先）

### 4.1 清单
| 关注点 | 选型 | 理由 |
|---|---|---|
| 运行时 | **.NET 10（已装 10.0.404）** | 无需安装；Windows 服务友好；内存约 80–150MB |
| Web | ASP.NET Core：Minimal API（数据接口）+ Razor Pages（页面） | 服务端渲染，首屏快，模块化页面天然隔离 |
| 交互 | **htmx + Alpine.js**（vendored 到 `Main\depend\web`） | 无需打包器、无需 Node，成熟稳定，页面级渐进增强 |
| 样式 | 自写 CSS 变量主题系统 + 少量原子类 | 主题（表/里）与双端布局必须自己控 |
| Markdown | **Markdig** | 最成熟的 .NET CommonMark 实现，表格/脚注/任务列表齐备 |
| 数据库 | **SQLite（Microsoft.Data.Sqlite）** | 单文件、零运维、随备份一起走 |
| ORM | **Dapper** | 轻、快、SQL 可控，避免 EF 迁移复杂度 |
| 日志 | **Serilog**（Console + File 轮转） | 成熟；结构化日志可按模块过滤 |
| 调度 | **Cronos**（cron 解析）+ 自建轻量调度宿主 | 只跑日备份/清理，不引入 Quartz 全家桶 |
| 同步 | **Syncthing** | 成熟 P2P 同步；"只同步 src" 用 ignore 规则实现 |
| 网盘 | **rclone** | 支持主流网盘，一条命令做镜像 |
| 备份 | **kopia**（已装 0.23.1，仓库 `H:\restore`） | 已加密、去重、支持恢复 |
| 系统信息 | `System.Diagnostics.PerformanceCounter` 或 P/Invoke 轻封装 | 仪表盘数据（CPU/内存/磁盘/网络/进程） |
| TOTP | **Otp.NET** | 成熟，2FA 直接可用 |
| 压缩/哈希 | 内置 `System.IO.Compression` / `SHA256` | 无额外依赖 |

> 依赖全部走 NuGet，并把全局包目录重定向到 `Main\depend\nuget`，这样"离线可重建"且随备份一起走。

### 4.2 为什么不是 React/Vue 全套
- chat-demo 已经是独立静态应用，主站再用重型 SPA 属于重复投入；
- htmx + 服务端渲染的维护成本更低，模块插件只要输出 HTML 片段即可；
- 手机端/桌面端两套布局用服务端按 UA + 媒体查询切换，比 SPA 里写两套组件直观。

### 4.3 依赖获取方式
- 首次构建会从 NuGet 拉包（需一次网络授权），之后走本地缓存；
- 前端库（htmx、Alpine）下载后放 `Main\depend\web`，**不引用任何 CDN**（断网也能跑）。

---

## 5. 目录结构（Main + Extend）

```
C:\Users\manifld\website\
├─ Main\                          # ★ 主站（玄关本体）
│  ├─ src\                        # 源码（解决方案 + 各模块项目）
│  │  ├─ Genkan.Contracts\        # 模块契约（插件引用）
│  │  ├─ Genkan.Core\             # 配置/认证/权限/审计/调度/存储
│  │  ├─ Genkan.Modules.*\        # 仪表盘/博客/文件/备份/同步/命令/路由…
│  │  ├─ Genkan.Web\              # 页面、静态资源映射、中间件
│  │  └─ Genkan.App\              # 入口，发布出 Genkan.exe
│  ├─ document\                   # Markdown 内容（自我陈述、文章、文档）
│  ├─ depend\                     # 第三方依赖（web 前端库、nuget 缓存、工具）
│  ├─ website\                    # 前端静态资源（css/js/图片/主题/logo）
│  ├─ data\                       # 用户数据（小文件、笔记附件、收件箱、扩展数据）
│  ├─ database\                   # SQLite 数据库文件（genkan.db 等）
│  ├─ conf\                       # 配置与密钥（appsettings、密钥环、恢复包）
│  ├─ logs\                       # 应用日志 / 审计 / 任务日志
│  └─ bin\                        # 构建发布输出（Genkan.exe + dll）
├─ Extend\                        # ★ 挂载物（每个一个目录，可独立启停）
│  ├─ chat\                       # AI 对话（chat-demo 静态包）
│  │  ├─ extend.json              # 挂载清单：名称/路由/类型/权限/图标
│  │  ├─ www\                     # 静态文件（index.html 等）
│  │  └─ logs\
│  ├─ dsh\                        # 预留：dsh-go 反向通道
│  └─ <你的挂载物>\
├─ Tools\                         # 构建/部署/同步客户端/备份脚本
├─ Docs\                          # 计划书、架构、运维、插件开发文档
├─ index.html                     # 落地页（替换原占位文件）
├─ README.md                      # ★ 自我陈述（站内「关于」渲染它）
├─ DEVLOG.md                      # ★ 开发日志（时间/进度/问题/解法）
└─ 回复.md                        # 你的原始意见（保留）
```

**数据与盘符**：`Main\data` 放小文件（C 盘）；大文件区通过**卷 GUID** 定位到 H 盘（`\\?\Volume{...}\Genkan\files`），盘符变化不影响。

---

## 6. 总体架构

```
 手机 / 电脑浏览器
        │ HTTPS
  Cloudflare Edge ──(tunnel token)── cloudflared.exe (LocalSystem)
        │ http://127.0.0.1:8080
 ┌──────┴───────────────────────────────────────────────┐
 │ Genkan.exe（nssm 服务，LocalSystem）                  │
 │                                                      │
 │  Web 层：Razor Pages + Minimal API + 静态资源          │
 │    ├─ 桌面布局（右侧导航） / 移动布局（上下栏）         │
 │    └─ 主题引擎（表 / 里 / 中性）                       │
 │                                                      │
 │  Core：认证 · 权限 · sudo 提权 · 审计 · 调度 · 日志    │
 │                                                      │
 │  Modules（可在界面挂载/卸载）                          │
 │    dashboard · chat(extend) · blog · files · routes   │
 │    backup · sync · users · sudo · shell(默认关)        │
 │                                                      │
 │  Extend 发现器：扫描 Extend\*\extend.json              │
 │    静态站点 / iframe / 反向代理 / 外部进程             │
 │                                                      │
 │  SQLite（database\genkan.db）                         │
 │  文件系统（data\ · H盘大文件区 · document\）            │
 └───────┬───────────────────────┬──────────────────────┘
         │ 子进程                 │ 外部服务
    kopia.exe / rclone.exe     Syncthing（127.0.0.1:8384）
    powershell.exe(默认关)      OpenList(5244) / DSH(61936)
```

请求链路：CF → tunnel → 回环 → 认证中间件 → 路由（本机判定）→ 权限 → 页面/接口 → 审计。

---

## 7. 界面设计（双端分离 + 主题）

### 7.1 布局
| | 桌面（≥ 900px） | 手机（< 900px） |
|---|---|---|
| 导航 | **右侧固定竖栏**（顶部站点名，图标+文字，底部用户/提权） | **顶部标题栏**（站点名+提权按钮）+ **底部标签栏**（首页/博客/文件/我的） |
| 内容 | 左侧主内容区，最大宽度 1100px | 单列全宽，卡片化，安全区内边距 |
| 操作 | 悬浮按钮 + 右键菜单 | 底部抽屉、长按菜单、下拉刷新 |
| 实现 | 两套 Razor 布局 `_Layout.Desktop.cshtml` / `_Layout.Mobile.cshtml`，服务端按 UA + 屏宽判定，CSS 断点兜底 | |

### 7.2 主题（表 / 里）
- **表（surface）**：干净、克制、信息密度高，浅色/深色两版，适合对外展示与访客。
- **里（inner）**：更个性化——背景纹理、强调色渐变、圆角更软、动效更活泼，仅站内账号可用。
- **中性**：极简无装饰，作为兜底。
- 实现：`data-theme` 属性 + CSS 变量；用户偏好存 SQLite（访客用 cookie）；主题文件独立在 `Main\website\themes\`，可新增文件夹即新增主题。

### 7.3 可用性
- 全中文文案；键盘可达；`prefers-reduced-motion` 时关动效；打印样式（博客文章）。

---

## 8. 模块设计

统一约定：每个模块 = 一个项目 + 一张启停开关 + 若干权限点 + 入口在 `Extend` 或内置。挂载/卸载在「路由与挂载」页完成。

### 8.1 AI 对话（Extend\chat，M1 首发）
- 使用官方 **chat-demo v0.1.3** 静态包，解压到 `Extend\chat\www\`，`extend.json` 声明路由 `/chat`。
- 特性（上游自带）：多会话、流式、深度思考、联网搜索、附件、语音、KaTeX/Mermaid、中英、明暗主题、移动布局。
- 与门户结合：
  - 站点账号登录 + 权限 `chat.use` 后可见；
  - 顶部注入「返回玄关」条；
  - API Key 仍由 chat-demo 自己存浏览器 localStorage（不经过我们的服务器，最省事且最安全）；
  - 可选增强（M7）：服务端 API 代理隐藏 Key（需要你提供 Key 存放方式）。
- 部署即"静态目录 + 一个清单"，也证明了 Extend 机制。

### 8.2 首页与系统状态仪表盘
- 卡片：站点状态、CPU/内存/磁盘/网络、进程 Top5、服务状态（Genkan/cloudflared/OpenList/Syncthing）、最近备份、最近登录、今日待办。
- **Windows 风格**：任务管理器式的进度条、灰底分组、Segoe UI 字体、系统图标；深色版本对齐 Win11 设置页。
- 数据来源：`PerformanceCounter`（CPU/内存/磁盘/网络）+ `Process`（进程）+ `sc query`（服务）。

### 8.3 博客与 Markdown（对外核心）
- 文章：`Main\document\blog\*.md`，front-matter（标题/日期/标签/摘要/隐藏），Markdig 渲染（表格/脚注/任务列表/代码高亮）。
- **访客只读**：只能看已发布文章与评论，不能碰任何数据接口；`blog.read` 权限对匿名开放。
- **反应**：每篇点赞 / 点踩（匿名可点，按 IP+cookie 去重，防刷）。
- **分行注释**：渲染时给每个块级元素加 `data-line` 锚点；访客可对某一行/一段提交注释，右侧抽屉展示；作者可回复与隐藏。
- **评论**：文章级评论，支持 Markdown 子集、昵称、可选邮箱；频率限制 + 关键词过滤 + 人工审核开关（默认需审核）。
- 作者侧：在线编辑、预览、发布/草稿、历史版本、阅读统计。

### 8.4 文件收发
- 站内文件库：目录、上传（拖拽）、下载（Range 断点）、预览、重命名、删除（回收站 7 天）、配额。
- **限时分享链接**：`/s/<token>`，有效期、提取码、次数上限、只读。
- **收件箱（Drop）**：`/d/<token>` 只收不看；落盘目录名为 `sha256(boxId + ts)` 前 32 位（**按你的要求用哈希做目录名**），原始文件名另存元数据，杜绝路径猜测。
- 大文件：默认走 H 盘大文件区；公网隧道仅建议 < 100MB，超出时提示改用虚拟局域网直连（第 11 节）。
- 分块上传：`init → PUT chunk → complete(sha256)`，支持断点续传与秒传。

### 8.5 路由与挂载管理（仅本机）
- 页面 `/admin/routes`：列出所有路由（内置模块 + Extend 挂载），可增删改：
  - 路径（`/chat`、`/m/openlist`）、目标类型（static / iframe / proxy / process）
  - 目标地址、权限点、图标、排序、是否显示在导航
  - 反向代理：超时、注入请求头、WebSocket 开关
- **安全边界**：仅当请求来自本机（无 `Cf-Connecting-IP`/`CF-Ray` 头，且 `RemoteIpAddress` 为回环/内网）才允许访问与保存；隧道来的一律 403。
- Extend 目录：`Extend\<id>\extend.json` 与数据库互为镜像——磁盘上有清单即可被发现，界面新增会写回磁盘。

### 8.6 备份（kopia）
- 仓库：`H:\restore`（已有，AES256-GCM，约 500GB 容量）。
- **节奏（按你的要求）**：
  - 每日 **12:00** 增量快照（kopia 快照本身始终增量+去重）；
  - 每 **10 天**一个"全量恢复点"（打 tag `full`，并做 `snapshot verify` 抽查）；
  - **保留 6 个恢复点 = 60 天**容错；中间日快照保留最近 10 天做细粒度回滚，更早的自动清理（依赖 kopia 的去重，空间占用远小于 6 份全量）。
- 备份范围（站点相关）：`Main\src`、`main\document`、`Main\depend`（排除 nuget 缓存）、`Main\website`、`Main\data`、`Main\database`、`Main\conf`、`Extend\*\extend.json`。**不备份**：`logs`、系统盘其他内容、`H:\restore` 自身。
- **网盘异地副本**：备份完成后用 rclone 把仓库镜像到网盘（目标待你选：阿里云盘/百度网盘/OneDrive/WebDAV/S3）。
- **加密可恢复、高可用**：
  - kopia 仓库密码 → 服务端用 ASP.NET Core DataProtection（密钥环在 `Main\conf\keys`）加密保存；
  - 提供「恢复包」导出：仓库地址 + 密码 + 版本号 + 恢复步骤（明文 zip，由你自行存 3 处：本机密码管理器 / 网盘 / 纸质）；
  - 仓库本身在 H 盘 + 网盘双份，密钥环随 `conf` 一起进快照（自举：恢复包 + 快照可重建整套）。
- UI：源清单、计划、立即备份、历史、浏览快照、恢复到 `Main\data\restore-staging`（绝不直接覆盖线上）、维护/校验。
- 失败处理：失败重试 2 次，仍失败则站内告警 + 音频提醒。

### 8.7 项目同步（远程编程）
- 用 **Syncthing** 做真正同步（成熟、P2P、可穿透 NAT）；
- 门户提供：安装引导、设备/文件夹管理（调 Syncthing REST API）、**"仅同步 src"模板**（自动生成 `.stignore`：忽略 `bin obj node_modules .git target dist build` 等）；
- 场景：机房上课 → 本机跑 Syncthing 接收 → 只同步项目 `src`（+ 必要配置），回到本机继续写；
- 补充 `Tools\sync-src.ps1`：一次性"推送/拉取"某个项目目录（走 ZeroTier/EasyTier 内网 IP，避免公网隧道），适合临时用；
- 大文件/整个仓库：建议直接走 ZeroTier/EasyTier + SMB 或 git over SSH，不经隧道。

### 8.8 用户、访客与权限
- 角色：
  - **站长**（admin）：全部权限 + 提权后才能做的高危操作
  - **站内账号**（member）：文件、同步、备份查看、博客写作
  - **访客**（guest，匿名）：仅 `blog.read`（看文章/评论/点赞/注释）+ 有效分享链接
- 权限点：`blog.read/write/moderate`、`files.read/write/share`、`sync.use`、`backup.view/run`、`routes.manage`、`users.manage`、`shell.exec`、`sudo.elevate`。
- 访客隔离：所有 `/api/*` 与 `/admin/*` 默认拒绝；仅匿名白名单端点（文章列表/详情/评论提交/反应/分享下载）。
- 2FA：TOTP（Otp.NET）+ 备用恢复码（10 个，一次性）。

### 8.9 临时提权（sudo 模式）
- 场景：你临时需要管理员权限（改路由、跑备份、看审计、执行命令），但平时以普通会话浏览。
- 流程：点「提权」→ 输入 TOTP 6 位码（可再叠一次邮箱/口令验证码）→ 获得 **15 分钟**临时管理员令牌（仅当前会话、仅高危操作）→ 面板显示倒计时，可提前退出。
- **音频呼叫**：提权请求与危险操作确认时，服务器用 SAPI 播报（"有人在请求管理员权限"），浏览器同时响提示音 + 桌面通知；你忙实验报告时也能听见。
- 审计：每次提权记录时间、IP、UA、结果；连续失败 5 次锁定 15 分钟。

### 8.10 远程命令（默认关闭）
- 完整实现：表单执行、超时、输出截断、审计、白名单（可配置为任意）。
- 出厂 `enabled: false`，路由不注册、导航不显示；开启需 sudo 模式 + 二次确认。
- 不提供 bash/terminal/PTY（按你的要求）。

### 8.11 日志、审计与开发日志
- 应用日志：Serilog → `Main\logs\app-YYYYMMDD.log`，按天 + 10MB 轮转，保留 30 天；错误单独 `error-*.log`。
- 审计：SQLite 表 + `logs\audit-YYYYMM.jsonl`（双写，防误删），记录登录、提权、文件删除、备份恢复、路由变更、评论审核。
- 任务日志：备份/同步/命令每次运行的输出存 `logs\jobs\<id>.log`，UI 可下载。
- **开发日志 `DEVLOG.md`**：每条 "时间 / 做到哪 / 遇到什么问题 / 怎么解决 / 待办"，每次提交附带；按你的要求"什么时间做到哪里什么问题怎么解决都记下"。

### 8.12 dsh-go 联动（方案 B，待确认）
- 手机 dsh-go 连接电脑 DSH，官方支持两条路：SSH 隧道、或 DSHarness Remote。
- 推荐：在 `Extend\dsh\` 部署 [DSHarness Remote](https://github.com/liguobao/ds-harness-remote) 服务，门户用反向代理给它一个 `/dsh` 路由 + 独立 token，手机端填 `https://你的域名/dsh` 即可连回本机。
- 备选 A：只把本机 DSH 网页（`127.0.0.1:61936`）作为「开发」板块内嵌。
- 备选 C：给 dsh-go 暴露几个 HTTP 工具（读文件/触发备份），做"AI 能操作门户"。

---

## 9. 数据模型（SQLite 主要表）

```
users(id, name, display, pwd_hash, roles, totp_secret, backup_codes, enabled, quota, created_at, last_login)
sessions(id, user_id, token_hash, ip, ua, created_at, last_seen, expires_at, sudo_until)
posts(id, slug, title, summary, content_path, status, published_at, views, likes, dislikes)
comments(id, post_id, parent_id, author, email, body, status, ip, created_at)
annotations(id, post_id, block_id, line_no, author, body, status, created_at)
reactions(id, post_id, kind, fingerprint, created_at)
files(id, owner, name, rel_path, size, sha256, mime, created_at, deleted_at)
shares(id, token, file_id, kind(share|drop), expires_at, password_hash, max_downloads, downloads)
mounts(id, route, title, kind(static|iframe|proxy|process), target, icon, permission, order, enabled, options_json)
backup_runs(id, started_at, ended_at, state, kind(daily|full), files, bytes, snapshot_id, error)
sync_rooms(id, name, local_path, st_folder_id, devices_json, ignore_rules)
routes(id, path, mount_id, kind, target, enabled, local_only, options_json)
audit(id, at, user, action, target, ip, ua, result, detail)
settings(key, value_json, updated_at)
```

---

## 10. 安全设计

1. **网络**：只绑 `127.0.0.1:8080`；大文件走虚拟局域网时另开 `0.0.0.0` 端口（可选，需防火墙规则）。
2. **认证**：无默认账号，首次运行向导；PBKDF2-SHA256 310k + 随机盐；登录限流 + 锁定；TOTP 2FA。
3. **会话**：随机 256bit，哈希落库，`HttpOnly; Secure; SameSite=Lax`；写操作校验 CSRF；可踢下线。
4. **提权**：sudo 令牌独立、15 分钟、绑定会话与 IP；高危接口同时校验角色 + sudo。
5. **访客隔离**：匿名只可访问博客只读与分享 token；代理路径不落用户输入；路径穿越、重解析点防护。
6. **路由管理**：仅本机（无 CF 头且回环/内网），防止被隧道利用改路由（SSRF 风险点）。
7. **命令**：默认关闭；开启后白名单 + 审计 + 超时 + 二次确认。
8. **审计**：不可从界面删除；日志与 SQLite 双写。
9. **备份恢复**：只恢复到 staging；恢复包导出明确提示"这是明文密钥"。

---

## 11. 大文件与网络策略

| 场景 | 通道 | 说明 |
|---|---|---|
| 页面、小文件（< 50–100MB） | Cloudflare 隧道（TCP/HTTPS） | 简单、随时可用 |
| 大文件、项目整体 | **ZeroTier / EasyTier 虚拟局域网** + SMB/HTTP | 不经公网隧道，速度快、无流量费 |
| 代码同步 | Syncthing（可跑在虚拟局域网内） | "只同步 src" |
| 备份 | 本机 H 盘 + 网盘 | 与隧道无关 |

门户在文件页检测到来源是隧道时会提示"建议改用内网直连"，并给出内网地址与二维码。

---

## 12. 备份与容灾细节

- **恢复点**：每日 12:00 快照；每 10 天打 `full` 标签并校验；保留 6 个恢复点（≈60 天）。
- **异地**：rclone 镜像 kopia 仓库到网盘（增量传输）；每日 12:00 备份后自动执行，失败告警。
- **恢复包**（`conf\recovery-kit.zip` 导出）：仓库路径 + 密码明文 + kopia 版本 + 分步恢复说明 + 校验和；提示存 3 处。
- **演练**：每季度一次"从快照恢复单个文件到 staging"的自动演练，结果写审计。
- **自举**：`conf` 与 `database` 在快照内，恢复时先取恢复包 → 装 kopia → 挂仓库 → 还原站点目录。

---

## 13. 部署与运维

- **服务**：`nssm.exe`（`C:\regser\nssm.exe`）注册 `Genkan`，`AppDirectory=Main\bin`，`--urls http://127.0.0.1:8080`，LocalSystem，日志重定向到 `Main\logs`，自动轮转；开机自启；进程退出自动重启。
- **安装脚本**：`Tools\install-service.ps1`（需管理员）、`uninstall-service.ps1`、`dev.ps1`（前台调试）。
- **构建**：`Tools\build.ps1` → 还原（缓存到 `depend\nuget`）→ 发布到 `Main\bin\new` → 冒烟测试 → 原子替换 `Main\bin` → 失败回滚。
- **升级**：保留上一版 `Main\bin.bak`；`DEVLOG` 记录版本与时间。
- **端口/域名**：Cloudflare 面板把主机名指向 `http://127.0.0.1:8080`；应用读取 `CF-Connecting-IP` 做真实 IP 审计。
- **盘符漂移**：大文件区用卷 GUID 定位；启动时若找不到则降级到 `Main\data` 并告警。
- **故障自愈**：健康检查 `/healthz`；nssm 自动重启；日志告警（可选）。

---

## 14. 里程碑（按"尽快上线"重排）

| 里程碑 | 内容 | 预估 | 可见成果 |
|---|---|---|---|
| **M0 骨架** | 目录重构、解决方案、SQLite、登录、双端布局、主题框架、日志、构建/服务脚本 | 2 天 | 能登录的空壳站点 |
| **M1 AI 对话 + 挂载** | Extend 机制、chat-demo 部署、本机路由管理页、仪表盘初版 | 1.5 天 | **`/chat` 可用**，可增删挂载 |
| **M2 博客** | Markdown 渲染、文章管理、访客只读、点赞点踩、行注释、评论 | 2 天 | 对外可看的博客 |
| **M3 文件收发** | 文件库、分块上传、分享链接、哈希收件箱、内网直连提示 | 1.5 天 | 可收发文件 |
| **M4 备份** | kopia 集成、10 日/6 份策略、网盘镜像、恢复包、恢复演练 | 1.5 天 | 每日自动备份可见 |
| **M5 同步** | Syncthing 安装与集成、"只同步 src"模板、客户端脚本 | 1 天 | 机房↔本机同步 |
| **M6 权限与提权** | 角色/权限点、sudo 模式、TOTP、音频呼叫、命令模块（关闭态） | 1.5 天 | 2FA 提权可用 |
| **M7 打磨** | dsh-go 联动、移动端细节、性能、文档、演练 | 1.5 天 | 验收清单全绿 |

合计约 **12.5 天**；M0–M2（含 AI 对话与博客）约 5.5 天即可对外。

### 验收标准（摘）
- M1：手机和电脑都能打开 `/chat` 正常对话；在 `/admin/routes` 新增一个 iframe 挂载；从隧道访问 `/admin/routes` 返回 403。
- M2：访客（未登录）能读文章、点赞、在指定行留言；无法访问任何 `/api/files`。
- M3：上传 500MB 文件成功；分享链接到期失效；收件箱目录名是哈希且不可枚举。
- M4：12:00 自动快照；第 10 天出现 `full` 标记；网盘有副本；恢复包能独立还原。
- M5：机房改一个文件，本机 30 秒内收到，且 `node_modules` 未被同步。
- M6：TOTP 提权 15 分钟后自动失效；错误码 5 次锁定；命令模块关闭时路由不存在。

---

## 15. 风险与对策

| 风险 | 对策 |
|---|---|
| 联网需逐次授权 | 批量合并下载/还原操作一次完成；依赖缓存到 `depend` |
| 访客评论/注释被刷 | 频率限制 + IP 指纹 + 审核队列（默认需审核） |
| 行注释锚点随文章更新错位 | 锚点记录块内容哈希，文章改动后旧注释标记"原文已变" |
| 隧道访问管理页（SSRF） | 强制本机判定 + 二次确认 + 审计 |
| 大文件走隧道超时 | 阈值提示 + 内网直连方案 |
| kopia 密码丢失 | 恢复包三处存放 + 文档强调 |
| 盘符变化 | 卷 GUID 定位 + 启动自检 |
| 站点被 AI 抓取/爬虫 | robots.txt + 博客限流 + 可选 CF 规则 |
| 你忙、授权不及时 | 音频/浏览器提醒 + 授权请求 15 分钟内有效 |

---

## 16. 还需你确认的 5 件事

1. **网盘**：备份异地副本用哪个？（阿里云盘 / 百度网盘 / OneDrive / 其他 WebDAV / S3）需提供挂载方式或账号。
2. **dsh-go 联动**：按方案 B（DSHarness Remote 反向通道）做，还是只要内嵌网页（方案 A）？
3. **代码托管**：GitHub 与 Gitee 的仓库地址？推送凭据怎么给（建议你建好空仓库后告诉我地址，凭据你自己配）？
4. **chat-demo 的 API Key**：继续存在浏览器（最简单，推荐），还是要服务端代理隐藏（需你提供 Key 并接受它在服务器上）？
5. **主题细节**：「里」形态你想要的个性化元素（背景图/配色/动效/字体），有没有参考图或关键词？没有的话我先做一版可切换的默认皮肤。

---

## 17. 参考项目清单（复用来源）

| 用途 | 项目 | 许可 |
|---|---|---|
| AI 对话前端 | [deepseek-ai/chat-demo](https://github.com/deepseek-ai/chat-demo) | MIT |
| Markdown | Markdig | BSD-2 |
| 数据库 | Microsoft.Data.Sqlite / SQLite | MIT / Public Domain |
| 轻量 ORM | Dapper | Apache-2.0 |
| 日志 | Serilog | Apache-2.0 |
| cron 解析 | Cronos | MIT |
| 2FA | Otp.NET | MIT |
| 文件同步 | Syncthing | MPL-2.0 |
| 网盘同步 | rclone | MIT |
| 备份 | kopia | Apache-2.0 |
| 交互 | htmx / Alpine.js | BSD-2 / MIT |
| 手机端联动 | [dsh-go](https://github.com/ccch1mneyyy/dsh-go) + [DSHarness Remote](https://github.com/liguobao/ds-harness-remote) | AGPL-3.0 / MIT |

> 所有第三方许可文件统一放 `Docs\licenses\`，随站点发布保留署名。

---

*v0.2 完。你确认第 16 节后，我按 M0 → M1 顺序开工，每步更新 `DEVLOG.md`。*
