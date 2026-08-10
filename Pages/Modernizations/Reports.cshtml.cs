using BreakdownReport.Models;
using BreakdownReport.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BreakdownReport.Pages.Modernizations;

public class ReportsModel : PageModel
{
    private readonly ModernizationService _service;
    private readonly ExcelExportService _export;
    public DictionaryStore Store { get; }

    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? SubmittedFrom { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? SubmittedTo { get; set; }
    [BindProperty(SupportsGet = true)] public int? AreaId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }

    public IReadOnlyList<Modernization> Items { get; private set; } = [];

    public int TotalCount { get; private set; }
    public int PendingCount { get; private set; }
    public int NewCount { get; private set; }
    public int InProgressCount { get; private set; }
    public int DoneCount { get; private set; }
    public int OverdueCount { get; private set; }
    public int TotalLabor { get; private set; }

    public string[] MonthLabels = [];
    public int[] MonthCounts = [];
    public string[] StatusLabels = ["Ожидает утверждения", "Новая", "В процессе", "Завершена"];
    public int[] StatusCounts = [];
    public string[] EquipmentLabels = [];
    public int[] EquipmentCounts = [];
    public int[] EquipmentIds = [];
    public string[] AreaLabels = [];
    public int[] AreaCounts = [];
    public int[] AreaIds = [];
    public string[] EmployeeLabels = [];
    public int[] EmployeeMinutes = [];

    public ReportsModel(ModernizationService service, ExcelExportService export, DictionaryStore store)
    {
        _service = service;
        _export = export;
        Store = store;
    }

    public void OnGet()
    {
        Items = LoadItems();

        TotalCount = Items.Count;
        PendingCount = Items.Count(m => m.Status == "Ожидает утверждения");
        NewCount = Items.Count(m => m.Status == "Новая");
        InProgressCount = Items.Count(m => m.Status == "В процессе");
        DoneCount = Items.Count(m => m.Status == "Завершена");
        OverdueCount = Items.Count(m => m.Status != "Завершена" && m.RequiredDate.Date < DateTime.Today);
        TotalLabor = Items.Sum(m => m.TotalLaborMinutes);

        var months = new List<(string Label, int Count)>();
        var start = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-11);
        for (var d = start; d <= DateTime.Today; d = d.AddMonths(1))
            months.Add(($"{d:MM.yy}",
                Items.Count(m => m.CreatedAt.Year == d.Year && m.CreatedAt.Month == d.Month)));
        MonthLabels = months.Select(m => m.Label).ToArray();
        MonthCounts = months.Select(m => m.Count).ToArray();

        StatusCounts = [PendingCount, NewCount, InProgressCount, DoneCount];

        var topEq = Items.GroupBy(m => m.EquipmentId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).Take(7).ToList();
        EquipmentIds = topEq.Select(x => x.Id).ToArray();
        EquipmentLabels = topEq.Select(x =>
            Store.Equipment.FirstOrDefault(e => e.Id == x.Id)?.Name ?? "—").ToArray();
        EquipmentCounts = topEq.Select(x => x.Count).ToArray();

        var byArea = Items
            .GroupBy(m => Store.Equipment.FirstOrDefault(e => e.Id == m.EquipmentId)?.AreaId ?? 0)
            .Select(g => new { Id = g.Key, Name = Store.FindArea(g.Key)?.Name ?? "—", Count = g.Count() })
            .OrderByDescending(x => x.Count).ToList();
        AreaIds = byArea.Select(x => x.Id).ToArray();
        AreaLabels = byArea.Select(x => x.Name).ToArray();
        AreaCounts = byArea.Select(x => x.Count).ToArray();

        var byEmp = Items.SelectMany(m => m.LaborEntries)
            .GroupBy(l => l.EmployeeId)
            .Select(g => new
            {
                Name = Store.Employees.FirstOrDefault(e => e.Id == g.Key)?.LastName ?? "—",
                Minutes = g.Sum(l => l.Minutes)
            })
            .OrderByDescending(x => x.Minutes).Take(7).ToList();
        EmployeeLabels = byEmp.Select(x => x.Name).ToArray();
        EmployeeMinutes = byEmp.Select(x => x.Minutes).ToArray();
    }

    public IActionResult OnGetExport()
    {
        var bytes = _export.ExportModernizationReports(LoadItems());
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Модернизации_отчёт_{DateTime.Now:yyyy-MM-dd_HH-mm}.xlsx");
    }

    private IReadOnlyList<Modernization> LoadItems() => _service.GetFiltered(
        requiredFrom: From, requiredTo: To,
        submittedFrom: SubmittedFrom, submittedTo: SubmittedTo,
        areaId: AreaId, status: Status, store: Store);
}