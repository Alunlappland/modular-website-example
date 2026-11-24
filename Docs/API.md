# 玄关 API v1（给第三方前端 / 附属程序）

站点所有内容都可以通过 `/api/v1` 读取；写操作按需要申请 API Key。
接口契约见 [`openapi.json`](openapi.json)，也可以直接访问 `/api/v1/openapi.json`。

## 一、鉴权

两种方式，任选其一：

| 方式 | 适用 | 说明 |
|---|---|---|
| Cookie | 同源页面、你自己写的挂载程序 | 浏览器会自动带 `genkan.auth` |
| Bearer Key | 跨源前端、脚本、别的服务器 | `Authorization: Bearer gk_...` |

API Key 由站长在后台创建，**只显示一次**，绑定到某个站内账号，权限由 scope 决定：

| scope | 能做什么 |
|---|---|
| `blog.read` | 读文章列表与正文 |
| `notes.read` | 读笔记 |
| `files.read` | 列自己的文件、下载 |
| `clips.read` / `clips.write` | 读 / 写剪贴板 |
| `gallery.read` | 图墙 |
| `status.read` | 站点状态与备份时间 |

没有 scope 的 Key 调对应接口会得到 `403`。

## 二、统一格式

成功：

```json
{ "page": 1, "size": 20, "total": 42, "items": [ ... ] }
```

出错：

```json
{ "error": { "code": "forbidden", "message": "这个 API Key 没有 files.read 权限。" } }
```

分页参数：`page` 从 1 开始，`size` 默认 20、最大 100。

## 三、跨源调用（推荐给前端）

服务端已经配好 CORS 白名单，在 `appsettings.json` 的 `api:corsOrigins` 里加你的前端地址：

```json
{
  "api": {
    "corsOrigins": ["https://your-frontend.example.com", "http://localhost:5173"]
  }
}
```

跨源请求要带凭据时：

```js
fetch('https://nas.20061028.xyz/api/v1/blog?page=1', {
  headers: { Authorization: 'Bearer gk_xxx' },
  // 如果改用 Cookie 登录态，需要 credentials: 'include'
});
```

注意：CORS 不允许 `*` 与 `credentials` 同时使用，所以必须是明确列出的 origin。

## 四、接口一览

| 方法 | 路径 | scope |
|---|---|---|
| GET | `/api/v1/ping` | 无 |
| GET | `/api/v1/me` | 登录或 Key |
| GET | `/api/v1/status` | `status.read` |
| GET | `/api/v1/blog?page=&size=&q=&tag=` | `blog.read` |
| GET | `/api/v1/blog/{slug}` | `blog.read` |
| GET | `/api/v1/notes` | `notes.read` |
| GET | `/api/v1/notes/{name}` | `notes.read` |
| GET | `/api/v1/files?page=&size=` | `files.read` |
| GET | `/api/v1/files/{id}/content` | `files.read` |
| GET | `/api/v1/clips` | `clips.read` |
| POST | `/api/v1/clips` | `clips.write` |
| GET | `/api/v1/gallery?page=&source=&tags=` | `gallery.read` |
| GET | `/api/v1/openapi.json` | 无 |

## 五、给前端同学的最小接入

1. 拿到站点地址和一把 API Key
2. 拉 `GET /api/v1/ping` 确认连通
3. 拉 `GET /api/v1/blog` 渲染列表，`GET /api/v1/blog/{slug}` 渲染正文（Markdown 原文，自己用 marked/markdown-it 渲染）
4. 图片文件走 `/api/v1/files/{id}/content`，支持 Range
5. 出错按 `error.code` 分支处理，不要解析 message 文案

## 六、限制

- 没有写博客 / 删文件的接口（这类操作只在站内后台做）
- API Key 只有 scope，没有"按文章可见性"的细分；`blog.read` 读到的是该账号本就能看的文章
- 速率限制暂未做；给第三方用之前建议先在反代层加一层限流
