using BreakdownReport.Data;
using BreakdownReport.Models;
using Microsoft.EntityFrameworkCore;

namespace BreakdownReport.Services;

public class ModernizationService
{
    private readonly ModernizationDbContext _db;
    public ModernizationService(ModernizationDbContext db) => _db = db;

    public IReadOnlyList<Modernization> GetAll() =>
        _db.Modernizations
            .Include(m => m.Materials)
            .Include(m => m.Assignees)
            .Include(m => m.LaborEntries)
            .Include(m => m.Comments)
            .AsEnumerable()
            .OrderBy(m => m.Status == "Завершена")
            .ThenBy(m => m.RequiredDate)
            .ToList();

    public Modernization? GetById(int id) =>
        _db.Modernizations
            .Include(m => m.Materials)
            .Include(m => m.Assignees)
            .Include(m => m.LaborEntries)
            .Include(m => m.Comments)
            .FirstOrDefault(m => m.Id == id);

    public int Add(Modernization m)
    {
        if (m.CreatedAt == default) m.CreatedAt = DateTime.Now;
        _db.Modernizations.Add(m);
        _db.SaveChanges();
        return m.Id;
    }

    public bool Update(Modernization input)
    {
        var existing = _db.Modernizations
            .Include(m => m.Materials)
            .Include(m => m.Assignees)
            .Include(m => m.LaborEntries)
            .FirstOrDefault(m => m.Id == input.Id);
        if (existing is null) return false;

        existing.EquipmentId = input.EquipmentId;
        existing.Customer = input.Customer;
        existing.ShortDescription = input.ShortDescription;
        existing.DetailedDescription = input.DetailedDescription;
        existing.RequiredDate = input.RequiredDate;
        existing.ApproverId = input.ApproverId;
        existing.Status = input.Status;

        _db.Materials.RemoveRange(existing.Materials);
        existing.Materials.Clear();
        existing.Materials.AddRange(input.Materials.Select(x =>
            new MaterialItem { Name = x.Name, Qty = x.Qty }));

        _db.Assignees.RemoveRange(existing.Assignees);
        existing.Assignees.Clear();
        existing.Assignees.AddRange(input.Assignees.Select(x =>
            new ModernizationAssignee { EmployeeId = x.EmployeeId }));

        _db.LaborEntries.RemoveRange(existing.LaborEntries);
        existing.LaborEntries.Clear();
        existing.LaborEntries.AddRange(input.LaborEntries.Select(l =>
            new ModernizationLabor { EmployeeId = l.EmployeeId, Minutes = l.Minutes }));

        _db.SaveChanges();
        return true;
    }

    public bool Delete(int id)
    {
        var item = _db.Modernizations.FirstOrDefault(m => m.Id == id);
        if (item is null) return false;
        _db.Modernizations.Remove(item);
        _db.SaveChanges();
        return true;
    }

    public IReadOnlyList<Modernization> GetFiltered(
        DateTime? requiredFrom = null,
        DateTime? requiredTo = null,
        DateTime? submittedFrom = null,
        DateTime? submittedTo = null,
        int? areaId = null,
        int? equipmentId = null,
        string? status = null,
        string? searchText = null,
        DictionaryStore? store = null)
    {
        IQueryable<Modernization> query = _db.Modernizations
            .Include(m => m.Materials)
            .Include(m => m.Assignees)
            .Include(m => m.LaborEntries)
            .Include(m => m.Comments);

        if (requiredFrom.HasValue)
        {
            var from = requiredFrom.Value;
            query = query.Where(m => m.RequiredDate >= from);
        }
        if (requiredTo.HasValue)
        {
            var to = requiredTo.Value.Date.AddDays(1).AddSeconds(-1);
            query = query.Where(m => m.RequiredDate <= to);
        }
        if (submittedFrom.HasValue)
        {
            var sFrom = submittedFrom.Value;
            query = query.Where(m => m.CreatedAt >= sFrom);
        }
        if (submittedTo.HasValue)
        {
            var sTo = submittedTo.Value.Date.AddDays(1).AddSeconds(-1);
            query = query.Where(m => m.CreatedAt <= sTo);
        }
        if (equipmentId.HasValue)
        {
            var eq = equipmentId.Value;
            query = query.Where(m => m.EquipmentId == eq);
        }
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(m => m.Status == status);
        if (areaId.HasValue && store is not null)
        {
            var area = areaId.Value;
            var eqIds = store.EquipmentByArea(area).Select(e => e.Id).ToList();
            query = query.Where(m => eqIds.Contains(m.EquipmentId));
        }
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var s = searchText.Trim();
            query = query.Where(m =>
                EF.Functions.Like(m.ShortDescription, $"%{s}%") ||
                EF.Functions.Like(m.DetailedDescription, $"%{s}%"));
        }

        return query.AsEnumerable()
            .OrderBy(m => m.Status == "Завершена")
            .ThenBy(m => m.RequiredDate)
            .ToList();
    }

    public void AddComment(int id, string authorName, string text)
    {
        _db.Comments.Add(new ModernizationComment
        {
            ModernizationId = id,
            AuthorName = authorName,
            At = DateTime.Now,
            Text = text
        });
        _db.SaveChanges();
    }

    public bool Approve(int id, int approverId)
    {
        var item = GetById(id);
        if (item is null) return false;
        item.Status = "В процессе";
        item.ApproverId = approverId;
        return Update(item);
    }
}