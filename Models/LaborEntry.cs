namespace BreakdownReport.Models;

public sealed class LaborEntry
{
    public int Id { get; set; }
    public int BreakdownId { get; set; }
    public int EmployeeId { get; set; }
    public int Minutes { get; set; }
}