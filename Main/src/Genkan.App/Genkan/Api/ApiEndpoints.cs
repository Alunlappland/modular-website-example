using System.Security.Claims;
using Genkan.Api;
using Genkan.Backup;
using Genkan.Data;
using Genkan.Blog;
using Genkan.Clip;
using Genkan.Files;
using Genkan.Gallery;
using Genkan.Notes;
using Genkan.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Genkan.Api;

public static class ApiEndpoints
{
    private const int DefaultSize = 20;

    public static WebApplication MapGenkanApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1");

        // 统一错误格式：{ error: { code, message } }
        api.MapGet("/ping", (IConfiguration config) => Results.Ok(new
        {
            ok = true,
            site = config["site:name"] ?? "玄关",
            time = DateTimeOffset.UtcNow.ToString("O"),
            version = "v1",
        }));

        api.MapGet("/me", (HttpContext context) =>
        {
            if (context.User.Identity?.IsAuthenticated != true)
            {
                return Fail(401, "unauthorized", "需要登录或有效的 API Key。");
            }

            var role = context.User.FindFirst(ClaimTypes.Role)?.Value ?? "guest";
            var key = context.Items["api.key"] as ApiKeyRow;

            return Results.Ok(new
            {
                name = context.User.Identity?.Name ?? string.Empty,
                display = context.User.FindFirst("display")?.Value ?? string.Empty,
                role,
                permissions = Permissions.Roles.TryGetValue(role, out var granted) ? granted : Array.Empty<string>(),
                scopes = key?.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         ?? Array.Empty<string>(),
            });
        });

        api.MapGet("/status", async (HttpContext context, GenkanDb db, BackupService backup, CancellationToken token) =>
        {
            if (Deny(context, ApiScopes.StatusRead) is { } denied)
            {
                return denied;
            }

            var databaseOk = true;
            long users = 0;
            long files = 0;

            try
            {
                using var connection = db.Open();
                users = Dapper.SqlMapper.ExecuteScalar<long>(connection, "SELECT COUNT(*) FROM users");
                files = Dapper.SqlMapper.ExecuteScalar<long>(connection, "SELECT COUNT(*) FROM files");
            }
            catch
            {
                databaseOk = false;
            }

            var last = backup.LastSuccess("daily") ?? backup.LastSuccess("full");

            return Results.Ok(new
            {
                database = databaseOk,
                users,
                files,
                lastBackup = last?.ToString("O"),
                serverTime = DateTimeOffset.UtcNow.ToString("O"),
            });
        });

        api.MapGet("/blog", (HttpContext context, BlogService blog, int? page, int? size, string? q, string? tag) =>
        {
            if (Deny(context, ApiScopes.BlogRead) is { } denied)
            {
                return denied;
            }

            var (current, take) = Paging(page, size);
            var role = RoleOf(context);
            var posts = blog.All().Where(post => CanSee(post, role)).AsEnumerable();

            if (!string.IsNullOrWhiteSpace(q))
            {
                posts = posts.Where(post =>
                    post.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    post.Summary.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    post.Body.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(tag))
            {
                posts = posts.Where(post => post.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));
            }

            var list = posts.ToList();
            var items = list.Skip((current - 1) * take).Take(take).Select(Public);

            return Results.Ok(new { page = current, size = take, total = list.Count, items });
        });

        api.MapGet("/blog/{slug}", (HttpContext context, string slug, BlogService blog) =>
        {
            if (Deny(context, ApiScopes.BlogRead) is { } denied)
            {
                return denied;
            }

            var post = blog.Find(slug);
            if (post is null || !CanSee(post, RoleOf(context)))
            {
                return Fail(404, "not_found", "文章不存在。");
            }

            return Results.Ok(new
            {
                slug = post.Slug,
                title = post.Title,
                date = post.Date.ToString("O"),
                summary = post.Summary,
                tags = post.Tags,
                visibility = post.Visibility,
                draft = post.Draft,
                body = post.Body,
            });
        });

        api.MapGet("/notes", (HttpContext context, NotesService notes) =>
        {
            if (Deny(context, ApiScopes.NotesRead) is { } denied)
            {
                return denied;
            }

            return Results.Ok(new
            {
                items = notes.List().Select(note => new { name = note.Name, size = note.Size, updatedAt = note.UpdatedAt }),
            });
        });

        api.MapGet("/notes/{name}", (HttpContext context, string name, NotesService notes) =>
        {
            if (Deny(context, ApiScopes.NotesRead) is { } denied)
            {
                return denied;
            }

            var normalized = NotesService.Normalize(name);
            var content = normalized is null ? null : notes.Read(normalized);
            return content is null
                ? Fail(404, "not_found", "笔记不存在。")
                : Results.Ok(new { name = normalized, content });
        });

        api.MapGet("/files", (HttpContext context, FileRepository files, int? page, int? size) =>
        {
            if (Deny(context, ApiScopes.FilesRead) is { } denied)
            {
                return denied;
            }

            var owner = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
            var (current, take) = Paging(page, size);
            var list = files.ListFiles(owner);
            var items = list.Skip((current - 1) * take).Take(take).Select(file => new
            {
                id = file.Id,
                name = file.Name,
                size = file.Size,
                mime = file.Mime,
                createdAt = file.CreatedAt,
                download = "/api/v1/files/" + file.Id + "/content",
            });

            return Results.Ok(new { page = current, size = take, total = list.Count, items });
        });

        api.MapGet("/files/{id}/content", (HttpContext context, string id, FileRepository files, FileStorage storage) =>
        {
            if (Deny(context, ApiScopes.FilesRead) is { } denied)
            {
                return denied;
            }

            var owner = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
            var file = files.ListFiles(owner).FirstOrDefault(item => item.Id == id);
            if (file is null)
            {
                return Fail(404, "not_found", "文件不存在。");
            }

            var path = storage.FullPath(owner, file.RelPath);
            return System.IO.File.Exists(path)
                ? Results.File(path, file.Mime ?? "application/octet-stream", file.Name, enableRangeProcessing: true)
                : Fail(410, "missing", "文件已不在磁盘上。");
        });

        api.MapGet("/clips", (HttpContext context, ClipRepository clips) =>
        {
            if (Deny(context, ApiScopes.ClipsRead) is { } denied)
            {
                return denied;
            }

            var owner = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
            return Results.Ok(new
            {
                items = clips.List(owner).Select(row => new { id = row.Id, text = row.Text, createdAt = row.CreatedAt, pinned = row.Pinned }),
            });
        });

        api.MapPost("/clips", (HttpContext context, ClipRepository clips, ApiTextRequest request) =>
        {
            if (Deny(context, ApiScopes.ClipsWrite) is { } denied)
            {
                return denied;
            }

            var text = (request.Text ?? string.Empty).Trim();
            if (text.Length == 0 || text.Length > 20000)
            {
                return Fail(400, "bad_request", "text 不能为空且不超过 20000 字。");
            }

            var owner = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
            clips.Add(owner, text, request.Pinned);
            return Results.Ok(new { ok = true });
        });

        api.MapGet("/gallery", async (HttpContext context, GalleryService gallery, int? page, string? source, string? tags, CancellationToken token) =>
        {
            if (Deny(context, ApiScopes.GalleryRead) is { } denied)
            {
                return denied;
            }

            var items = await gallery.FetchAsync(page ?? 1, tags, token, source);
            return Results.Ok(new { page = page ?? 1, count = items.Count, items });
        });

        api.MapGet("/openapi.json", (IWebHostEnvironment env) =>
        {
            var file = Path.Combine(env.ContentRootPath, "Docs", "openapi.json");
            return System.IO.File.Exists(file)
                ? Results.File(file, "application/json")
                : Fail(404, "not_found", "openapi.json 缺失。");
        });

        return app;
    }

    // ---------- 鉴权 ----------

    /// <summary>在 /api/v1 上把 Bearer Key 换成登录身份；Cookie 登录保持原样。</summary>
    public static async Task ApiKeyAuthentication(HttpContext context, RequestDelegate next)
    {
        if (!context.Request.Path.StartsWithSegments("/api/v1"))
        {
            await next(context);
            return;
        }

        var header = context.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var key = header["Bearer ".Length..].Trim();
            var repo = context.RequestServices.GetRequiredService<ApiKeyRepository>();
            var record = repo.Authenticate(key);

            if (record is not null)
            {
                var users = context.RequestServices.GetRequiredService<Genkan.Data.UserRepository>();
                var user = users.FindById(record.OwnerId);

                if (user is not null && user.Enabled)
                {
                    context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, user.Id),
                        new Claim(ClaimTypes.Name, user.Name),
                        new Claim(ClaimTypes.Role, user.Role),
                        new Claim("display", user.Display),
                    }, "ApiKey"));

                    context.Items["api.key"] = record;
                }
            }
        }

        await next(context);
    }

    // ---------- 工具 ----------

    private static (int Page, int Size) Paging(int? page, int? size)
        => (Math.Max(1, page ?? 1), Math.Clamp(size ?? DefaultSize, 1, 100));

    private static string RoleOf(HttpContext context)
        => context.User.FindFirst(ClaimTypes.Role)?.Value ?? "guest";

    private static bool CanSee(BlogPost post, string role)
    {
        if (role == "admin")
        {
            return true;
        }

        if (post.Draft)
        {
            return false;
        }

        return post.Visibility switch
        {
            "public" => true,
            "member" => role is "member" or "admin",
            _ => false,
        };
    }

    private static object Public(BlogPost post) => new
    {
        slug = post.Slug,
        title = post.Title,
        date = post.Date.ToString("O"),
        summary = post.Summary,
        tags = post.Tags,
        visibility = post.Visibility,
        draft = post.Draft,
    };

    /// <summary>API Key 走 scope；Cookie 登录走角色权限。返回 null 表示放行。</summary>
    private static IResult? Deny(HttpContext context, string scope)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Fail(401, "unauthorized", "需要登录或有效的 API Key。");
        }

        var key = context.Items["api.key"] as ApiKeyRow;
        if (key is not null && !ApiKeyRepository.HasScope(key, scope))
        {
            return Fail(403, "forbidden", "这个 API Key 没有 " + scope + " 权限。");
        }

        return null;
    }

    private static IResult Fail(int status, string code, string message)
        => Results.Json(new { error = new { code, message } }, statusCode: status);
}

public sealed record ApiTextRequest(string? Text, bool Pinned);
