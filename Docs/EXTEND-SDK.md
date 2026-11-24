# 给玄关写一个可挂载的程序

玄关的 `Extend/` 目录就是给"挂载物"用的：一个文件夹 = 一个程序，写个 `extend.json` 就能出现在站点里，
不用改主站代码，也不用重启。

## 一、最小结构

```
Extend/
└─ mytool/
   ├─ extend.json      ← 必需
   ├─ index.html       ← static 挂载的入口
   └─ app.js
```

`extend.json`：

```json
{
  "id": "mytool",
  "title": "我的小工具",
  "route": "/mytool",
  "kind": "static",
  "entry": "index.html",
  "permission": "auth",
  "order": 120
}
```

## 二、三种挂载方式

| kind | 含义 | 需要字段 |
|---|---|---|
| `static` | 直接把这个文件夹当网站根目录提供 | `entry` |
| `iframe` | 把外面跑的服务嵌进来 | `url`，可加 `allowedOrigin` / `exposeUser` |
| `link` | 只做 302 跳转 | `url` |

### 字段表

| 字段 | 默认 | 说明 |
|---|---|---|
| `id` | 文件夹名 | 唯一标识 |
| `title` | 同 id | 显示名 |
| `route` | `/<id>` | 访问路径，不能和系统路径冲突 |
| `kind` | `static` | `static` / `iframe` / `link` |
| `entry` | `index.html` | static 的入口文件 |
| `url` | 空 | iframe / link 的目标 |
| `permission` | 空 | 空 = 公开；`auth` = 登录可见；也可填 `chat.use` 等权限点 |
| `order` | `100` | 排序 |
| `enabled` | `true` | 关闭后整条不生效 |
| `allowedOrigin` | 空 | iframe 挂载时，允许接收登录信息的源（如 `http://127.0.0.1:5244`） |
| `exposeUser` | `false` | 是否把当前登录用户通过 postMessage 告诉 iframe |

## 三、怎么拿到"当前用户"

### static 挂载（同源）

直接请求：

```js
const response = await fetch('/api/me', { headers: { Accept: 'application/json' } });
if (response.ok) {
  const user = await response.json();
  // { name, display, role, permissions: [] }
}
```

`/api/me` 需要登录态（同源 Cookie），未登录返回 401。

### iframe 挂载（跨源）

在 `extend.json` 里声明允许的源：

```json
{
  "kind": "iframe",
  "url": "http://127.0.0.1:5244",
  "allowedOrigin": "http://127.0.0.1:5244",
  "exposeUser": true
}
```

然后在你自己的页面里引入 SDK：

```html
<script src="http://<玄关地址>/assets/js/genkan-mount.js"></script>
<script>
  GenkanMount.ready().then((user) => {
    if (user) console.log('当前用户', user.name, user.role);
  });

  // 或者监听
  GenkanMount.onUser((user) => { /* ... */ });
</script>
```

宿主页面只在 `allowedOrigin` 完全匹配时才推送用户信息；**不配这两个字段，父页面什么都不会给**。

### 让宿主页面跳转

```js
GenkanMount.open('/blog');   // iframe 里不能用 top.location，交给宿主处理
```

## 四、安全边界

- 挂载程序**不能**读写数据库、不能调用管理接口；它只是页面或另一个服务
- `permission` 决定谁能打开这个挂载；`auth` 表示登录即可
- iframe 默认拿不到任何用户信息，必须显式 `exposeUser` + `allowedOrigin`
- static 挂载会把整个文件夹公开（除非设了 permission），别放密钥和私人文件
- 挂载程序想调用站点的写接口（比如发剪贴板），需要单独申请 API Key——见 `/developers` 后续章节

## 五、调试

1. 保存 `extend.json` 后，到 `/admin/routes` 点「重新扫描」（或直接改页面保存）
2. 路由冲突、字段非法会在保存时提示
3. 404 先看 `entry` 文件名和 `route` 拼写
4. 白屏多半是静态资源用了绝对路径 `/app.js`，改成 `./app.js`
5. iframe 空白：对方站点的 `X-Frame-Options` / CSP 不允许被嵌入，只能改用 link

## 六、示例：一个最小的 static 挂载

`Extend/hello/extend.json`：

```json
{ "id": "hello", "title": "Hello", "route": "/hello", "permission": "auth" }
```

`Extend/hello/index.html`：

```html
<!doctype html>
<meta charset="utf-8">
<h1>Hello</h1>
<p id="who">读取中…</p>
<script>
  fetch('/api/me').then(r => r.ok ? r.json() : null).then(u => {
    document.getElementById('who').textContent = u ? `你好，${u.display}` : '未登录';
  });
</script>
```
