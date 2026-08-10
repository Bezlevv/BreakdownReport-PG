using System.Text.Encodings.Web;
using System.Text.Json;
using BreakdownReport.Models;

namespace BreakdownReport.Services;

public class AuditService
{
    private readonly string _path;
    private readonly List<AuditEntry> _entries = [];
    private readonly object _lock = new();

    public AuditService(string path)
    {
        _path = path;
        if (File.Exists(path))
            _entries = JsonSerializer.Deserialize<List<AuditEntry>>(File.ReadAllText(path)) ?? [];
    }

    public void Log(int breakdownId, string userName, string action, string details = "")
    {
        lock (_lock)
        {
            _entries.Add(new AuditEntry
            {
                At = DateTime.Now,
                BreakdownId = breakdownId,
                UserName = userName,
                Action = action,
                Details = details
            });
            File.WriteAllText(_path, JsonSerializer.Serialize(_entries,
                new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        }
    }

    public IReadOnlyList<AuditEntry> For(int breakdownId)
    {
        lock (_lock)
            return _entries.Where(e => e.BreakdownId == breakdownId)
                           .OrderByDescending(e => e.At).ToList();
    }
}