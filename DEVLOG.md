# 开发日志 DEVLOG

> 规则：每次动手都追加一条，写清 **时间 / 做到哪 / 遇到什么问题 / 怎么解决 / 下一步**。
> 时间统一用本机时间（Asia/Shanghai，UTC+8）。倒序排列，最新在上。

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
