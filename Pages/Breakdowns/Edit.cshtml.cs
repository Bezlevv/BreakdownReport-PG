using BreakdownReport.Models;
using BreakdownReport.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BreakdownReport.Pages.Breakdowns;

public class EditModel : PageModel
{
    private readonly BreakdownService _breakdowns;
    private readonly AuditService _audit;

    public DictionaryStore Store { get; }

    [BindProperty] public Breakdown Input { get; set; } = new();
    [BindProperty] public Dictionary<int, bool> LaborIncluded { get; set; } = [];
    [BindProperty] public Dictionary<int, int> LaborMinutes { get; set; } = [];

    public EditModel(BreakdownService breakdowns, AuditService audit, DictionaryStore store)
    {
        _breakdowns = breakdowns;
        _audit = audit;
        Store = store;
    }

    public IActionResult OnGet(int id)
    {
        var item = _breakdowns.GetById(id);
        if (item is null) return NotFound();

        Input = item;
        foreach (var l in item.LaborEntries)
        {
            LaborIncluded[l.EmployeeId] = true;
            LaborMinutes[l.EmployeeId] = l.Minutes;
        }
        return Page();
    }

    public IActionResult OnPost(int id)
    {
        if (string.IsNullOrWhiteSpace(Input.ShortDescription))
            ModelState.AddModelError("ShortDescription", "Укажите короткое описание поломки");
        if (Input.EquipmentId <= 0)
            ModelState.AddModelError("EquipmentId", "Выберите оборудование");
        if (Input.AuthorId <= 0)
            ModelState.AddModelError("AuthorId", "Выберите автора");
        if (Input.FailureTypeId <= 0)
            ModelState.AddModelError("FailureTypeId", "Выберите тип поломки");
        if (Input.OccurredAt == default || Input.OccurredAt > DateTime.Now.AddMinutes(5))
            ModelState.AddModelError("OccurredAt", "Укажите корректную дату и время возникновения поломки");

        if (!ModelState.IsValid)
            return Page();

        Input.Id = id;
        Input.LaborEntries = LaborIncluded
            .Where(kv => kv.Value)
            .Select(kv => new LaborEntry
            {
                EmployeeId = kv.Key,
                Minutes = LaborMinutes.TryGetValue(kv.Key, out var m) ? m : 0
            })
            .ToList();

        // сначала считаем, ЧТО изменилось, и только потом сохраняем
        var old = _breakdowns.GetById(id);
        if (old is null) return NotFound();
        var changes = Diff(old, Input);

        if (!_breakdowns.Update(Input))
            return NotFound();

        if (changes.Count > 0)
            _audit.Log(id, CurrentUserName(), "изменение записи", string.Join(", ", changes));

        return RedirectToPage("/Breakdowns/Details", new { id });
    }

    public bool IsIncluded(int employeeId) =>
        LaborIncluded.TryGetValue(employeeId, out var v) && v;

    private List<string> Diff(Breakdown o, Breakdown n)
    {
        var changes = new List<string>();
        if (o.OccurredAt != n.OccurredAt) changes.Add("дата и время");
        if (o.EquipmentId != n.EquipmentId) changes.Add("оборудование");
        if (o.FailureTypeId != n.FailureTypeId) changes.Add("тип поломки");
        if (o.ShortDescription != n.ShortDescription) changes.Add("короткое описание");
        if (o.DetailedDescription != n.DetailedDescription) changes.Add("подробное описание");
        if (o.LineDowntimeMinutes != n.LineDowntimeMinutes) changes.Add("простой линии");
        if (o.EquipmentDowntimeMinutes != n.EquipmentDowntimeMinutes) changes.Add("простой оборудования");
        if (o.ThirdPartyFault != n.ThirdPartyFault) changes.Add("вина третьих лиц");
        if (o.AuthorId != n.AuthorId) changes.Add("автор");
        if (o.TotalLaborMinutes != n.TotalLaborMinutes || o.LaborEntries.Count != n.LaborEntries.Count)
            changes.Add("бригада/трудозатраты");
        return changes;
    }

    private string CurrentUserName()
    {
        var winLogin = User.Identity?.IsAuthenticated == true ? User.Identity.Name : null;
        if (winLogin is not null && winLogin.Contains('\\'))
            winLogin = winLogin.Split('\\')[^1];
        var emp = (winLogin is not null ? Store.FindByLogin(winLogin) : null)
            ?? (int.TryParse(Request.Cookies["currentUserId"], out var cid)
                ? Store.Employees.FirstOrDefault(e => e.Id == cid) : null);
        return emp?.FullName ?? "—";
    }
}