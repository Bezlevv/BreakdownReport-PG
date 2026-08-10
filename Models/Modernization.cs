namespace BreakdownReport.Models;

public class Modernization
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public int AuthorId { get; set; }              // кто оформил заявку
    public int EquipmentId { get; set; }
    public string Customer { get; set; } = "";
    public string ShortDescription { get; set; } = string.Empty;
    public string DetailedDescription { get; set; } = "";
    public DateTime RequiredDate { get; set; }     // необходимая дата выполнения
    public int ApproverId { get; set; }            // инженер, согласовавший заявку
    public string Status { get; set; } = "Новая";  // Новая / В процессе / Завершена
    public List<MaterialItem> Materials { get; set; } = [];
    public List<ModernizationAssignee> Assignees { get; set; } = [];
    public List<ModernizationLabor> LaborEntries { get; set; } = [];
    public List<ModernizationComment> Comments { get; set; } = [];
    public int TotalLaborMinutes => LaborEntries.Sum(l => l.Minutes);
}

public class MaterialItem
{
    public int Id { get; set; }
    public int ModernizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Qty { get; set; } = "";          // «2 шт», «5 м» — свободный формат
}
// Класс коментариев от исполнителей 
public class ModernizationComment
{
    public int Id { get; set; }
    public int ModernizationId { get; set; }
    public string AuthorName { get; set; } = "";
    public DateTime At { get; set; }
    public string Text { get; set; } = "";
}

public class ModernizationLabor
{
    public int Id { get; set; }
    public int ModernizationId { get; set; }
    public int EmployeeId { get; set; }
    public int Minutes { get; set; }
}

public class ModernizationAssignee
{
    public int Id { get; set; }
    public int ModernizationId { get; set; }
    public int EmployeeId { get; set; }
}