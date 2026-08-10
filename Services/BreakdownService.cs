using BreakdownReport.Data;
using BreakdownReport.Models;
using Microsoft.EntityFrameworkCore;

namespace BreakdownReport.Services;

public class BreakdownService
{
    private readonly AppDbContext _db;

    public BreakdownService(AppDbContext db) => _db = db;

    public IReadOnlyList<Breakdown> GetAll() =>
    _db.Breakdowns.Include(b => b.LaborEntries)
        .OrderByDescending(b => b.OccurredAt).ToList();

    public Breakdown? GetById(int id) =>
        _db.Breakdowns.Include(b => b.LaborEntries).FirstOrDefault(b => b.Id == id);

    public int Add(Breakdown breakdown)
    {
        breakdown.CreatedAt = DateTime.Now;
        _db.Breakdowns.Add(breakdown);
        _db.SaveChanges();
        return breakdown.Id;
    }

    public bool Delete(int id)
    {
        var item = _db.Breakdowns.FirstOrDefault(b => b.Id == id);
        if (item is null) return false;
        _db.Breakdowns.Remove(item);
        _db.SaveChanges();
        return true;
    }

    public bool Update(Breakdown input)
    {
        var existing = _db.Breakdowns.Include(b => b.LaborEntries)
            .FirstOrDefault(b => b.Id == input.Id);
        if (existing is null) return false;

        existing.OccurredAt = input.OccurredAt;
        existing.AuthorId = input.AuthorId;
        existing.EquipmentId = input.EquipmentId;
        existing.FailureTypeId = input.FailureTypeId;
        existing.ShortDescription = input.ShortDescription;
        existing.DetailedDescription = input.DetailedDescription;
        existing.LineDowntimeMinutes = input.LineDowntimeMinutes;
        existing.EquipmentDowntimeMinutes = input.EquipmentDowntimeMinutes;
        existing.ThirdPartyFault = input.ThirdPartyFault;

        // бригада заменяется целиком
        _db.LaborEntries.RemoveRange(existing.LaborEntries);
        existing.LaborEntries.Clear();
        existing.LaborEntries.AddRange(input.LaborEntries.Select(l => new LaborEntry
        {
            EmployeeId = l.EmployeeId,
            Minutes = l.Minutes
        }));

        _db.SaveChanges();
        return true;
    }

    public IReadOnlyList<Breakdown> GetFiltered(
        DateTime? from = null,
        DateTime? to = null,
        int? areaId = null,
        int? equipmentId = null,
        int? authorId = null,
        int? crewMemberId = null,
        bool? thirdPartyFault = null,
        string? searchText = null,
        DictionaryStore? store = null)
    {
        IQueryable<Breakdown> query = _db.Breakdowns.Include(b => b.LaborEntries);

        if (from.HasValue) query = query.Where(b => b.OccurredAt >= from.Value);
        if (to.HasValue) query = query.Where(b => b.OccurredAt <= to.Value);
        if (equipmentId.HasValue) query = query.Where(b => b.EquipmentId == equipmentId.Value);
        if (authorId.HasValue) query = query.Where(b => b.AuthorId == authorId.Value);
        if (crewMemberId.HasValue) query = query.Where(b => b.LaborEntries.Any(l => l.EmployeeId == crewMemberId.Value));
        if (thirdPartyFault.HasValue) query = query.Where(b => b.ThirdPartyFault == thirdPartyFault.Value);

        if (areaId.HasValue && store is not null)
        {
            var eqIds = store.EquipmentByArea(areaId.Value).Select(e => e.Id).ToList();
            query = query.Where(b => eqIds.Contains(b.EquipmentId));
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var s = searchText.Trim();
            query = query.Where(b =>
                EF.Functions.Like(b.ShortDescription, $"%{s}%") ||
                EF.Functions.Like(b.DetailedDescription, $"%{s}%"));
        }

        return query.OrderByDescending(b => b.OccurredAt).ToList();
    }
}