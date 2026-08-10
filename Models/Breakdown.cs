using System.ComponentModel.DataAnnotations.Schema;

namespace BreakdownReport.Models;

public sealed class Breakdown
{
    public int Id { get; set; }

    public DateTime OccurredAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public int AuthorId { get; set; }
    public int EquipmentId { get; set; }
    public int FailureTypeId { get; set; }

    public string ShortDescription { get; set; } = string.Empty;
    public string DetailedDescription { get; set; } = string.Empty;

    public int LineDowntimeMinutes { get; set; }
    public int EquipmentDowntimeMinutes { get; set; }

    public bool ThirdPartyFault { get; set; }

    public List<LaborEntry> LaborEntries { get; set; } = [];

    [NotMapped]
    public int TotalLaborMinutes => LaborEntries.Sum(l => l.Minutes);
}