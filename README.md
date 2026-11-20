# 情绪流形的小屋 o(*￣▽￣*)ブ

> 玄关 · Genkan —— 一台常开的 Windows 小服务器上的个人门户。
> 推开门，是各个房间：对话、博客、文件、备份、同步，以及随手挂上去的任何东西。

## 这是什么

「玄关」是一个自托管的个人站点门户，运行在本机 `127.0.0.1:8080`，经 Cloudflare Tunnel
对外提供 HTTPS 访问。它把个人服务器上零散的能力收拢成一个统一入口：

| 板块 | 用途 | 状态 |
|---|---|---|
| 门户骨架 | 登录、账号、双端布局（桌面右栏 / 手机上下栏）、表/里/中性主题 | ✅ 已跑通 |
| 对话 `/chat` | DeepSeek 对话前端（chat-demo 静态应用，需登录） | ✅ 已就位 |
| 博客 | 只读访客、文章、点赞点踩、分行注释、评论 | 待建 |
| 文件 | 上传下载、限时分享链接、哈希目录收件箱 | 待建 |
| 备份 | kopia：每日增量、每 10 天全量恢复点、留 6 份、网盘副本 | 待建 |
| 同步 | Syncthing 集成，项目只同步 `src` | 待建 |
| 仪表盘 | 仿 Windows 任务管理器的系统状态 | 待建 |
| 路由与挂载 | 可视化增删路由，仅限本机操作 | 待建 |

> 远程命令模块已设计但**出厂关闭**——安全优先，需要时在提权后手动开启。

## 设计原则

1. **复用优先**：Markdown 用 Markdig、数据库用 SQLite、日志用 Serilog、同步用 Syncthing、
   网盘用 rclone、备份用 kopia。自己只写把它们粘起来的胶水。
2. **一个进程**：`Genkan.exe` 由 nssm 注册为 Windows 服务，空闲内存目标 < 150MB。
3. **模块可挂载**：内置模块与 `Extend\` 下的外部挂载物走同一套契约，界面里可增删启停。
4. **双端分离**：桌面右侧导航栏，手机顶部标题栏 + 底部标签栏，两套布局分别维护。
5. **纵深防御**：即便访问已经过 Cloudflare，应用层仍有账号、2FA、临时提权、权限点与审计。
6. **一切可恢复**：站点自身数据进 kopia 快照，密钥有恢复包，网盘有异地副本。

## 目录一览

```
website\
├─ Main\          主站（src 源码 / document 文档 / depend 依赖 / website 静态 / data 数据 /
│                 database 数据库 / conf 配置 / logs 日志 / bin 产物）
├─ Extend\        挂载物，一个目录一个（chat/ 已是可用的对话前端）
├─ Tools\         构建、部署、同步、备份脚本
├─ Docs\          计划书、架构、运维、插件开发、许可证
├─ index.html     落地页
├─ README.md      本文件（站内「关于」页直接渲染它）
└─ DEVLOG.md      开发日志
```

## 快速开始

```powershell
# 前台调试
.\Tools\dev.ps1

# 构建
.\Tools\build.ps1

# 注册为 Windows 服务（需管理员）
.\Tools\install-service.ps1
```

访问 <http://127.0.0.1:8080>。

## 技术栈

.NET 10 / ASP.NET Core · Razor Pages · Minimal API · htmx · Alpine.js · Markdig ·
SQLite · Dapper · Serilog · Cronos · Otp.NET · Syncthing · rclone · kopia · nssm

## 许可与致谢

本项目自有代码以 MIT 发布。第三方组件保留各自许可，见 `Docs\licenses\`。

特别感谢：[chat-demo](https://github.com/deepseek-ai/chat-demo)、
[OpenList](https://github.com/OpenListTeam/OpenList)、
[kopia](https://kopia.io/)、[Syncthing](https://syncthing.net/)、
[Cloudflare Tunnel](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/)。

---

*欲知进度与踩坑记录，请看 [DEVLOG.md](DEVLOG.md)；完整设计见 [Docs/PLAN.md](Docs/PLAN.md)。*
