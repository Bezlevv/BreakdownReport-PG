namespace BreakdownReport.Models;

public class AuditEntry
{
    public DateTime At { get; set; }
    public int BreakdownId { get; set; }
    public string UserName { get; set; } = "—";
    public string Action { get; set; } = "";
    public string Details { get; set; } = "";
}