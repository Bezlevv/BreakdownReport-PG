using BreakdownReport.Models;
using BreakdownReport.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BreakdownReport.Pages;

public class ReportsModel : PageModel
{
    private readonly BreakdownService _breakdowns;
    private readonly ExcelExportService _export;

    public DictionaryStore Store { get; }

    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }

    public ReportSnapshot Snapshot { get; private set; } = new();
    public IReadOnlyList<Breakdown> Items { get; private set; } = [];
    public List<MonthRow> Monthly { get; private set; } = [];
    public List<ReportSnapshot.EquipmentRow> TopEquipment { get; private set; } = [];
    public List<ParetoRow> Pareto { get; private set; } = [];
    public List<ReliabilityRow> Reliability { get; private set; } = [];
    public List<ShiftRow> Shifts { get; private set; } = [];


    public record MonthRow(string Label, int Count, int LineDowntime, string From, string To);
    public record ParetoRow(string TypeName, int Count, int LineDowntime, double CumulativePct);
    public record ReliabilityRow(string EquipmentName, int Count, int MttrMinutes, string MtbfDays);
    public record ShiftRow(string Name, int Count, int LineDowntime);

    public ReportsModel(BreakdownService breakdowns, ExcelExportService export, DictionaryStore store)
    {
        _breakdowns = breakdowns;
        _export = export;
        Store = store;
    }

    public void OnGet() => Compute();

    public IActionResult OnGetExport()
    {
        Compute();
        return File(_export.ExportReports(Snapshot, Items),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Отчет_{DateTime.Now:yyyy-MM-dd_HH-mm}.xlsx");
    }

    private void Compute()
    {
        var toInclusive = To?.Date.AddDays(1).AddSeconds(-1);
        var items = _breakdowns.GetFiltered(from: From, to: toInclusive, store: Store);
        Items = items;

        Snapshot = new ReportSnapshot
        {
            TotalCount = items.Count,
            ThirdPartyCount = items.Count(b => b.ThirdPartyFault),
            TotalLineDowntime = items.Sum(b => b.LineDowntimeMinutes),
            TotalEquipmentDowntime = items.Sum(b => b.EquipmentDowntimeMinutes),
            TotalLabor = items.Sum(b => b.TotalLaborMinutes),

            Repeats = items
                .GroupBy(b => b.ShortDescription.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => new ReportSnapshot.RepeatRow(g.Key, g.Count(),
                    g.Sum(b => b.LineDowntimeMinutes), g.Max(b => b.OccurredAt)))
                .OrderByDescending(g => g.Count)
                .ToList(),

            Areas = items
                .GroupBy(b => AreaName(b.EquipmentId))
                .Select(g => new ReportSnapshot.AreaRow(g.Key, g.Count(),
                    g.Sum(b => b.LineDowntimeMinutes), g.Sum(b => b.EquipmentDowntimeMinutes)))
                .OrderByDescending(x => x.LineDowntime)
                .ToList(),

            Equipment = items
                .GroupBy(b => b.EquipmentId)
                .Select(g => new ReportSnapshot.EquipmentRow(AreaName(g.Key), EquipmentName(g.Key), g.Count(),
                    g.Sum(b => b.LineDowntimeMinutes), g.Sum(b => b.EquipmentDowntimeMinutes)))
                .OrderByDescending(x => x.Count)
                .ToList(),

            Employees = items
                .SelectMany(b => b.LaborEntries)
                .GroupBy(l => l.EmployeeId)
                .Select(g => new ReportSnapshot.EmployeeRow(EmployeeName(g.Key), g.Count(), g.Sum(l => l.Minutes)))
                .OrderByDescending(x => x.Minutes)
                .ToList()
        };

        // Динамика за последние 12 месяцев
        Monthly = Enumerable.Range(0, 12).Reverse().Select(off =>
        {
            var d = DateTime.Now.AddMonths(-off);
            var first = new DateTime(d.Year, d.Month, 1);
            var last = first.AddMonths(1).AddDays(-1);
            var list = items.Where(b => b.OccurredAt.Year == d.Year && b.OccurredAt.Month == d.Month).ToList();
            return new MonthRow(
                $"{d.Month:D2}.{d.Year % 100:D2}",
                list.Count,
                list.Sum(b => b.LineDowntimeMinutes),
                first.ToString("yyyy-MM-dd"),
                last.ToString("yyyy-MM-dd"));
        }).ToList();

        // Топ-6 оборудования по простою линии
        TopEquipment = Snapshot.Equipment.OrderByDescending(e => e.LineDowntime).Take(6).ToList();

        // Pareto по типам поломок (по простою линии)
        var byType = items
            .GroupBy(b => b.FailureTypeId)
            .Select(g => new
            {
                Name = Store.FailureTypes.FirstOrDefault(t => t.Id == g.Key)?.Name ?? "—",
                Count = g.Count(),
                Downtime = g.Sum(b => b.LineDowntimeMinutes)
            })
            .OrderByDescending(x => x.Downtime)
            .ToList();
        var totalDt = byType.Sum(x => x.Downtime);
        var cum = 0;
        Pareto = byType.Select(x =>
        {
            cum += x.Downtime;
            return new ParetoRow(x.Name, x.Count, x.Downtime,
                totalDt == 0 ? 0 : Math.Round(100.0 * cum / totalDt, 1));
        }).ToList();

        // Надёжность: MTTR и MTBF по каждому оборудованию
        Reliability = items
            .GroupBy(b => b.EquipmentId)
            .Select(g =>
            {
                var count = g.Count();
                var mttr = count == 0 ? 0 : g.Sum(b => b.EquipmentDowntimeMinutes) / count;
                var mtbf = "—";
                if (count >= 2)
                {
                    var days = (g.Max(b => b.OccurredAt) - g.Min(b => b.OccurredAt)).TotalDays;
                    mtbf = (days / (count - 1)).ToString("0.0");
                }
                return new ReliabilityRow(EquipmentName(g.Key), count, mttr, mtbf);
            })
            .OrderByDescending(r => r.Count)
            .ToList();

        // Распределение по сменам
        Shifts = Enumerable.Range(1, 3).Select(s =>
        {
            var list = items.Where(b => ShiftOf(b.OccurredAt) == s).ToList();
            return new ShiftRow(
                s switch { 1 => "День (07–16)", 2 => "Вечер (16–24)", _ => "Ночь (00–07)" },
                list.Count, list.Sum(b => b.LineDowntimeMinutes));
        }).ToList();
    }

    private string AreaName(int equipmentId)
    {
        var eq = Store.Equipment.FirstOrDefault(e => e.Id == equipmentId);
        return eq is null ? "—" : Store.FindArea(eq.AreaId)?.Name ?? "—";
    }

    private string EquipmentName(int id) =>
        Store.Equipment.FirstOrDefault(e => e.Id == id)?.Name ?? "—";

    private string EmployeeName(int id) =>
        Store.Employees.FirstOrDefault(e => e.Id == id)?.FullName ?? "—";

    private static int ShiftOf(DateTime dt) => dt.Hour switch
    {
        >= 8 and < 16 => 1,
        >= 16 => 2,
        _ => 3
    };
}