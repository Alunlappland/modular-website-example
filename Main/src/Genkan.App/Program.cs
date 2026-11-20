using System.Reflection;
using Genkan;
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

builder.Services.AddSingleton(paths);
builder.Services.AddSingleton<ExtendRegistry>();

var app = builder.Build();
var registry = app.Services.GetRequiredService<ExtendRegistry>();
var startedAt = DateTimeOffset.UtcNow;

app.UseSerilogRequestLogging();

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["X-Genkan"] = "1";
    await next();
});

// 主站静态资源：Main\website
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(paths.Website),
    RequestPath = string.Empty,
});

// 玄关：访客第一次进来看到的地方
app.MapGet("/", () =>
{
    var home = Path.Combine(paths.Pages, "home.html");
    return File.Exists(home)
        ? Results.File(home, "text/html; charset=utf-8")
        : Results.Text("Genkan is running. (Main\\website\\pages\\home.html not found)", "text/plain; charset=utf-8");
});

// 健康检查：给 cloudflared / 外部监控 / 我自己冒烟测试用
app.MapGet("/healthz", () => Results.Json(new
{
    status = "ok",
    site = app.Configuration["site:name"],
    version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
    uptimeSeconds = (long)(DateTimeOffset.UtcNow - startedAt).TotalSeconds,
    extends = registry.Entries.Select(e => new { e.Id, e.Route, e.Kind, e.Enabled }),
}));

// 已挂载物清单：给首页卡片和后续管理页用
app.MapGet("/api/extends", () => Results.Json(registry.Entries
    .Where(e => e.Enabled)
    .Select(e => new { e.Id, e.Title, e.Route, e.Kind, e.Order })));

// Extend\ 下的静态挂载物
foreach (var extend in registry.Entries.Where(e =>
             e.Enabled && string.Equals(e.Kind, "static", StringComparison.OrdinalIgnoreCase)))
{
    var mountRoot = Directory.Exists(Path.Combine(extend.Directory, "www"))
        ? Path.Combine(extend.Directory, "www")
        : extend.Directory;
    var provider = new PhysicalFileProvider(mountRoot);

    app.Map(extend.Route, branch =>
    {
        branch.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider });
        branch.UseStaticFiles(new StaticFileOptions { FileProvider = provider });

        branch.Run(async context =>
        {
            var requestPath = context.Request.Path.Value ?? string.Empty;

            // 子路径静态站点需要尾斜杠，否则相对资源会解析错位
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
