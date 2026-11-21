# 玄关 v0.3 增补（对应 `回复2.md`）

> 基线：`Docs/PLAN.md`（v0.2）。本篇只记录**变化与新增决策**，不重复已确认的内容。
> 日期：2026-11-20 23:50

---

## 1. 玄关 = 公开欢迎页

- `/` 由服务渲染，**任何访客**第一眼看到的就是玄关：站点名、一句话、入口卡片、在线状态。
- 已随 M0.1 上线：`Main\website\pages\home.html` + `assets\css\home.css` + `assets\js\home.js`。
- 未登录访客看到"登录"入口；站内账号看到自己的快捷入口（M0.2 补）。

## 2. 「里」主题增强（M2 / M7）

| 能力 | 实现方式 |
|---|---|
| 背景图上传（含 NSFW 二次元） | **只存私密目录** `Main\data\private\{userId}\bg\`，经 `/media/private/{id}` 鉴权下发，响应头 `Cache-Control: private, no-store`；不进静态目录、不生成公开 URL、访客一律 403、不进分享/导出；可选"模糊到悬停" |
| 滑动特效 | `IntersectionObserver` 进场，逐卡片位移/淡入 |
| 点击特效 | canvas 涟漪/粒子，可在设置里关；尊重 `prefers-reduced-motion` |
| 字体 | 内置几套系统中文字体 + 字重/字号/行高；后续支持上传字体（同样私密存储） |
| 配色 | 主色/强调色/背景色自定义，存用户偏好；预设「表 / 里 / 中性」 |
| 图标 | 扁平 SVG 一套 + 位图（PNG）一套，放 `Main\website\themes\icons\`，主题声明用哪套 |

> 底线：私密背景只服务于你自己，不做公开图床；`/media/private/*` 永远要求登录 + 所有者匹配。

## 3. 评论审核交给 Qwen 小模型

- 走 DashScope 的 OpenAI 兼容接口，默认 `qwen-turbo`，可切 `qwen-plus`。
- 配置：`Main\conf\appsettings.json` → `moderation: { provider:"qwen", baseUrl, model, apiKeyRef }`；Key 用 DataProtection 加密存 `conf\keys`。
- 流程：提交 → 本地规则快筛（长度/链接/重复/频率）→ 模型返回 `{allow|review|block, reason}` → 存 `comments.moderation`；`review` 进人工队列，`block` 直接拒。
- 降级：无 Key / 超时 / 报错 → **全部进人工队列**，绝不放行。
- 提示词与阈值做成可配置项，方便你以后换模型。

## 4. 日志 90 天 + 进入备份

- Serilog 保留 **90 天**（已生效：`retainedFileCountLimit: 90`，单文件 10MB 轮转）。
- kopia 源清单**新增** `Main\logs`（v0.2 里是排除的），备份节奏不变。

## 5. 错误自诊断（去重 → 交给模型）

- 每小时聚合 `Error`/`Warning`：规范化为指纹（去掉时间戳、路径、GUID、纯数字），计数，取**最近 10 个不同指纹**。
- 调用模型生成"可能原因 / 排查步骤 / 涉及文件"，结果存 `diagnostics` 表，在「诊断」页展示。
- 关于「发送到 chat-demo 特定会话」：chat-demo 的会话存在**浏览器 IndexedDB**，服务端无法可靠写入。因此先做「一键复制成对话文本 → 你粘进指定会话」；M8 若 fork chat-demo 做同步（第 9 节），则可直接推成会话。
- 可作为定时任务，也可在错误突增时触发（阈值可配）。

## 6. 远程命令 → **命令提示器**（不执行）

- 完全没有执行能力、没有 shell/PTY/ConPTY。
- 数据：**tldr-pages 中文页**（CC-BY-SA）导入 `Main\depend\tldr`，建全文索引。
- 交互：输入命令名或自然语言 → 返回：命令、一句话说明、**语法格式**、常用参数、2–3 个示例、适用平台。
- 可选：接模型做"我该用哪个命令"的推荐（仍不执行）。
- 权限：登录即可，只读，无需提权。

## 7. Docs 阅读鉴权

- front-matter：`visibility: public | member | private | password`。
- 访客只能读 `public`；列表、搜索、直链、RSS 全部按同一规则过滤；`private` 仅站长。
- 支持单篇密码（临时给朋友看某篇）。

## 8. 文件模块新增「服务器文件」

- 只读浏览 + 下载**本机文件**（如 H 盘素材、G 盘文档）。
- 沙箱：`files.roots` 白名单（例：`G:\文档`、`H:\`）；路径规范化、拒绝重解析点、禁止 `..` 逃逸；只读；每次访问审计；大文件与速率限制。
- 大文件仍建议走 H 盘 + ZeroTier/EasyTier 内网直连，公网隧道只做小文件。

## 9. chat-demo 多端同步（只同步你本人的对话）

| 方案 | 做法 | 评价 |
|---|---|---|
| **A（推荐）** | 轻量 fork 到 `Extend\chat-src`，加一个 `sync` 服务：登录后把会话/消息推到 `/api/chat/sync`（SQLite），另一台登录后拉回 | 真正多端同步；改动集中、可跟随上游合并；需要 pnpm 构建 |
| B | 注入适配器脚本 monkey-patch IndexedDB 后台上报 | 零 fork，但脆弱，仅当"备份"用 |
| C | 用 chat-demo 自带历史导出 ZIP，传到门户，另一台导入 | 最省事，但手动 |

> 先上静态版（已完成）；同步放 **M8**，按方案 A 实施，其他访客不受影响。

## 10. dsh 代理站长（本机 Agent API）

- 提供 `/agent/*` REST 接口 + `Main\conf\agent.token`（首次启动生成）。
- 能力：读状态、发布/修改文章、改路由与挂载、触发备份、查看审计。
- 限制：**仅本机**（无 `Cf-Connecting-IP`/`CF-Ray` 且回环）、token 鉴权、全量审计、高危操作另需 sudo 令牌。
- dsh-go 的远程连接仍走它原生方式（SSH / DSHarness Remote），网站不承担该流量。

## 11. 网盘异地副本

- 采纳调研结论（`Docs/CLOUD-BACKUP.md`）：kopia 仓库不直接建在网盘上，只做字节级镜像。
- **已定（2026-11-21）：先用 Cloudflare R2 试水**。站点数据约 1GB，落在 R2 免费 10GB 额度内；个人几十 GB 数据暂不上云，或按量付费（约 $0.015/GB·月）。
- 已知前提：Cloudflare 账单需要可用的国际支付方式（Visa/Mastercard；银联单币卡可能不行），你手上的 PayPal 是否可用需实测。
- 运维窗口：学校供电/网络 24h 可用；**12:00 与 18:00 之后**通常有人，备份时间仍定 12:00（正好落在你在场的半小时）。

## 12. 代码托管

- `origin` = GitHub、`gitee` = Gitee，已配置并指向你给的两个空仓库。
- 首次 `git push` 需要你在本机完成一次凭据授权（凭据只存本机，我不接触你的 token）。

## 13. 里程碑更新

| 阶段 | 内容 | 状态 |
|---|---|---|
| M0.1 | 站点跑通：玄关、`/chat/`、`/healthz`、日志、构建/服务脚本 | ✅ 2026-11-20 |
| M0.2 | SQLite + 登录 + 双端布局 + 主题切换 | ✅ 2026-11-21 |
| M1 | 路由与挂载管理页、Windows 风格仪表盘 | ✅ 2026-11-21 |
| M2 | 博客 + Markdown + 评论/点赞/行注释 + Qwen 审核 | ✅ 2026-11-21 |
| M3 | 文件收发 + 哈希收件箱 + 服务器文件沙箱 | ✅ 2026-11-21 |
| M4 | kopia 备份（日增量/10 日全量/6 份/网盘/恢复包） | ✅ 2026-11-21（网盘镜像待接 R2） |
| M5 | Syncthing 同步 + 仅 src 模板 | ✅ 2026-11-21（Syncthing 本体需管理员安装） |
| M6 | 权限点 + sudo 提权 + TOTP + 音频呼叫 + 命令提示器 | 下一步 |
| M6 | 权限点 + sudo 提权 + TOTP + 音频呼叫 + 命令提示器 | |
| M7 | 主题打磨（特效/字体/图标/背景）、移动端细节 | |
| M8 | chat-demo 多端同步、错误自诊断、Agent API | |

## 14. M0.1 实际完成情况（2026-11-20 23:45）

- `Genkan.exe` 跑通：`/`（玄关）、`/chat/`（对话）、`/healthz`、`/api/extends`；冒烟测试 4/4 通过。
- 日志落 `Main\logs\app-20261120.log`，90 天轮转，结构化输出。
- 脚本齐备：`Tools\build.ps1`、`dev.ps1`、`install-service.ps1`、`uninstall-service.ps1`、`smoke-test.ps1`。
- 发布产物 **0.7MB**（框架依赖，.NET 10 运行时已装）。
- 已知事项：PowerShell 脚本必须存为 **UTF-8 with BOM**，否则 PS 5.1 按 ANSI 解析会导致中文注释把语法搞坏（已修）。
