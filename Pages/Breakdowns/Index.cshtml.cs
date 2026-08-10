using BreakdownReport.Models;
using BreakdownReport.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BreakdownReport.Pages.Breakdowns;

public class IndexModel : PageModel
{
    private readonly BreakdownService _breakdowns;
    private readonly ExcelExportService _export;

    public DictionaryStore Store { get; }

    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true)] public int? AreaId { get; set; }
    [BindProperty(SupportsGet = true)] public int? EquipmentId { get; set; }
    [BindProperty(SupportsGet = true)] public int? AuthorId { get; set; }
    [BindProperty(SupportsGet = true)] public int? CrewMemberId { get; set; }
    [BindProperty(SupportsGet = true)] public string? FaultFilter { get; set; }
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }

    public IReadOnlyList<Breakdown> Items { get; private set; } = [];

    public IndexModel(BreakdownService breakdowns, ExcelExportService export, DictionaryStore store)
    {
        _breakdowns = breakdowns;
        _export = export;
        Store = store;
    }

    public void OnGet() => Items = LoadItems();

    public IActionResult OnGetExport()
    {
        var bytes = _export.ExportBreakdowns(LoadItems());
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Поломки_{DateTime.Now:yyyy-MM-dd_HH-mm}.xlsx");
    }

    private IReadOnlyList<Breakdown> LoadItems()
    {
        bool? thirdParty = FaultFilter switch
        {
            "yes" => true,
            "no" => false,
            _ => null
        };

        var toInclusive = To?.Date.AddDays(1).AddSeconds(-1);

        return _breakdowns.GetFiltered(
            from: From,
            to: toInclusive,
            areaId: AreaId,
            equipmentId: EquipmentId,
            authorId: AuthorId,
            crewMemberId: CrewMemberId,
            thirdPartyFault: thirdParty,
            searchText: Search,
            store: Store);
    }

    public string EquipmentName(int id) =>
        Store.Equipment.FirstOrDefault(e => e.Id == id)?.Name ?? "—";

    public string AreaNameByEquipment(int equipmentId)
    {
        var eq = Store.Equipment.FirstOrDefault(e => e.Id == equipmentId);
        return eq is null ? "—" : Store.FindArea(eq.AreaId)?.Name ?? "—";
    }

    public string EmployeeName(int id) =>
        Store.Employees.FirstOrDefault(e => e.Id == id)?.FullName ?? "—";
}