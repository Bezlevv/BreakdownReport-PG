using BreakdownReport.Models;
using BreakdownReport.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BreakdownReport.Pages.Modernizations;

public class IndexModel : PageModel
{
    private readonly ModernizationService _service;
    private readonly ExcelExportService _export;
    public DictionaryStore Store { get; }

    public string? Status { get; set; }
    public int? AreaId { get; set; }
    public int? EquipmentId { get; set; }
    public DateTime? RequiredFrom { get; set; }
    public DateTime? RequiredTo { get; set; }
    public DateTime? SubmittedFrom { get; set; }
    public DateTime? SubmittedTo { get; set; }
    public string? SearchText { get; set; }

    public IReadOnlyList<string> Statuses { get; } =
        ["Ожидает утверждения", "Новая", "В процессе", "Завершена"];
    public IReadOnlyList<Modernization> Items { get; private set; } = [];

    public int TotalCount { get; private set; }
    public int PendingCount { get; private set; }
    public int NewCount { get; private set; }
    public int InProgressCount { get; private set; }
    public int DoneCount { get; private set; }
    public int OverdueCount { get; private set; }

    public IndexModel(ModernizationService service, ExcelExportService export, DictionaryStore store)
    {
        _service = service;
        _export = export;
        Store = store;
    }

    public void OnGet(string? status, int? areaId, int? equipmentId,
                      DateTime? requiredFrom, DateTime? requiredTo,
                      DateTime? submittedFrom, DateTime? submittedTo,
                      string? searchText)
    {
        Status = status;
        AreaId = areaId;
        EquipmentId = equipmentId;
        RequiredFrom = requiredFrom;
        RequiredTo = requiredTo;
        SubmittedFrom = submittedFrom;
        SubmittedTo = submittedTo;
        SearchText = searchText;

        Items = LoadItems();

        TotalCount = Items.Count;
        PendingCount = Items.Count(m => m.Status == "Ожидает утверждения");
        NewCount = Items.Count(m => m.Status == "Новая");
        InProgressCount = Items.Count(m => m.Status == "В процессе");
        DoneCount = Items.Count(m => m.Status == "Завершена");
        OverdueCount = Items.Count(m => m.Status != "Завершена" && m.RequiredDate.Date < DateTime.Today);
    }

    public IActionResult OnGetExport(string? status, int? areaId, int? equipmentId,
        DateTime? requiredFrom, DateTime? requiredTo,
        DateTime? submittedFrom, DateTime? submittedTo,
        string? searchText)
    {
        var items = _service.GetFiltered(
            requiredFrom: requiredFrom, requiredTo: requiredTo,
            submittedFrom: submittedFrom, submittedTo: submittedTo,
            areaId: areaId, equipmentId: equipmentId,
            status: status, searchText: searchText, store: Store);

        var bytes = _export.ExportModernizations(items);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Модернизации_{DateTime.Now:yyyy-MM-dd_HH-mm}.xlsx");
    }

    private IReadOnlyList<Modernization> LoadItems() => _service.GetFiltered(
        requiredFrom: RequiredFrom, requiredTo: RequiredTo,
        submittedFrom: SubmittedFrom, submittedTo: SubmittedTo,
        areaId: AreaId, equipmentId: EquipmentId,
        status: Status, searchText: SearchText, store: Store);
}