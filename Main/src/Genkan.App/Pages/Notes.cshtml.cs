using Genkan.Blog;
using Genkan.Notes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Genkan.Pages;

[Authorize]
public class NotesModel : PageModel
{
    private readonly NotesService _notes;
    private readonly BlogService _blog;

    public NotesModel(NotesService notes, BlogService blog)
    {
        _notes = notes;
        _blog = blog;
    }

    public IReadOnlyList<NoteEntry> Notes { get; private set; } = Array.Empty<NoteEntry>();

    public string? CurrentName { get; private set; }

    public IReadOnlyList<BlogBlock> Blocks { get; private set; } = Array.Empty<BlogBlock>();

    [BindProperty]
    public string? Name { get; set; }

    [BindProperty]
    public string? Content { get; set; }

    [BindProperty]
    public IFormFile? Upload { get; set; }

    public string? Message { get; private set; }

    public string? Error { get; private set; }

    public void OnGet(string? file, string? edit)
    {
        Notes = _notes.List();

        if (!string.IsNullOrWhiteSpace(edit))
        {
            Name = edit;
            Content = _notes.Read(edit);
            return;
        }

        if (!string.IsNullOrWhiteSpace(file))
        {
            var name = NotesService.Normalize(file);
            var text = name is null ? null : _notes.Read(name);
            if (text is null)
            {
                Error = "找不到这篇笔记。";
                return;
            }

            CurrentName = name;
            Blocks = _blog.Render(text);
        }
    }

    public IActionResult OnPostSave()
    {
        var name = NotesService.Normalize(Name);
        if (name is null)
        {
            Error = "标题不能为空。";
            Notes = _notes.List();
            return Page();
        }

        _notes.Save(name, Content ?? string.Empty);
        Message = "已保存：" + name + ".md";
        return RedirectToPage(new { file = name });
    }

    public async Task<IActionResult> OnPostUploadAsync(CancellationToken cancellationToken)
    {
        var (ok, message) = await _notes.UploadAsync(Upload, cancellationToken);
        if (ok) Message = message;
        else Error = message;

        Notes = _notes.List();
        return Page();
    }

    public IActionResult OnPostDelete(string file)
    {
        var name = NotesService.Normalize(file);
        if (name is not null)
        {
            _notes.Delete(name);
            Message = "已删除：" + name + ".md";
        }

        return RedirectToPage();
    }
}
