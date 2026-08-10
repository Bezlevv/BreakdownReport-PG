using BreakdownReport.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BreakdownReport.Pages;

public class IndexModel : PageModel
{
    private readonly BreakdownService _breakdowns;

    public int TotalCount { get; private set; }
    public int MonthCount { get; private set; }
    public int ThirdPartyCount { get; private set; }

    public IndexModel(BreakdownService breakdowns) => _breakdowns = breakdowns;

    public void OnGet()
    {
        var all = _breakdowns.GetAll();
        TotalCount = all.Count;
        MonthCount = all.Count(b =>
            b.OccurredAt.Month == DateTime.Now.Month &&
            b.OccurredAt.Year == DateTime.Now.Year);
        ThirdPartyCount = all.Count(b => b.ThirdPartyFault);
    }
}