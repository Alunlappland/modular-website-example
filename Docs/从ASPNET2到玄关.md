# 从 ASP.NET 2.0 到玄关：概念对照与读码路线

给刚看完 2005 年教材的人：老概念没有作废，只是换了名字。

## 一、框架概念对照

| ASP.NET 2.0（2005） | 玄关（.NET 10） | 说明 |
|---|---|---|
| `.aspx` 页面 | `Pages/*.cshtml` | 都是"带代码的模板" |
| `.aspx.cs` code-behind | `*.cshtml.cs` 的 `PageModel` | 逻辑与模板分离 |
| WebForms 控件 `<asp:GridView>` | 手写 `<table>` + `@foreach` | 不再有控件树 |
| PostBack | `<form method="post" asp-page-handler="X">` | 提交到指定处理器 |
| `ViewState` | 无 | 状态要么进数据库，要么进 Cookie |
| `Session` | 无默认会话 | 登录态用 Cookie + 数据库 |
| `web.config` | `appsettings.json` | 配置从 XML 变 JSON |
| `Global.asax` | `Program.cs` | 启动逻辑集中在一个文件 |
| `App_Code` 里放业务类 | `Genkan/*` 下的服务类 | 依赖注入，不再 `new` 到处飞 |
| 三层架构（DAL/BLL/UI） | Repository / Service / PageModel | 同一个思路，更轻 |

## 二、C# 2005 → 现在

| 老写法 | 现在 | 在你的代码里 |
|---|---|---|
| `string s = "";` + `if (s != null)` | `string? s` + 可空分析 | 到处都是 `string?` |
| `ArrayList` / 强制转换 | `List<T>` / 泛型 | `IReadOnlyList<ClipRow>` |
| `foreach` 手动过滤 | LINQ | `files.Where(f => ...)` |
| `delegate` + `BeginInvoke` | `async/await` | 所有 HTTP、文件操作 |
| 手写 `Equals/GetHashCode` | `record` | `DiagnosticCheck`、`ApiProvider` |
| `switch` + 类型转换 | 模式匹配 | `kind is "static" or "iframe"` |
| `class Program { static void Main }` | 顶级语句 | `Program.cs` 第一行就是代码 |
| 命名空间大括号 | 文件作用域命名空间 | `namespace Genkan.Clip;` |

## 三、读代码路线（按这个顺序看，最省力）

1. **`Main/src/Genkan.App/Program.cs`** —— 站点的"目录"：路径、数据库、登录、路由、中间件。
   不用全懂，先看它注册了哪些服务和 `app.MapGet`。
2. **`Genkan/GenkanPaths.cs`** —— 所有目录都从站点根推导，理解这个就理解了数据放在哪。
3. **`Pages/Clip.cshtml` + `.cshtml.cs`** —— 最小的一对模板/模型，看懂"表单 → 处理器 → 重定向"。
4. **`Genkan/Clip/ClipRepository.cs`** —— 一个完整的数据访问类：建表、增删查、加密。
5. **`Genkan/Blog/BlogService.cs`** —— Markdown 解析与文件读写，看 `Markdig` 怎么用。
6. **`Genkan/Backup/BackupService.cs`** —— 怎么调外部程序（kopia）并解析输出。
7. **`Genkan/Api/ApiEndpoints.cs`** —— 对外 API 的写法，`MapGroup` + 鉴权。

## 四、练习建议

- 改 `Pages/Clip.cshtml` 的文案 → 重新部署看变化
- 给 `/api/v1/ping` 加一个字段 → 理解 Minimal API 返回值
- 在 `ClipRepository` 加一个「按关键词查」的方法 → 理解 Dapper
- 写一个挂载：`Extend/hello/extend.json` + `index.html` → 不用编译就能上线

## 五、别学的部分

- `ViewState`、`UpdatePanel`、`web.config` 程序集注册
- `WebForms` 控件生命周期
- `System.Data.SqlClient` + 拼接 SQL（本项目用 Dapper + 参数化）

这些在 2005 年是对的，现在会被 code review 打回。
