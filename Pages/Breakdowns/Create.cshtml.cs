using BreakdownReport.Models;
using BreakdownReport.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BreakdownReport.Pages.Breakdowns;

[DisableRequestSizeLimit]
[RequestFormLimits(ValueLengthLimit = 52428800, ValueCountLimit = 4096)]
public class CreateModel : PageModel
{
    private readonly BreakdownService _breakdowns;
    private readonly AttachmentService _attachments;
    private readonly AuditService _audit;

    public DictionaryStore Store { get; }
    public Employee? CurrentUser { get; private set; }

    [BindProperty] public Breakdown Input { get; set; } = new();
    [BindProperty] public Dictionary<int, bool> LaborIncluded { get; set; } = [];
    [BindProperty] public Dictionary<int, int> LaborMinutes { get; set; } = [];
    [BindProperty] public List<IFormFile>? Files { get; set; }
    [BindProperty] public int? AreaId { get; set; }

    public CreateModel(BreakdownService breakdowns, AttachmentService attachments, AuditService audit, DictionaryStore store)
    {
        _breakdowns = breakdowns;
        _attachments = attachments;
        _audit = audit;
        Store = store;
    }

    public IActionResult OnGet()
    {
        LoadCurrentUser();
        if (Input.OccurredAt == default) Input.OccurredAt = DateTime.Now;
        if (AreaId is null && Input.EquipmentId > 0)
            AreaId = Store.Equipment.FirstOrDefault(e => e.Id == Input.EquipmentId)?.AreaId;
        return Page();
    }


    public async Task<IActionResult> OnPost()
    {
        Console.WriteLine("=== OnPost начался ===");

        LoadCurrentUser();
        Console.WriteLine($"CurrentUser: {CurrentUser?.FullName ?? "null"}");

        if (CurrentUser is null)
            ModelState.AddModelError(string.Empty,
                "Не выбран текущий пользователь — выберите себя в правом верхнем углу и повторите.");

        if (string.IsNullOrWhiteSpace(Input.ShortDescription))
            ModelState.AddModelError("ShortDescription", "Укажите короткое описание поломки");
        if (Input.EquipmentId <= 0)
            ModelState.AddModelError("EquipmentId", "Выберите оборудование");
        if (Input.FailureTypeId <= 0)
            ModelState.AddModelError("FailureTypeId", "Выберите тип поломки");
        if (Input.OccurredAt == default || Input.OccurredAt > DateTime.Now.AddMinutes(5))
            ModelState.AddModelError("OccurredAt", "Укажите корректную дату и время возникновения поломки");

        if (!ModelState.IsValid)
        {
            Console.WriteLine("=== Валидация не пройдена ===");
            return Page();
        }

        Input.AuthorId = CurrentUser!.Id;

        Input.LaborEntries = LaborIncluded
            .Where(kv => kv.Value)
            .Select(kv => new LaborEntry
            {
                EmployeeId = kv.Key,
                Minutes = LaborMinutes.TryGetValue(kv.Key, out var m) ? m : 0
            })
            .ToList();

        Console.WriteLine("=== Перед сохранением в БД ===");
        var id = _breakdowns.Add(Input);
        _audit.Log(id, CurrentUser?.FullName ?? "—", "создание записи");
        Console.WriteLine($"=== Запись создана с id={id} ===");

        // вложения сохраняются сразу после получения id записью
        if (Files is not null)
        {
            Console.WriteLine($"=== Файлов: {Files.Count} ===");
            try
            {
                foreach (var f in Files.Where(f => f.Length > 0))
                {
                    Console.WriteLine($"=== Сохраняем файл: {f.FileName}, размер: {f.Length} ===");
                    await _attachments.SaveAsync(id, f);
                    Console.WriteLine($"=== Файл сохранён ===");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"=== ОШИБКА при сохранении файлов: {ex.Message} ===");
                Console.WriteLine($"=== Стек: {ex.StackTrace} ===");
                TempData["Warning"] = $"Запись создана, но не удалось сохранить вложения: {ex.Message}";
            }
        }
        else
        {
            Console.WriteLine("=== Файлов нет ===");
        }

        Console.WriteLine("=== OnPost завершается ===");
        return RedirectToPage("/Breakdowns/Details", new { id });
    }

    public bool IsIncluded(int employeeId) =>
        LaborIncluded.TryGetValue(employeeId, out var v) && v;


    // Определение текущего пользователя
    private void LoadCurrentUser()
    {
        var winLogin = User.Identity?.IsAuthenticated == true ? User.Identity.Name : null;
        if (winLogin is not null && winLogin.Contains('\\'))
            winLogin = winLogin.Split('\\')[^1];

        CurrentUser = (winLogin is not null ? Store.FindByLogin(winLogin) : null)
            ?? (int.TryParse(Request.Cookies["currentUserId"], out var id)
                ? Store.Employees.FirstOrDefault(e => e.Id == id)
                : null);
    }
}