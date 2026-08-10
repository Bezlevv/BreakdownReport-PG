using BreakdownReport.Models;
using BreakdownReport.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BreakdownReport.Pages.Breakdowns;

public class DetailsModel : PageModel
{
    private readonly BreakdownService _breakdowns;
    private readonly AttachmentService _attachments;
    private readonly AuditService _audit;

    public DictionaryStore Store { get; }
    public Breakdown? Item { get; private set; }
    public IReadOnlyList<string> Attachments { get; private set; } = [];
    public IReadOnlyList<AuditEntry> Audit { get; private set; } = [];
    public bool IsAdmin { get; private set; }

    public DetailsModel(BreakdownService breakdowns, AttachmentService attachments, AuditService audit, DictionaryStore store)
    {
        _breakdowns = breakdowns;
        _attachments = attachments;
        _audit = audit;
        Store = store;
    }

    public IActionResult OnGet(int id)
    {
        Item = _breakdowns.GetById(id);
        if (Item is null) return NotFound();
        Attachments = _attachments.ListFiles(id);
        Audit = _audit.For(id);
        IsAdmin = CurrentUser()?.IsAdmin == true;
        return Page();
    }

    public async Task<IActionResult> OnPostUploadAsync(int id, List<IFormFile>? files)
    {
        if (files is not null)
            foreach (var f in files.Where(f => f.Length > 0))
            {
                await _attachments.SaveAsync(id, f);
                _audit.Log(id, CurrentUserName(), "добавлено вложение", f.FileName);
            }
        return RedirectToPage("/Breakdowns/Details", new { id });
    }

    public IActionResult OnPostDeleteFile(int id, string fileName)
    {
        if (!IsAdminUser())
        {
            TempData["Warning"] = "Удаление вложений доступно только администратору";
            return RedirectToPage("/Breakdowns/Details", new { id });
        }
        _attachments.Delete(id, fileName);
        _audit.Log(id, CurrentUserName(), "удалено вложение", fileName);
        return RedirectToPage("/Breakdowns/Details", new { id });
    }

    public IActionResult OnPostDelete(int id)
    {
        if (!IsAdminUser())
        {
            TempData["Warning"] = "Удаление записей доступно только администратору";
            return RedirectToPage("/Breakdowns/Details", new { id });
        }
        _audit.Log(id, CurrentUserName(), "удаление записи");
        _breakdowns.Delete(id);
        _attachments.DeleteAll(id);
        return RedirectToPage("/Breakdowns/Index");
    }

    public string EmployeeName(int id) =>
        Store.Employees.FirstOrDefault(e => e.Id == id)?.FullName ?? "—";

    private bool IsAdminUser() => CurrentUser()?.IsAdmin == true;

    private Employee? CurrentUser()
    {
        var winLogin = User.Identity?.IsAuthenticated == true ? User.Identity.Name : null;
        if (winLogin is not null && winLogin.Contains('\\'))
            winLogin = winLogin.Split('\\')[^1];
        return (winLogin is not null ? Store.FindByLogin(winLogin) : null)
            ?? (int.TryParse(Request.Cookies["currentUserId"], out var cid)
                ? Store.Employees.FirstOrDefault(e => e.Id == cid) : null);
    }

    private string CurrentUserName() => CurrentUser()?.FullName ?? "—";
}