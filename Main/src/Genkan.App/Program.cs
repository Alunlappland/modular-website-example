using System.Reflection;
using System.Security.Claims;
using Dapper;
using Genkan;
using Genkan.Data;
using Genkan.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.RazorPages;
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
builder.Services.AddSingleton<ExtendRegistry>();
builder.Services.AddSingleton<SetupState>();
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

// Extend\ 下的静态挂载物（按权限放行）
foreach (var extend in registry.Entries.Where(e =>
             e.Enabled && string.Equals(e.Kind, "static", StringComparison.OrdinalIgnoreCase)))
{
    var mountRoot = Directory.Exists(Path.Combine(extend.Directory, "www"))
        ? Path.Combine(extend.Directory, "www")
        : extend.Directory;
    var provider = new PhysicalFileProvider(mountRoot);
    var permission = extend.Permission;

    app.Map(extend.Route, branch =>
    {
        if (!string.IsNullOrWhiteSpace(permission))
        {
            branch.Use(async (context, next) =>
            {
                if (!Permissions.IsAuthenticated(context.User))
                {
                    var target = context.Request.Path + context.Request.QueryString;
                    context.Response.Redirect("/login?returnUrl=" + Uri.EscapeDataString(target));
                    return;
                }

                if (!Permissions.Has(context.User, permission))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "text/plain; charset=utf-8";
                    await context.Response.WriteAsync("没有访问「" + extend.Title + "」的权限。");
                    return;
                }

                await next();
            });
        }

        branch.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider });
        branch.UseStaticFiles(new StaticFileOptions { FileProvider = provider });

        branch.Run(async context =>
        {
            var requestPath = context.Request.Path.Value ?? string.Empty;

            if (!requestPath.EndsWith('/'))
            {
                context.Response.Redirect(requestPath + "/", permanent: false);
                return;
            }

            var index = Path.Combine(mountRoot, "index.html");
            if (!File.Exists(index))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(index);
        });
    });
}

app.MapRazorPages();

app.Lifetime.ApplicationStarted.Register(() =>
    Log.Information("玄关已开门：{Urls}  站点根目录：{Root}", configuredUrls ?? "(默认)", siteRoot));

app.Run();

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
