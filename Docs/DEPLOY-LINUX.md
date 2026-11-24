# 部署到 Linux（Oracle Cloud / 任意 VPS）

玄关是 ASP.NET Core 应用，本身就跨平台；之前有几处 Windows 专有代码，已经改成运行时判断。
这份文档记录从 Windows 迁到 Linux 的完整步骤和坑。

## 一、先说 Oracle 免费层

Always Free（长期免费，不是试用）：

| 资源 | 额度 |
|---|---|
| Ampere A1（ARM） | 4 OCPU + 24 GB 内存，可拆成最多 4 台 |
| AMD micro | 2 台 1/8 OCPU + 1 GB |
| 块存储 | 200 GB |
| 出站流量 | 每月 10 TB |

现实问题：

- **ARM 容量紧张**：热门区域经常开不出 A1，报 "Out of capacity"。日韩区相对好一点，可以换可用域或错峰重试。
- **闲置回收**：免费实例长期低负载可能被回收，跑着站点和备份就不算闲置。
- **注册要卡**：需要一张能验证的信用卡/借记卡，扣 1 美元验证。
- **区域选择**：对国内访问，首尔 / 大阪 / 新加坡较好；法兰克福、美西明显更慢。
- **入站端口**：Oracle 默认只开 22，需要在安全列表（Security List）和实例防火墙（iptables/firewalld）两处都放行。

## 二、两种用法

### A. 站点还留在家里，VPS 当中转

适合不想动数据、只想让外网访问快一点。

- VPS 上跑 `frps`，家里跑 `frpc` 把 8080 反代出去
- 或者 VPS 上跑 `cloudflared`，家里保持现有 tunnel
- 优点：迁移成本最低；缺点：家里上行仍是瓶颈，VPS 只是换了条路由

### B. 整站迁到 VPS（推荐）

站点直接跑在 VPS 上，家里只负责备份与素材。

- 不再受家里上行带宽限制
- 服务器 24 小时在线，不依赖家里机器开着
- 需要重新填一次密钥（原因见下）

## 三、迁移步骤（B 方案）

### 1. 装 .NET 运行时

Ubuntu 22.04/24.04：

```bash
sudo apt update && sudo apt install -y curl unzip
curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
bash dotnet-install.sh --channel 10.0 --runtime aspnetcore --install-dir /usr/share/dotnet
sudo ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet
dotnet --info
```

### 2. 在 Windows 上发布 Linux 版本

```powershell
.\Tools\publish-linux.ps1 -Runtime linux-arm64   # AMD 机型用 linux-x64
```

产物在 `Main\dist-linux\`。

### 3. 拷到服务器

```bash
sudo mkdir -p /opt/genkan
sudo rsync -av ./dist-linux/ server:/opt/genkan/Main/bin/
```

同时需要带过去：

- `Main/conf/appsettings.json`（把 `server.urls` 改成 `http://0.0.0.0:8080`）
- `Main/document/`（博客与笔记）
- `Main/data/`（上传文件、对话、收藏）
- `Main/database/genkan.db`
- `Extend/`（挂载物）

### 4. 密钥要重新填（重要）

Windows 上的密钥环用 DPAPI 加密，**绑这台机器的账户**，拷到 Linux 解不开。
迁移后需要重新录入：

- `/backup` 里的 kopia 仓库密码
- `/ai` 里各供应商的 API Key
- `/backup` 的 R2 凭据
- `/me` 的 TOTP（重新绑定一次）

`Main/conf/keys/` 不要带过去，让新机器自己生成。

### 5. systemd 服务

`/etc/systemd/system/genkan.service`：

```ini
[Unit]
Description=Genkan personal portal
After=network.target

[Service]
Type=simple
WorkingDirectory=/opt/genkan/Main/bin
ExecStart=/usr/bin/dotnet /opt/genkan/Main/bin/Genkan.dll --urls http://0.0.0.0:8080
Environment=GENKAN_ROOT=/opt/genkan
Environment=ASPNETCORE_ENVIRONMENT=Production
Restart=always
RestartSec=5
User=www-data
Group=www-data

[Install]
WantedBy=multi-user.target
```

```bash
sudo chown -R www-data:www-data /opt/genkan
sudo systemctl daemon-reload
sudo systemctl enable --now genkan
sudo systemctl status genkan
```

### 6. 反向代理与 HTTPS

两种都行：

- **Cloudflare Tunnel**：VPS 上装 `cloudflared`，指到 `http://localhost:8080`，不用开 80/443
- **Caddy**：`caddy reverse-proxy --from your.domain --to localhost:8080`，自动签证书

### 7. 备份与 R2

- 装 kopia（Linux 有官方二进制），仓库可以用本地盘或 R2
- 装 rclone，镜像到 R2 的配置和 Windows 一致
- 家里那台机器可以改成「只做备份源」，或者反过来从 VPS 拉

## 四、这台机器上的跨平台改动

已经处理：

- DataProtection 的 DPAPI 只在 Windows 上启用，Linux 退回文件密钥环
- 自诊断的磁盘检查区分 Windows 盘符与 Linux 根分区
- 响铃在非 Windows 上静默失败（浏览器端仍会响）

仍需注意：

- `appsettings.json` 里的 kopia / rclone 路径是 Windows 写法，Linux 上要改成 `/usr/bin/kopia`、`/usr/bin/rclone`
- 备份源路径同理
- 文件模块的「服务器目录浏览」白名单要改成 Linux 路径

## 五、什么时候值得迁

- 家里上行 < 20 Mbps，且经常有外部访问 → 值得
- 只是自己用、偶尔看看 → 家里 + ZeroTier 直连更省事
- 需要 24 小时在线（AI 代理、图墙、备份）→ 值得
