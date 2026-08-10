using BreakdownReport.Models;
using BreakdownReport.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BreakdownReport.Pages.Modernizations;

public class EditModel : PageModel
{
    private readonly ModernizationService _service;
    private readonly ModernizationAuditService _audit;

    public DictionaryStore Store { get; }

    [BindProperty] public Modernization Input { get; set; } = new();
    [BindProperty] public int? AreaId { get; set; }
    [BindProperty] public List<int> AssigneeIds { get; set; } = [];
    [BindProperty] public Dictionary<int, int> LaborMinutes { get; set; } = [];

    public bool IsApprover { get; private set; }

    public IReadOnlyList<string> Statuses { get; } =
        ["Ожидает утверждения", "Новая", "В процессе", "Завершена"];

    public EditModel(ModernizationService service, ModernizationAuditService audit, DictionaryStore store)
    {
        _service = service;
        _audit = audit;
        Store = store;
    }

    public IActionResult OnGet(int id)
    {
        var item = _service.GetById(id);
        if (item is null) return NotFound();
        Input = item;
        AreaId = Store.Equipment.FirstOrDefault(e => e.Id == item.EquipmentId)?.AreaId;
        AssigneeIds = item.Assignees.Select(a => a.EmployeeId).ToList();
        foreach (var l in item.LaborEntries)
            LaborMinutes[l.EmployeeId] = l.Minutes;
        IsApprover = CurrentUser() is { } u && (u.IsEngineer || u.IsAdmin);
        return Page();
    }

    public IActionResult OnPost(int id)
    {
        var user = CurrentUser();
        IsApprover = user is not null && (user.IsEngineer || user.IsAdmin);

        if (Input.CreatedAt == default || Input.CreatedAt > DateTime.Now.AddMinutes(5))
            ModelState.AddModelError("CreatedAt", "Укажите корректную дату подачи заявки");

        var old = _service.GetById(id);
        if (old is null) return NotFound();

        // Обычный сотрудник корректирует и дополняет, но не меняет статус и согласующего
        if (!IsApprover)
        {
            Input.Status = old.Status;
            Input.ApproverId = old.ApproverId;
        }

        if (string.IsNullOrWhiteSpace(Input.ShortDescription))
            ModelState.AddModelError("ShortDescription", "Укажите краткое описание");
        if (Input.EquipmentId <= 0)
            ModelState.AddModelError("EquipmentId", "Выберите оборудование");
        if (string.IsNullOrWhiteSpace(Input.Customer))
            ModelState.AddModelError("Customer", "Укажите заказчика");
        if (IsApprover && Input.ApproverId <= 0)
            ModelState.AddModelError("ApproverId", "Выберите инженера");
        if (Input.RequiredDate == default)
            ModelState.AddModelError("RequiredDate", "Укажите необходимую дату");

        if (!ModelState.IsValid)
            return Page();

        Input.Id = id;
        Input.Materials = Input.Materials?
            .Where(m => !string.IsNullOrWhiteSpace(m.Name)).ToList() ?? [];
        Input.Assignees = AssigneeIds.Select(i =>
            new ModernizationAssignee { EmployeeId = i }).ToList();

        Input.LaborEntries = LaborMinutes
            .Where(kv => kv.Value > 0)
            .Select(kv => new ModernizationLabor { EmployeeId = kv.Key, Minutes = kv.Value })
            .ToList();

        var changes = Diff(old, Input);

        if (!_service.Update(Input))
            return NotFound();

        if (changes.Count > 0)
            _audit.Log(id, CurrentUserName(), "изменение заявки на модернизацию", string.Join(", ", changes));

        return RedirectToPage("/Modernizations/Details", new { id });
    }

    private List<string> Diff(Modernization o, Modernization n)
    {
        var c = new List<string>();
        if (o.EquipmentId != n.EquipmentId) c.Add("оборудование");
        if (o.Customer != n.Customer) c.Add("заказчик");
        if (o.ShortDescription != n.ShortDescription) c.Add("краткое описание");
        if (o.DetailedDescription != n.DetailedDescription) c.Add("полное описание");
        if (o.RequiredDate != n.RequiredDate) c.Add("необходимая дата");
        if (o.ApproverId != n.ApproverId) c.Add("инженер");
        if (o.Status != n.Status) c.Add("статус");
        if (!o.Materials.Select(m => m.Name + "|" + m.Qty).OrderBy(x => x)
             .SequenceEqual(n.Materials.Select(m => m.Name + "|" + m.Qty).OrderBy(x => x)))
            c.Add("материалы");
        if (!o.Assignees.Select(a => a.EmployeeId).OrderBy(x => x)
             .SequenceEqual(n.Assignees.Select(a => a.EmployeeId).OrderBy(x => x)))
            c.Add("исполнители");
        if (!o.LaborEntries.Select(l => l.EmployeeId + ":" + l.Minutes).OrderBy(x => x)
             .SequenceEqual(n.LaborEntries.Select(l => l.EmployeeId + ":" + l.Minutes).OrderBy(x => x)))
            c.Add("трудозатраты");
        return c;
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

    private string CurrentUserName() => CurrentUser()?.FullName ?? "—";
}