# kopia 备份异地镜像到网盘：调研与落地手册

> 场景：Windows 10 个人服务器，kopia v0.23.1，仓库为 **filesystem 类型**、位于 `H:\restore`（AES256-GCM，500GB 容量，当前空仓库）。
> 目标：每天把整个 kopia 仓库**原样镜像**到网盘一份，保留 kopia 的加密与去重，成本接近 0。
> **结论先行：首选「腾讯云 COS（S3 兼容）」，备选「Cloudflare R2」。明天就要上线 → 直接看第 8 节。**

---

## 0. 决定（2026-11-21，用户拍板）

**先用 Cloudflare R2 试水。**

| 项 | 结论 |
|---|---|
| 镜像范围 | 只镜像站点相关数据（`Main\src`、`document`、`depend`、`website`、`data`、`database`、`conf`、`logs` + `Extend\*` 清单），当前约 **1GB** |
| 免费额度是否够 | R2 免费 **10GB 存储** + 100 万次 A 类请求/月 + **零出站流量费**；1GB 站点 + 多版本余量充足 |
| 个人数据（几十 GB） | **不要塞进免费额度**。若以后也要上云，按 $0.015/GB·月 估算，50GB ≈ $0.75/月；更现实的做法是只备份站点，个人大文件走本地/移动硬盘 |
| 前置条件 | ① 已有 Cloudflare 账号（隧道在跑）② **可用的国际支付方式**：Cloudflare 账单要 Visa/Mastercard 等，单币种银联卡可能绑不上，PayPal 在部分地区可用，**需实测** |
| 落地参数 | 照第 8 节流程，把 COS 换成 R2：`rclone config` → `s3` → provider=`Cloudflare` → endpoint=`https://<accountid>.r2.cloudflarestorage.com` → region=`auto` |

> 你有长期机场账号，配置 R2 控制台时用得上；但**日常备份流量别走机场**（大流量费机场额度），R2 本身免出站费，直连即可。

## 1. 一句话结论

| | 方案 | 为什么 |
|---|---|---|
| **首选** | 腾讯云 COS，rclone `s3` 后端，每天 `rclone sync` | 国内直连最快最稳、rclone 官方一等支持、新用户 50GB 免费 6 个月、之后每月约 1~3 元，学生完全负担得起 |
| **备选** | Cloudflare R2，rclone `s3` 后端 | 10GB 存储**永久**免费、无出站流量费、无 6 个月到期的坑；代价是要国际支付方式，国内速度需实测 |
| 零成本兜底 | 现有 OpenList → WebDAV | 一分钱不花，但依赖第三方网盘接口，token 会过期、有风控/限速，**只适合当第二副本** |

## 2. 先搞清楚三件事（否则会走错路）

1. **不要把 kopia 仓库直接"建"在网盘上。** kopia 的 rclone 后端官方标注为 `[Not maintained]`（不再维护）。正确做法是：仓库继续留在 `H:\restore`，用 rclone 把**目录字节级镜像**到云端。镜像副本就是完整仓库，恢复时下载回来、用**同一个密码**即可打开。
2. **kopia 的 blob 是内容寻址、写后不改**（文件名是哈希）。所以"只增不改"的镜像天然安全，也不会破坏去重。
3. **`rclone sync` 会删除云端多余文件**——如果本地 `H:` 盘某天挂了或目录被清空，一次 sync 会把云端也清空。**必须加 `--max-delete`**（见第 6 节），这是本方案唯一的高危操作。

## 3. 全方案对比（先看这张表）

| 方案 | rclone 接入方式 | 免费额度（出处见第 10 节） | 中国网络实际可用性 | 运维复杂度 | 推荐指数 |
|---|---|---|---|---|---|
| **腾讯云 COS** | `s3`，provider=`TencentCOS` | 新用户 50GB 标准存储 6 个月，之后按量（约 ¥1~3/月 @10GB，以官网计费为准） | ★★★★★ 国内直连，上传快 | ★★ | ★★★★★ |
| **Cloudflare R2** | `s3`，provider=`Cloudflare` | 10GB 存储 + 100 万次 A 类请求/月，**永久**免费 | ★★★☆ 通常可用，速度波动，需实测 | ★★ | ★★★★☆ |
| Backblaze B2 | `b2` | 首 10GB 永久免费；$6.95/TB/月；出站 ≤3× 存储量免费 | ★★★ 可用但慢，需实测 | ★★ | ★★★ |
| 阿里云 OSS | `s3`，provider=`Alibaba` | 无长期免费额度（新用户试用以官网为准） | ★★★★★ 国内直连 | ★★★ | ★★★☆ |
| 七牛 Kodo | `s3`，provider=`Qiniu` | 标准存储 10GB/月 + 回源流量 10GB/月 + PUT/DELETE 10 万次/月 | ★★★★ 国内直连 | ★★★ | ★★★ |
| OneDrive（个人版） | `onedrive`（OAuth 网页授权） | 5GB | ★★ 常需代理，授权流程要浏览器 | ★★★ | ★★★ |
| Google Drive | `drive`（OAuth） | 15GB（与 Gmail/相册共享） | ★ 大陆不可直连，**排除** | ★★★★ | ★ |
| 坚果云 | `webdav` | 免费版每月上传 1GB / 下载 3GB | ★★★★★ 国内直连，官方 WebDAV | ★★ | ★★★ |
| 自建 WebDAV（含 ZeroTier 对端） | `webdav` | 取决于你的另一台机器/硬盘 | ★★★★ 看线路 | ★★★★ | ★★ |
| 阿里云盘（经 OpenList） | `webdav` 中转 | 容量大但**官方授权通道已停**，需自建授权服务 | ★★★ 不稳定 | ★★★★★ | ★ |
| 百度网盘 / 夸克 / 115（经 OpenList） | `webdav` 中转 | 百度约 305GB（第三方站数据，需核实）；夸克/115 容量小 | ★★ 非会员上传限速、风控、token 会过期 | ★★★★★ | ★ |

**为什么 OneDrive 排不到前面**：5GB 免费空间对"几百 MB~几 GB 源码 + 多版本"太紧；rclone 配置 OneDrive 要开浏览器做 OAuth，国内常需代理，对一个"不懂网盘细节"的用户是负担。
**为什么国内网盘经 OpenList 排最后**：能跑通，但不是给"每天自动备份"用的（详见第 5 节）。

## 4. 三条技术路线的关键差异

| | S3 兼容对象存储（COS/R2/B2/OSS/七牛） | 官方 API 网盘（OneDrive/Drive） | WebDAV 中转（坚果云/OpenList/自建） |
|---|---|---|---|
| 认证方式 | 一对 AccessKey，**无需浏览器**，适合无人值守 | OAuth，需要浏览器点一次，token 会过期需重连 | 用户名+密码/App 密码 |
| 修改时间 | 支持（存在对象元数据里） | 支持 | **不支持**（rclone 只能拿到服务器时间） |
| 校验和 | 有 MD5/ETag，可用 `--checksum` | 有 hash | 无 hash，只能 `--size-only` |
| 上传大文件 | 分片，稳 | 分片，稳 | 单次 PUT 或分片，容易断 |
| 长期风险 | 低（付钱即稳定） | 中（容量小、可能被限流） | **高**（接口随时变） |

> 结论：**备份写入端优先选 S3 兼容对象存储**，它是唯一"认证不过期、有校验和、断点重试可靠"的一类。

## 5. 专项评估：用现有 OpenList 当 WebDAV 中转，可行吗？

**能跑通，但不建议做唯一异地副本。** 你机器上 OpenList v4.2.9 已在 `http://127.0.0.1:5244` 运行，WebDAV 默认入口是 `http://127.0.0.1:5244/dav/`（端口与网页端一致，默认 5244；用户名密码就是 OpenList 后台的账号）。

**确认 WebDAV 是否可用的方法**（只读检查，不改系统）：
1. 浏览器打开 `http://127.0.0.1:5244/dav/`，出现 401/登录框或 405 说明服务在；
2. 或 PowerShell：`curl.exe -u 用户名:密码 -X PROPFIND "http://127.0.0.1:5244/dav/" -H "Depth: 1"`，返回 XML 即正常；
3. 端口被改过就查 `netstat -ano | findstr 5244`，或在 OpenList 后台「设置 → 站点」里看实际端口。

| 优点 | 缺点 |
|---|---|
| 零成本、零新增软件（省掉一个组件） | **单点故障**：OpenList 挂了/升级失败，备份静默失败 |
| 一个入口统一挂多网盘，换网盘不用改 rclone 配置 | **token/cookie 会过期**，通常几周到几个月，过期后备份失败且不报错给用户 |
| 明文流量只在本机 loopback，不出网 | **不支持修改时间**，rclone 必须加 `--size-only`，否则每次都全量重传 |
| kopia 数据本身已加密，网盘只看到密文和哈希文件名 | 走网盘厂商的非官方接口：**限速、风控、封号风险**都由用户承担 |
| | 阿里云盘/115 的 OpenList 授权通道已停止，需自建授权服务；个人开发者 API 申请已暂停 |
| | 上传走网盘客户端协议，几 GB 首次同步可能要好几天 |

**结论**：OpenList 适合当"第三份便宜副本"或"应急手动上传通道"；**不适合当每日自动备份的主链路**。如果只想花 0 元，可以先用它，但要接受"哪天不跑了你不知道"。

## 6. 首选方案落地步骤（腾讯云 COS）

### 6.1 准备（约 10 分钟）
1. 注册腾讯云 → 实名认证 → 开通「对象存储 COS」→ 领取新用户免费资源包（50GB / 6 个月）。
2. 创建存储桶：地域选离你近的（如 `ap-guangzhou`/`ap-shanghai`），**访问权限选"私有读写"**，记下桶名，例如 `kopia-mirror-1300000000`。
3. 在「访问管理 CAM → API 密钥管理」创建子账号密钥，**只授予该桶的读写权限**（不要用主账号密钥）。
4. 装 rclone：`scoop install rclone`，然后 `rclone version` 确认。

### 6.2 配置 remote（一条命令，不需要交互）
```powershell
rclone config create cos s3 provider=TencentCOS `
  access_key_id=AKIDxxxxxxxx secret_access_key=xxxxxxxx `
  endpoint=cos.ap-guangzhou.myqcloud.com region=ap-guangzhou acl=private
rclone lsd cos:            # 能列出桶就说明通了
rclone mkdir cos:kopia-mirror/restore
```
> 其他家同理：R2 用 `provider=Cloudflare endpoint=https://<账号ID>.r2.cloudflarestorage.com region=auto`；B2 用 `type=b2`；OSS 用 `provider=Alibaba`；七牛用 `provider=Qiniu`。endpoint 以各家控制台为准。

### 6.3 镜像命令（这是核心，请逐字用）
```powershell
rclone sync "H:\restore" "cos:kopia-mirror/restore" `
  --size-only --fast-list --transfers 4 --checkers 8 `
  --max-delete 50 --bwlimit 4M --retries 3 --low-level-retries 10 `
  --log-level INFO --log-file "C:\Logs\rclone-kopia-$(Get-Date -f yyyyMMdd).log"
```
参数为什么要这么写：

| 参数 | 作用 |
|---|---|
| `--size-only` | kopia blob 写后不改，比大小足够；**WebDAV 后端必须加**（拿不到修改时间） |
| `--fast-list` | 对象存储下一次列全，省请求次数 |
| `--max-delete 50` | **保险丝**：本次要删超过 50 个文件就中止，防止本地盘挂了把云端也清空 |
| `--bwlimit 4M` | 限速 4MB/s，避免家用上行被打满，站点卡顿 |
| `--retries 3` | 网络抖动自动重试 |

第一次跑建议加 `--dry-run` 先看清单，再正式跑；首轮同步几 GB 建议放在夜里手动跑一次。

### 6.4 每天自动跑（二选一）

**A. 计划任务（简单，要求用户已登录）**：写一个 `C:\Users\manifld\bin\kopia-mirror.ps1`：
```powershell
$log = "C:\Logs\kopia-mirror-$(Get-Date -f yyyyMMdd).log"
& rclone sync "H:\restore" "cos:kopia-mirror/restore" --size-only --fast-list `
  --transfers 4 --checkers 8 --max-delete 50 --bwlimit 4M --retries 3 --log-level INFO --log-file $log
$rc = $LASTEXITCODE
"$(Get-Date -f s) exit=$rc" | Add-Content "C:\Logs\kopia-mirror-status.txt"
if ($rc -ne 0) { "FAIL $rc" | Add-Content "C:\Logs\kopia-mirror-ALERT.txt" }
```
```powershell
schtasks /Create /TN "kopia-mirror-daily" /SC DAILY /ST 03:30 /F `
  /TR "powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\Users\manifld\bin\kopia-mirror.ps1"
```

**B. nssm 服务（无人登录也能跑，你已经有 `C:\regser\nssm.exe`）**：把上面的 ps1 改成 `while ($true) { 执行; Start-Sleep 86400 }`，然后
```powershell
C:\regser\nssm.exe install kopia-mirror C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe `
  "-NoProfile -ExecutionPolicy Bypass -File C:\Users\manifld\bin\kopia-mirror.ps1"
```
注意：服务以 SYSTEM 运行，rclone 配置文件要显式指定 `--config C:\Users\manifld\AppData\Roaming\rclone\rclone.conf`（用 `rclone config file` 查真实路径）。

### 6.5 顺序与时序（很重要）
- 快照 → 维护 → 镜像，**三者错开至少 30 分钟**，镜像时不要有 kopia 正在写仓库。
- 每周手动/定时跑一次 `kopia maintenance run --full`，再跑镜像，避免云端长期残留已过期 blob。
- 镜像任务失败告警：最低成本方案是"写状态文件"，让站点首页/健康检查页读 `C:\Logs\kopia-mirror-status.txt` 显示"上次同步时间 + 结果"；想要主动推送，再挂 Server 酱/邮件。

### 6.6 校验与恢复演练（别跳过）
```powershell
rclone check "H:\restore" "cos:kopia-mirror/restore" --size-only --one-way   # 每周
rclone copy "cos:kopia-mirror/restore" "D:\restore-from-cloud"              # 每季度真恢复一次
kopia repository connect filesystem --path "D:\restore-from-cloud"
kopia snapshot list          # 能看到快照 = 异地副本真的可用
```
> 只验证"文件传上去了"没有意义，**能 connect + 能列出快照**才算备份成功。

## 7. 其他方案的实操要点（简述）

- **Cloudflare R2**：注册 → 开启 R2 → 建桶 → 创建 API Token（Object Read & Write）→ 用 6.2 的 R2 配置。要点：账号需绑支付方式（免费额度内不扣费）；国内访问 `*.r2.cloudflarestorage.com` 速度需实测，慢的话加 `--transfers 2`。
- **OneDrive**：`rclone config` 交互式，选 `onedrive`，`client_id` 留空（用内置 global），浏览器授权需能访问微软登录页（可能要开代理）。5GB 上限务必先用 `rclone size "H:\restore"` 估体积。
- **坚果云**：`rclone config create nutstore webdav url=https://dav.jianguoyun.com/dav/ vendor=other user=邮箱 pass=$(rclone obscure "应用密码")`。**免费版每月只能上传 1GB**，首次全量同步会卡住，只适合仓库 < 800MB 且几乎不增长的情况。
- **国内网盘（阿里云盘/百度/夸克/115）**：`rclone config create openlist webdav url=http://127.0.0.1:5244/dav vendor=other user=admin pass=$(rclone obscure "密码")`。必须加 `--size-only`、`--transfers 1~2`、`--max-delete`，并接受：token 会过期、非会员上传被限速、阿里云盘第三方并发 >10 会触发风控。

## 8. 如果明天就要上线：最小可行方案（约 15 分钟）

> 前提：能实名 + 有微信/支付宝。**全程零脚本，先跑通再优化。**

1. 腾讯云开通 COS，建私有桶 `kopia-mirror`，拿子账号 AccessKey（5 分钟）。
2. `scoop install rclone`（1 分钟）。
3. `rclone config create cos s3 provider=TencentCOS access_key_id=... secret_access_key=... endpoint=cos.ap-guangzhou.myqcloud.com region=ap-guangzhou acl=private`（1 分钟）。
4. `rclone size "H:\restore"` 看体积，确认 <50GB 免费额度。
5. 手动跑一次 6.3 的命令（不加 `--dry-run`），等它跑完（几 GB 约 10~40 分钟，夜里跑）。
6. 建计划任务：`schtasks /Create /TN kopia-mirror-daily /SC DAILY /ST 03:30 /TR "powershell.exe -File C:\Users\manifld\bin\kopia-mirror.ps1" /F`。
7. 第二天看 `C:\Logs\kopia-mirror-*.log` 末尾有没有 `Errors: 0`，以及 `kopia-mirror-status.txt` 里 `exit=0`。
8. **上线验收**：随便挑一个快照，把云端副本下载到 `D:\` 并 `kopia repository connect` 成功 → 完成。

> 如果连支付方式都没有：临时用**坚果云免费 WebDAV**（仓库 <800MB 可行）或**现有 OpenList**，先有异地副本，再按第 9 节问卷换正式方案。

## 9. 给用户的 5 分钟选择问卷

请回答下面 4 题（每题选一个），对照表给出结论：

**Q1. 你手上已经有下面哪个账号？**
A. 有 OneDrive（含学校/Office 365 的 1TB）｜B. 有腾讯云/阿里云且已完成实名｜C. 只有阿里云盘/百度网盘等国内网盘｜D. 有 Visa/Mastercard 或 PayPal｜E. 都没有

**Q2. `H:\restore` 现在（以及一年后预计）有多大？**
A. < 1GB｜B. 1~5GB｜C. 5~15GB｜D. 15GB 以上

**Q3. 每月最多愿意花多少钱？**
A. 一分不花｜B. ≤ 5 元｜C. ≤ 15 元

**Q4. 这台服务器是否 7×24 开机、且通常有人登录着？**
A. 是，一直登录｜B. 是，但经常没人登录｜C. 会关机

| 你的组合 | 建议 |
|---|---|
| Q1=B 或 Q3=B，Q2 ∈ {A,B,C} | **腾讯云 COS**（首选，国内最省心） |
| Q1=D，Q3=A，Q2 ∈ {A,B} | **Cloudflare R2**（永久免费 10GB） |
| Q1=A 且是 1TB 版 | **OneDrive**（容量最宽松，接受偶尔要代理） |
| Q2=A 且 Q1=E 且 Q3=A | **坚果云免费 WebDAV** 起步 |
| Q1=C 且 Q3=A 且不想绑卡 | **OpenList → WebDAV**（接受第 5 节风险） |
| Q4=C（会关机） | 计划任务不可靠 → 改 nssm 服务 + 开机补跑，或干脆只用 R2/坚果云手动周备 |

## 10. 参考链接（额度与支持情况以官网当期为准）

- rclone 后端总览与支持等级：https://rclone.org/overview/ ・ https://rclone.org/tiers/
- rclone WebDAV（含"不支持修改时间/哈希"说明）：https://rclone.org/webdav/
- rclone S3（各厂商 provider 与 `--size-only`/`--checksum` 取舍）：https://rclone.org/s3/
- rclone OneDrive：https://rclone.org/onedrive/
- kopia 的 rclone 后端（标注 Not maintained）：https://kopia.io/docs/reference/command-line/common/repository-connect-rclone/
- kopia 论坛「整仓 rclone 拷贝 vs sync-to」讨论：https://kopia.discourse.group/t/difference-s-between-a-kopia-server-and-rcloning-my-repo/1155
- Cloudflare R2 计费与免费额度（10GB / 100 万 A 类请求）：https://developers.cloudflare.com/r2/pricing/
- Backblaze B2 计费（首 10GB 免费、出站 3× 免费）：https://www.backblaze.com/cloud-storage/pricing
- 腾讯云 COS 新用户免费额度（50GB / 6 个月）：https://www.tencentcloud.com/zh/document/product/436/6240
- 七牛云免费额度（标准存储 10GB/月等）：https://developer.qiniu.io/af/12968/freequota
- 坚果云免费版流量限制（1GB 上传 / 3GB 下载 每月）：https://help.jianguoyun.com/?p=ygawetnqxamaou
- Google 账号 15GB 免费空间：https://support.google.com/googleone/answer/9004014
- OneDrive 免费 5GB：https://learn.microsoft.com/en-us/answers/questions/5559675/
- OpenList / AList 的 WebDAV 接入参数（默认 `/dav/`、端口同网页端）：https://alist-doc.nn.ci/en/docs/webdav/
- 阿里云盘/115 停止向 AList 提供 Open 授权（社区通报）：https://linux.do/t/topic/720642
- 阿里云盘第三方并发 >10 触发风控的提醒：https://www.landian.news/archives/101155.html
- OpenList 官方文档（驱动与授权说明）：https://doc.oplist.org/

> 本文中标注"需实测确认"的项目：R2/B2 在中国大陆的实际上传速度、腾讯云 COS 6 个月免费期后的实际月账单、OpenList WebDAV 对你所用网盘的写入稳定性、`kopia repository validate-provider` 在当前版本的可用性。
