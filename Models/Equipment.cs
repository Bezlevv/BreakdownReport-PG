namespace BreakdownReport.Models;

public sealed class Equipment
{
    public int Id { get; set; }
    public int AreaId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? InventoryNumber { get; set; }
    public string? OsNumber { get; set; }
}