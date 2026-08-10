using BreakdownReport.Models;
using BreakdownReport.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BreakdownReport.Pages.Modernizations;

public class DetailsModel : PageModel
{
    private readonly ModernizationService _service;
    private readonly ModernizationAttachmentService _attachments;
    private readonly ModernizationAuditService _audit;

    public DictionaryStore Store { get; }
    public Modernization? Item { get; private set; }
    public IReadOnlyList<string> Attachments { get; private set; } = [];
    public IReadOnlyList<AuditEntry> Audit { get; private set; } = [];
    public bool IsAdmin { get; private set; }
    public bool CanApprove { get; private set; }
    public IReadOnlyList<string> Statuses { get; } =
        ["Ожидает утверждения", "Новая", "В процессе", "Завершена"];

    public DetailsModel(ModernizationService service,
                        ModernizationAttachmentService attachments,
                        ModernizationAuditService audit,
                        DictionaryStore store)
    {
        _service = service;
        _attachments = attachments;
        _audit = audit;
        Store = store;
    }

    public IActionResult OnGet(int id)
    {
        Item = _service.GetById(id);
        if (Item is null) return NotFound();
        Attachments = _attachments.ListFiles(id);
        Audit = _audit.For(id);
        var user = CurrentUser();
        IsAdmin = user?.IsAdmin == true;
        CanApprove = user is not null && (user.IsEngineer || user.IsAdmin);
        return Page();
    }

    public IActionResult OnPostStatus(int id, string status)
    {
        var user = CurrentUser();
        if (user is null || (!user.IsEngineer && !user.IsAdmin))
        {
            TempData["Warning"] = "Менять статус заявки может только инженер";
            return RedirectToPage("/Modernizations/Details", new { id });
        }

        var item = _service.GetById(id);
        if (item is null) return NotFound();
        var old = item.Status;
        item.Status = status;

        if (old == "Ожидает утверждения" && status != "Новая" && item.ApproverId <= 0)
            item.ApproverId = user.Id;

        _service.Update(item);

        if (old == "Ожидает утверждения" && status == "В процессе")
            _audit.Log(id, user.FullName, "заявка утверждена (взята в работу)");
        else
            _audit.Log(id, user.FullName, "смена статуса заявки", $"{old} → {status}");

        return RedirectToPage("/Modernizations/Details", new { id });
    }

    public IActionResult OnPostApprove(int id)
    {
        var user = CurrentUser();
        if (user is null || (!user.IsEngineer && !user.IsAdmin))
        {
            TempData["Warning"] = "Утверждать заявку может только инженер или админ";
            return RedirectToPage("/Modernizations/Details", new { id });
        }
        if (_service.Approve(id, user.Id))
            _audit.Log(id, user.FullName, "заявка утверждена (взята в работу)");
        return RedirectToPage("/Modernizations/Details", new { id });
    }

    public IActionResult OnPostComment(int id, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return RedirectToPage("/Modernizations/Details", new { id });
        _service.AddComment(id, CurrentUserName(), text.Trim());
        return RedirectToPage("/Modernizations/Details", new { id });
    }

    public async Task<IActionResult> OnPostUploadAsync(int id, List<IFormFile>? files)
    {
        if (files is not null)
            foreach (var f in files.Where(f => f.Length > 0))
            {
                await _attachments.SaveAsync(id, f);
                _audit.Log(id, CurrentUserName(), "добавлено вложение к заявке", f.FileName);
            }
        return RedirectToPage("/Modernizations/Details", new { id });
    }

    public IActionResult OnPostDeleteFile(int id, string fileName)
    {
        if (CurrentUser()?.IsAdmin == true)
        {
            _attachments.Delete(id, fileName);
            _audit.Log(id, CurrentUserName(), "удалено вложение к заявке", fileName);
        }
        return RedirectToPage("/Modernizations/Details", new { id });
    }

    public IActionResult OnPostDelete(int id)
    {
        if (CurrentUser()?.IsAdmin != true)
        {
            TempData["Warning"] = "Удаление заявок доступно только администратору";
            return RedirectToPage("/Modernizations/Details", new { id });
        }
        _audit.Log(id, CurrentUserName(), "удаление заявки на модернизацию");
        _service.Delete(id);
        _attachments.DeleteAll(id);
        return RedirectToPage("/Modernizations/Index");
    }

    public string EmployeeName(int id) =>
        Store.Employees.FirstOrDefault(e => e.Id == id)?.FullName ?? "—";

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