namespace BreakdownReport.Models;

public sealed class Area
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string[] Aliases { get; set; } = [];
}