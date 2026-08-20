namespace BreakdownReport.Models;

public sealed class Employee
{
    public int Id { get; set; }
    public string LastName { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? Position { get; set; }
    public bool CanBeAuthor { get; set; }
    public string[] Aliases { get; set; } = [];
    public string? Login { get; set; }
    public string FullName =>
        string.Join(' ', new[] { LastName, FirstName, MiddleName }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

    public bool IsAdmin =>
        string.Equals(Position, "Admin", StringComparison.OrdinalIgnoreCase);

    public bool IsEngineer =>
        string.Equals(Position, "Engineer", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Position, "инженер", StringComparison.OrdinalIgnoreCase);
}