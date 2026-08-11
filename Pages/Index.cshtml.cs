using BreakdownReport.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BreakdownReport.Pages;

public class IndexModel : PageModel
{
    private readonly BreakdownService _breakdowns;
    private readonly ModernizationService _modernizations;

    public int TotalCount { get; private set; }
    public int MonthCount { get; private set; }
    public int ThirdPartyCount { get; private set; }

    public int ModNewCount { get; private set; }
    public int ModPendingCount { get; private set; }
    public int ModInProgressCount { get; private set; }
    public int ModDoneCount { get; private set; }

    public IndexModel(BreakdownService breakdowns, ModernizationService modernizations)
    {
        _breakdowns = breakdowns;
        _modernizations = modernizations;
    }

    public void OnGet()
    {
        var all = _breakdowns.GetAll();
        TotalCount = all.Count;
        MonthCount = all.Count(b =>
            b.OccurredAt.Month == DateTime.Now.Month &&
            b.OccurredAt.Year == DateTime.Now.Year);
        ThirdPartyCount = all.Count(b => b.ThirdPartyFault);

        var mods = _modernizations.GetAll();
        ModNewCount = mods.Count(m => m.Status == "Новая");
        ModPendingCount = mods.Count(m => m.Status == "Ожидает утверждения");
        ModInProgressCount = mods.Count(m => m.Status == "В процессе");
        ModDoneCount = mods.Count(m => m.Status == "Завершена");
    }
}