using System.Reflection;
using System.Security.Claims;
using Dapper;
using Genkan;
using Genkan.Backup;
using Genkan.Blog;
using Genkan.Data;
using Genkan.Files;
using Genkan.Security;
using Genkan.Sync;
using Genkan.Sys;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Serilog;
using Serilog.Events;

var siteRoot = ResolveSiteRoot();
var paths = new GenkanPaths(siteRoot);
paths.EnsureCreated();

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = siteRoot,
    ApplicationName = "Genkan",
});

builder.Configuration.AddJsonFile(
    Path.Combine(paths.Conf, "appsettings.json"),
    optional: true,
    reloadOnChange: true);

var configuredUrls = builder.Configuration["server:urls"];
if (!string.IsNullOrWhiteSpace(configuredUrls))
{
    builder.WebHost.UseUrls(configuredUrls);
}

builder.Host.UseSerilog((context, _, configuration) =>
{
    var level = Enum.TryParse<LogEventLevel>(context.Configuration["logging:minLevel"], ignoreCase: true, out var parsed)
        ? parsed
        : LogEventLevel.Information;
    var retainDays = int.TryParse(context.Configuration["logging:retainDays"], out var days) ? days : 90;

    configuration
        .MinimumLevel.Is(level)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(
            path: Path.Combine(paths.Logs, "app-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: retainDays,
            fileSizeLimitBytes: 10L * 1024 * 1024,
            rollOnFileSizeLimit: true,
            shared: true,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}");
});

// SQLite 列名是 snake_case，Dapper 自动映射到 PascalCase 属性
Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

var database = new GenkanDb(paths);
database.EnsureSchema();

builder.Services.AddSingleton(paths);
builder.Services.AddSingleton(database);

// 密钥环固定放在 conf\keys：控制台运行与 LocalSystem 服务都读同一份，
// 否则已加密的仓库密码换个账户就解不开。
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(paths.Conf, "keys")))
    .ProtectKeysWithDpapi(protectToLocalMachine: true)
    .SetApplicationName("Genkan");
builder.Services.AddSingleton<ExtendRegistry>();
builder.Services.AddSingleton<ExtendStore>();
builder.Services.AddSingleton<SysMonitor>();
builder.Services.AddSingleton<BlogService>();
builder.Services.AddSingleton<BlogRepository>();
builder.Services.AddSingleton<ModerationService>();
builder.Services.AddSingleton<FileRepository>();
builder.Services.AddSingleton<FileStorage>();
builder.Services.AddSingleton<ShareService>();
builder.Services.AddSingleton<SecretStore>();
builder.Services.AddSingleton<KopiaService>();
builder.Services.AddSingleton<BackupService>();
builder.Services.AddHostedService<BackupScheduler>();
builder.Services.AddSingleton<SyncService>();
builder.Services.AddHttpClient("syncthing", client => client.Timeout = TimeSpan.FromSeconds(3));
builder.Services.AddHttpClient("moderation", client => client.Timeout = TimeSpan.FromSeconds(8));
builder.Services.AddSingleton<SetupState>();

// 大文件上传：放宽请求体与 multipart 限制（512MB）
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 512L * 1024 * 1024);
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 512L * 1024 * 1024);
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<AuditLog>();

builder.Services.AddRazorPages(options =>
{
    // 编译期页面路径是 /Pages/xxx，运行时不要改根目录，否则找不到已编译的页面
    options.Conventions.AllowAnonymousToPage("/Index");
    options.Conventions.AllowAnonymousToPage("/Login");
    options.Conventions.AllowAnonymousToPage("/Setup");
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

// Razor 默认只放行 Basic Latin，中文会被编码成 &#x...;。放行全部 Unicode，中文直接输出。
builder.Services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(options =>
{
    options.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All);
});

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.Cookie.Name = "genkan.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/login?denied=1";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;

        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();
var registry = app.Services.GetRequiredService<ExtendRegistry>();
var setupState = app.Services.GetRequiredService<SetupState>();
var startedAt = DateTimeOffset.UtcNow;

using (var scope = app.Services.CreateScope())
{
    setupState.Initialized = scope.ServiceProvider.GetRequiredService<UserRepository>().Count() > 0;
}

app.UseForwardedHeaders();
app.UseSerilogRequestLogging();

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["X-Genkan"] = "1";
    await next();
});

app.UseAuthentication();

// 首次启动必须先进安装向导
app.Use(async (context, next) =>
{
    if (!setupState.Initialized)
    {
        var path = context.Request.Path;
        var allowed = path.StartsWithSegments("/setup")
                      || path.StartsWithSegments("/assets")
                      || path.StartsWithSegments("/healthz")
                      || path.StartsWithSegments("/theme")
                      || path.StartsWithSegments("/favicon");

        if (!allowed)
        {
            context.Response.Redirect("/setup");
            return;
        }
    }

    await next();
});

app.UseAuthorization();

// 主站静态资源：Main\website
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(paths.Website),
    RequestPath = string.Empty,
});

// 健康检查（匿名）
app.MapGet("/healthz", () => Results.Json(new
{
    status = "ok",
    site = app.Configuration["site:name"],
    version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
    initialized = setupState.Initialized,
    uptimeSeconds = (long)(DateTimeOffset.UtcNow - startedAt).TotalSeconds,
    extends = registry.Entries.Select(e => new { e.Id, e.Route, e.Kind, e.Enabled }),
}));

// 已挂载物清单
app.MapGet("/api/extends", (HttpContext context) => Results.Json(
    registry.Entries
        .Where(e => e.Enabled && (e.Permission is null || Permissions.Has(context.User, e.Permission)))
        .Select(e => new { e.Id, e.Title, e.Route, e.Kind, e.Order })));

// 主题切换（表 / 里 / 中性）
app.MapGet("/theme", (HttpContext context, string? name, string? returnUrl) =>
{
    var allowed = new[] { "surface", "inner", "neutral" };
    if (!string.IsNullOrWhiteSpace(name) && allowed.Contains(name, StringComparer.OrdinalIgnoreCase))
    {
        context.Response.Cookies.Append("genkan.theme", name.ToLowerInvariant(), new CookieOptions
        {
            Path = "/",
            HttpOnly = false,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddYears(1),
        });
    }

    var back = string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith('/') || returnUrl.StartsWith("//")
        ? "/"
        : returnUrl;

    return Results.Redirect(back);
});

// 挂载物：每次请求按当前注册表解析，管理页保存后即时生效（无需重启）
var contentTypes = new FileExtensionContentTypeProvider();

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "/";
    var mount = registry.Entries.FirstOrDefault(e =>
        e.Enabled &&
        (path.Equals(e.Route, StringComparison.OrdinalIgnoreCase) ||
         path.StartsWith(e.Route + "/", StringComparison.OrdinalIgnoreCase)));

    if (mount is null)
    {
        await next();
        return;
    }

    if (!string.IsNullOrWhiteSpace(mount.Permission))
    {
        if (!Permissions.IsAuthenticated(context.User))
        {
            var target = path + context.Request.QueryString;
            context.Response.Redirect("/login?returnUrl=" + Uri.EscapeDataString(target));
            return;
        }

        if (!Permissions.Has(context.User, mount.Permission!))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("没有访问「" + mount.Title + "」的权限。");
            return;
        }
    }

    if (string.Equals(mount.Kind, "link", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Redirect(string.IsNullOrWhiteSpace(mount.Url) ? "/" : mount.Url!);
        return;
    }

    if (string.Equals(mount.Kind, "iframe", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(BuildFramePage(mount));
        return;
    }

    var root = Directory.Exists(Path.Combine(mount.Directory, "www"))
        ? Path.Combine(mount.Directory, "www")
        : mount.Directory;

    var rel = path.Length > mount.Route.Length
        ? path[mount.Route.Length..].TrimStart('/')
        : string.Empty;

    if (rel.Length == 0 && !path.EndsWith('/'))
    {
        context.Response.Redirect(path + "/", permanent: false);
        return;
    }

    if (rel.Length == 0)
    {
        rel = "index.html";
    }

    var full = Path.GetFullPath(Path.Combine(root, rel));
    var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

    if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    if (Directory.Exists(full))
    {
        full = Path.Combine(full, "index.html");
    }

    if (!File.Exists(full) && !Path.HasExtension(full))
    {
        // 没有扩展名的路径当作前端路由，回退到 index.html
        full = Path.Combine(root, "index.html");
    }

    if (!File.Exists(full))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    context.Response.ContentType = contentTypes.TryGetContentType(full, out var contentType)
        ? contentType
        : "application/octet-stream";
    context.Response.Headers.CacheControl = "no-cache";
    await context.Response.SendFileAsync(full);
});

// 路由与挂载管理：只允许本机（隧道过来的请求带 CF 头，直接 403）
app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/admin"),
    branch => branch.Use(async (context, next) =>
    {
        if (!LocalOnly.IsLocal(context))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(
                "<!doctype html><html lang=\"zh-CN\"><meta charset=\"utf-8\">" +
                "<body style=\"font-family:system-ui;padding:40px\">" +
                "<h1>403</h1><p>路由与挂载管理只允许在本机操作。</p></body></html>");
            return;
        }

        await next();
    }));

app.MapGet("/api/sys", (SysMonitor monitor) => Results.Json(monitor.Sample()))
    .RequireAuthorization();

// ---------- 博客：反应 / 行级注释 / 预览 ----------
app.MapPost("/api/blog/{slug}/react", async (
    string slug,
    ReactRequest request,
    HttpContext context,
    IAntiforgery antiforgery,
    BlogService blog,
    BlogRepository repository) =>
{
    await antiforgery.ValidateRequestAsync(context);

    if (blog.Find(slug) is null)
    {
        return Results.NotFound();
    }

    if (request.Kind is not ("like" or "dislike"))
    {
        return Results.BadRequest("kind 只能是 like 或 dislike。");
    }

    repository.React(slug, request.Kind, Fingerprint(context));
    return Results.Json(repository.Counts(slug));
});

app.MapGet("/api/blog/{slug}/annotations", (
    string slug,
    HttpContext context,
    BlogRepository repository) =>
    Results.Json(repository.Annotations(slug, context.User.IsInRole("admin"))));

app.MapPost("/api/blog/{slug}/annotations", async (
    string slug,
    AnnotateRequest request,
    HttpContext context,
    IAntiforgery antiforgery,
    BlogService blog,
    BlogRepository repository,
    ModerationService moderation) =>
{
    await antiforgery.ValidateRequestAsync(context);

    if (blog.Find(slug) is null)
    {
        return Results.NotFound();
    }

    var author = (request.Author ?? string.Empty).Trim();
    var body = (request.Body ?? string.Empty).Trim();

    if (author.Length is < 1 or > 24)
    {
        return Results.BadRequest("昵称要 1–24 个字。");
    }

    if (body.Length is < 1 or > 500)
    {
        return Results.BadRequest("注释要 1–500 个字。");
    }

    if (!RateLimiter.TryAcquire(RateLimiter.ClientKey(context, "annotation"), 6, TimeSpan.FromMinutes(5)))
    {
        return Results.Json(new { error = "提交太频繁，歇一会儿再试。" }, statusCode: StatusCodes.Status429TooManyRequests);
    }

    var duplicate = repository.FindRecentDuplicate(slug, request.BlockIndex, author, body);
    if (duplicate is not null)
    {
        return Results.Json(new { id = duplicate, status = "duplicate" });
    }

    var status = context.User.Identity?.IsAuthenticated == true
        ? "approved"
        : await moderation.ReviewAsync(author + "：" + body);

    var id = repository.AddAnnotation(slug, request.BlockIndex, request.BlockHash ?? string.Empty, author, body, status);
    return Results.Json(new { id, status });
});

app.MapPost("/api/blog/preview", async (
    PreviewRequest request,
    HttpContext context,
    IAntiforgery antiforgery,
    BlogService blog) =>
{
    await antiforgery.ValidateRequestAsync(context);

    if (!context.User.IsInRole("admin"))
    {
        return Results.Forbid();
    }

    return Results.Json(blog.Render(request.Markdown ?? string.Empty));
});

// 管理员审核/删除评论与注释：AJAX 调用，不产生页面历史
app.MapPost("/api/blog/{slug}/comments/{id:long}/moderate", async (
    string slug,
    long id,
    ModerateRequest request,
    HttpContext context,
    IAntiforgery antiforgery,
    BlogRepository repository,
    AuditLog audit) =>
{
    await antiforgery.ValidateRequestAsync(context);

    if (!context.User.IsInRole("admin"))
    {
        return Results.Forbid();
    }

    var action = (request.Action ?? string.Empty).ToLowerInvariant();
    var ok = action switch
    {
        "approve" => repository.SetCommentStatus(id, "approved"),
        "reject" => repository.SetCommentStatus(id, "rejected"),
        "delete" => repository.DeleteComment(id),
        _ => false,
    };

    if (ok)
    {
        audit.Write(context, "blog.comment." + action, slug, "ok");
    }

    return ok ? Results.Json(new { ok = true }) : Results.NotFound();
});

app.MapPost("/api/blog/{slug}/annotations/{id:long}/moderate", async (
    string slug,
    long id,
    ModerateRequest request,
    HttpContext context,
    IAntiforgery antiforgery,
    BlogRepository repository,
    AuditLog audit) =>
{
    await antiforgery.ValidateRequestAsync(context);

    if (!context.User.IsInRole("admin"))
    {
        return Results.Forbid();
    }

    var action = (request.Action ?? string.Empty).ToLowerInvariant();
    var ok = action switch
    {
        "approve" => repository.SetAnnotationStatus(id, "approved"),
        "delete" => repository.DeleteAnnotation(id),
        _ => false,
    };

    if (ok)
    {
        audit.Write(context, "blog.annotation." + action, slug, "ok");
    }

    return ok ? Results.Json(new { ok = true }) : Results.NotFound();
});

app.MapRazorPages();

app.Lifetime.ApplicationStarted.Register(() =>
    Log.Information("玄关已开门：{Urls}  站点根目录：{Root}", configuredUrls ?? "(默认)", siteRoot));

app.Run();

static string Fingerprint(HttpContext context)
{
    if (context.Request.Cookies.TryGetValue("genkan.fp", out var existing) && !string.IsNullOrWhiteSpace(existing))
    {
        return existing;
    }

    var fingerprint = Guid.NewGuid().ToString("N");
    context.Response.Cookies.Append("genkan.fp", fingerprint, new CookieOptions
    {
        Path = "/",
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        MaxAge = TimeSpan.FromDays(365),
    });

    return fingerprint;
}

static string BuildFramePage(ExtendEntry mount)
{
    var encoder = System.Text.Encodings.Web.HtmlEncoder.Default;
    var title = encoder.Encode(mount.Title);
    var url = encoder.Encode(mount.Url ?? "about:blank");

    return $$"""
        <!doctype html>
        <html lang="zh-CN">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>{{title}}</title>
          <style>
            html, body { margin: 0; height: 100%; font-family: "Segoe UI", "Microsoft YaHei UI", system-ui, sans-serif; }
            header { display: flex; align-items: center; gap: 12px; padding: 8px 14px;
                     border-bottom: 1px solid rgba(0,0,0,.12); font-size: 13px; }
            header a { color: inherit; text-decoration: none; }
            iframe { border: 0; width: 100%; height: calc(100% - 38px); display: block; }
            @media (prefers-color-scheme: dark) {
              body { background: #121017; color: #ece7f2; }
              header { border-color: rgba(255,255,255,.14); }
            }
          </style>
        </head>
        <body>
          <header><a href="/">← 玄关</a><span>{{title}}</span></header>
          <iframe src="{{url}}" referrerpolicy="no-referrer"></iframe>
        </body>
        </html>
        """;
}

static string ResolveSiteRoot()
{
    var configured = Environment.GetEnvironmentVariable("GENKAN_ROOT");
    if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
    {
        return Path.GetFullPath(configured);
    }

    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "Main")) &&
            Directory.Exists(Path.Combine(dir.FullName, "Extend")))
        {
            return dir.FullName;
        }
    }

    return AppContext.BaseDirectory;
}
