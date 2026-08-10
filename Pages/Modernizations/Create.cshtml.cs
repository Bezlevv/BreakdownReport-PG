using BreakdownReport.Models;
using BreakdownReport.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BreakdownReport.Pages.Modernizations;

public class CreateModel : PageModel
{
    private readonly ModernizationService _service;
    private readonly ModernizationAttachmentService _attachments;
    private readonly ModernizationAuditService _audit;

    public DictionaryStore Store { get; }

    [BindProperty] public Modernization Input { get; set; } = new();
    [BindProperty] public int? AreaId { get; set; }
    [BindProperty] public List<int> AssigneeIds { get; set; } = [];
    [BindProperty] public List<IFormFile>? Files { get; set; }

    // Может ли текущий пользователь утверждать заявки (инженер или админ)
    public bool IsApprover { get; private set; }

    public IReadOnlyList<string> Statuses { get; } =
        ["Ожидает утверждения", "Новая", "В процессе", "Завершена"];

    public CreateModel(ModernizationService service,
                       ModernizationAttachmentService attachments,
                       ModernizationAuditService audit,
                       DictionaryStore store)
    {
        _service = service;
        _attachments = attachments;
        _audit = audit;
        Store = store;
    }

    public IActionResult OnGet()
    {
        var user = CurrentUser();
        IsApprover = user is not null && (user.IsEngineer || user.IsAdmin);

        if (Input.RequiredDate == default) Input.RequiredDate = DateTime.Now.AddDays(14);
        if (Input.CreatedAt == default) Input.CreatedAt = DateTime.Now;
        if (string.IsNullOrEmpty(Input.Status))
            Input.Status = IsApprover ? "Новая" : "Ожидает утверждения";

        return Page();
    }

    public async Task<IActionResult> OnPost()
    {
        var currentUser = CurrentUser();
        if (currentUser is null)
            ModelState.AddModelError(string.Empty,
                "Не выбран текущий пользователь — выберите себя в правом верхнем углу и повторите.");

        if (string.IsNullOrWhiteSpace(Input.ShortDescription))
            ModelState.AddModelError("ShortDescription", "Укажите краткое описание");
        if (Input.EquipmentId <= 0)
            ModelState.AddModelError("EquipmentId", "Выберите оборудование");
        if (string.IsNullOrWhiteSpace(Input.Customer))
            ModelState.AddModelError("Customer", "Укажите заказчика");
        if (Input.ApproverId <= 0)
            ModelState.AddModelError("ApproverId", "Выберите инженера");
        if (Input.RequiredDate == default)
            ModelState.AddModelError("RequiredDate", "Укажите необходимую дату");
        if (Input.CreatedAt == default || Input.CreatedAt > DateTime.Now.AddMinutes(5))
            ModelState.AddModelError("CreatedAt", "Укажите корректную дату подачи заявки");

        if (!ModelState.IsValid)
            return Page();

        Input.AuthorId = currentUser!.Id;

        // Обычный сотрудник — заявка уходит на утверждение;
        // инженер/админ при желании сразу согласует сам себя.
        var isApprover = currentUser.IsEngineer || currentUser.IsAdmin;
        IsApprover = isApprover;
        if (!isApprover)
        {
            Input.Status = "Ожидает утверждения";
        }
        else if (Input.ApproverId <= 0 && Input.Status != "Ожидает утверждения")
        {
            Input.ApproverId = currentUser.Id;
        }

        Input.Materials = Input.Materials?
            .Where(m => !string.IsNullOrWhiteSpace(m.Name))
            .ToList() ?? [];
        Input.Assignees = AssigneeIds.Select(id =>
            new ModernizationAssignee { EmployeeId = id }).ToList();

        var id = _service.Add(Input);
        _audit.Log(id, currentUser.FullName, "создание заявки на модернизацию");

        if (Files is not null)
        {
            try
            {
                foreach (var f in Files.Where(f => f.Length > 0))
                    await _attachments.SaveAsync(id, f);
            }
            catch (Exception ex)
            {
                TempData["Warning"] = $"Заявка создана, но вложения не сохранились: {ex.Message}";
            }
        }

        return RedirectToPage("/Modernizations/Details", new { id });
    }

    private Employee? CurrentUser()
    {
        var winLogin = User.Identity?.IsAuthenticated == true ? User.Identity.Name : null;
        if (winLogin is not null && winLogin.Contains('\\'))
            winLogin = winLogin.Split('\\')[^1];
        return (winLogin is not null ? Store.FindByLogin(winLogin) : null)
            ?? (int.TryParse(Request.Cookies["currentUserId"], out var cid)
                ? Store.Employees.FirstOrDefault(e => e.Id == cid) : null);
    }
}