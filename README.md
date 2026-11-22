# 情绪流形的小屋 o(*￣▽￣*)ブ

> 玄关 · Genkan —— 一台常开的 Windows 小服务器上的个人门户。
> 推开门，是各个房间：对话、博客、文件、备份、同步，以及随手挂上去的任何东西。

## 这是什么

「玄关」是一个自托管的个人站点门户，运行在本机 `localhost:8080`，经 Cloudflare Tunnel
对外提供 HTTPS 访问。它把个人服务器上零散的能力收拢成一个统一入口，自己只写"把成熟工具粘起来"的胶水。

## 现在能用的

| 板块 | 地址 | 说明 |
|---|---|---|
| 玄关 | `/` | 公开欢迎页，双端布局（桌面右侧导航 / 手机上下栏），表·里·中性三套主题 |
| 对话 | `/ai` | 原生对话页：10 家供应商（DeepSeek/OpenAI/Kimi/GLM/通义/硅基/OpenRouter/Claude/Gemini/本地 Ollama）、流式输出、统一余额、Key 加密托管、对话存成服务器上的 JSON 文件，可导出 Markdown |
| 临时提权 | `/elevate` | 15 分钟提权；开了 TOTP 用验证码，否则用密码；删快照/恢复/导出恢复包需要它 |
| 博客 | `/blog` | 访客只读；Ctrl+F 文内搜索、点赞点踩、行级注释、评论审核；站长可在 `/write` 用 Markdown 写作并实时预览 |
| 文件 | `/files` | 上传（单文件 512MB）、下载（Range 断点）、限时分享链接（提取码/次数上限）、哈希收件箱 |
| 服务器文件 | `/files/server` | 只读浏览白名单目录，逐段校验路径防穿越 |
| 仪表盘 | `/dashboard` | 仿任务管理器：CPU/内存/磁盘/网络/进程/服务，2 秒刷新 |
| 备份 | `/backup` | kopia：每天 12:00 快照、满 10 天打 full 恢复点、快照列表、恢复到 staging、恢复包导出 |
| 同步 | `/sync` | Syncthing 状态/文件夹/设备；「只同步 src」的 `.stignore` 模板；一次性同步脚本 |
| 命令提示 | `/console` | 查命令用法（ssh、端口、同步、压缩…），**只给提示、不执行**；`/api/console/hints` 可给别处调用 |
| 路由与挂载 | `/admin/routes` | 增删挂载（static / iframe / link），**仅本机可改**，保存即生效 |
| 关于 | `/about` | 就是本文件 |
| 图墙 | `/gallery` | 瀑布流 + 无限滚动；只接 SFW 图源（konachan.net safe / safebooru），服务端代理取图 |
| 笔记 | `/notes` | 自己的 .md：网页里写、导入已有文件，存在 `Main\document\notes`，拷走就能用 |
| 剪贴板 | `/clip` | 跨设备存一段文字，手机上复制、电脑上再点一下 |
| 代理站长 | `/steward` | AI 助手：查体检、读写笔记、读博客、存草稿、看文件、塞剪贴板；不碰删除与恢复 |
| 账号管理 | `/admin/users` | 管理员：建号、改角色、启用/禁用、重置密码、删除；禁用即刻踢下线 |
| 自诊断 | `/diagnostics` | 管理员：一次检查数据库、磁盘、备份、R2、同步、AI、日志，给出正常/注意/失败 |
| 我的 | `/me` | 账号、二次验证（TOTP）、临时提权、外观偏好（里主题特效/字体/背景图） |

## 设计原则

1. **复用优先**：Markdown 用 Markdig，数据用 SQLite + Dapper，日志用 Serilog，
   备份用 kopia，同步用 Syncthing，服务用 nssm，隧道用 Cloudflare。自己只写胶水。
2. **一个进程**：`Genkan.exe`，空闲内存约 80–90MB。
3. **模块可挂载**：内置模块与 `Extend\` 下外部挂载物同一套约定，管理页里增删启停。
4. **双端分离**：桌面右侧固定导航；手机顶部标题栏 + 底部标签栏，各自维护。
5. **纵深防御**：隧道之外仍有账号、权限点、仅本机限制、审计日志。
6. **一切可恢复**：站点数据进 kopia 快照，密钥有恢复包，随时能拉回 staging。

## 目录一览

```
website\
├─ Main\          主站：src 源码 / document 文档 / depend 依赖 / website 静态资源 /
│                 data 运行数据 / database SQLite / conf 配置 / logs 日志 / bin 产物
├─ Extend\        挂载物，一个目录一个（chat/ 就是对话前端）
├─ Tools\         构建、部署、备份、同步、修密码等脚本
├─ Docs\          计划书、进度、运维手册
├─ index.html     本地占位跳转页
├─ README.md      本文件（站内「关于」直接渲染）
└─ DEVLOG.md      开发日志（时间 / 做到哪 / 问题 / 解法）
```

## 快速开始

```powershell
# 前台调试
.\Tools\dev.ps1

# 构建（发布到 Main\bin）
.\Tools\build.ps1

# 注册为 Windows 服务（需管理员）
.\Tools\install-service.ps1
```

首次启动：浏览器打开 <http://127.0.0.1:8080> → 自动跳到 `/setup` → 建站长账号。

## 常用工具

| 脚本 | 用途 |
|---|---|
| `Tools\reset-password.ps1 -UserName manifld` | 忘记密码时在本机重置 |
| `Tools\snapshot-data.ps1` / `restore-data.ps1` | 给数据库、上传文件、文档拍快照 / 还原（动数据前先跑） |
| `Tools\install-syncthing.ps1` | 装 Syncthing 并注册服务，打印 API Key（管理员） |
| `Tools\sync-src.ps1 -Project ... -Remote ...` | 一次性同步项目的 `src`（robocopy 或 scp） |
| `Tools\e2e-m1..m4.ps1` | 各模块端到端测试 |
| `Tools\smoke-test.ps1` | 健康检查 + 静态资源冒烟 |

## 需要手动完成的事

- `/backup` 里填一次 kopia 仓库密码（`H:\restore` 那个），每天 12:00 的计划任务才会成功。
- `Tools\install-syncthing.ps1` 跑一次，把 API Key 填进 `conf/appsettings.json` 的 `sync:syncthing:apiKey`。
- 想让备份有异地副本：给 rclone 配好网盘（计划是 Cloudflare R2），目前只有本机一份。

## 内网直连（比隧道快）

站点默认监听 `0.0.0.0:8080`，同一局域网或虚拟局域网（ZeroTier / EasyTier）里的设备可以直连，
不经过 Cloudflare，速度取决于本地链路。

1. 管理员跑一次防火墙放行（只对「专用网络」开放）：
   ``powershell
   powershell -ExecutionPolicy Bypass -File .\Tools\open-lan-firewall.ps1
   ``
2. 手机装 ZeroTier / EasyTier，加入同一个网络。
3. 访问 `http://<虚拟网卡IP>:8080`，IP 可以在 `/diagnostics` 的「直连地址」里看到。

注意：`/admin/*` 仍然只允许本机回环访问，直连进来只能看公开页面和自己账号的内容。
Cloudflare 隧道继续给不在虚拟网络里的访客用。

## 技术栈（实际用的）

.NET 10 / ASP.NET Core（Razor Pages + Minimal API）· Markdig · SQLite + Dapper ·
Serilog · kopia · Syncthing · nssm · Cloudflare Tunnel · 纯手写 CSS/JS（无前端框架、无构建步骤）

## 已知限制

- 上传超过 4MB 会自动分块（2MB/块）以穿过 Cloudflare 的 100 秒限制；上行很慢时仍需要耐心。
- 备份的保留策略可一键清理，但不会自动定时删（避免误删）。
- 同步、提权、2FA、主题特效等 M6–M8 内容尚未完成（见 `Docs/PLAN-v0.3-ADDENDUM.md`）。

## 许可与致谢

自有代码以 MIT 发布；第三方组件保留各自许可。

特别感谢：[chat-demo](https://github.com/deepseek-ai/chat-demo)、
[OpenList](https://github.com/OpenListTeam/OpenList)、[kopia](https://kopia.io/)、
[Syncthing](https://syncthing.net/)、[Cloudflare Tunnel](https://developers.cloudflare.com/cloudflare-one/)。

---

*进度与踩坑见 [DEVLOG.md](DEVLOG.md)；设计见 [Docs/PLAN.md](Docs/PLAN.md)；待办见 [Docs/进度-早晨.md](Docs/进度-早晨.md)。*
