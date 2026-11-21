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
| 对话 | `/chat/` | chat-demo 静态前端，登录后使用，API Key 存在你自己的浏览器 |
| 博客 | `/blog` | 访客只读；点赞点踩、行级注释、评论审核；站长可在 `/write` 用 Markdown 写作并实时预览 |
| 文件 | `/files` | 上传（单文件 512MB）、下载（Range 断点）、限时分享链接（提取码/次数上限）、哈希收件箱 |
| 服务器文件 | `/files/server` | 只读浏览白名单目录，逐段校验路径防穿越 |
| 仪表盘 | `/dashboard` | 仿任务管理器：CPU/内存/磁盘/网络/进程/服务，2 秒刷新 |
| 备份 | `/backup` | kopia：每天 12:00 快照、满 10 天打 full 恢复点、快照列表、恢复到 staging、恢复包导出 |
| 同步 | `/sync` | Syncthing 状态/文件夹/设备；「只同步 src」的 `.stignore` 模板；一次性同步脚本 |
| 路由与挂载 | `/admin/routes` | 增删挂载（static / iframe / link），**仅本机可改**，保存即生效 |
| 关于 | `/about` | 就是本文件 |

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

## 技术栈（实际用的）

.NET 10 / ASP.NET Core（Razor Pages + Minimal API）· Markdig · SQLite + Dapper ·
Serilog · kopia · Syncthing · nssm · Cloudflare Tunnel · 纯手写 CSS/JS（无前端框架、无构建步骤）

## 已知限制

- 单文件上传上限 512MB，还没有分块续传；大文件建议走 ZeroTier/EasyTier 内网直连。
- 备份的自动保留删除只做预览，删旧快照仍需手工确认。
- 同步、提权、2FA、主题特效等 M6–M8 内容尚未完成（见 `Docs/PLAN-v0.3-ADDENDUM.md`）。

## 许可与致谢

自有代码以 MIT 发布；第三方组件保留各自许可。

特别感谢：[chat-demo](https://github.com/deepseek-ai/chat-demo)、
[OpenList](https://github.com/OpenListTeam/OpenList)、[kopia](https://kopia.io/)、
[Syncthing](https://syncthing.net/)、[Cloudflare Tunnel](https://developers.cloudflare.com/cloudflare-one/)。

---

*进度与踩坑见 [DEVLOG.md](DEVLOG.md)；设计见 [Docs/PLAN.md](Docs/PLAN.md)；待办见 [Docs/进度-早晨.md](Docs/进度-早晨.md)。*
