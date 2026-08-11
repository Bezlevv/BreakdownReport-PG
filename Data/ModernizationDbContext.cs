using BreakdownReport.Models;
using Microsoft.EntityFrameworkCore;

namespace BreakdownReport.Data;

public class ModernizationDbContext : DbContext
{
    public ModernizationDbContext(DbContextOptions<ModernizationDbContext> options)
        : base(options) { }

    public DbSet<Modernization> Modernizations => Set<Modernization>();
    public DbSet<MaterialItem> Materials => Set<MaterialItem>();
    public DbSet<ModernizationAssignee> Assignees => Set<ModernizationAssignee>();
    public DbSet<ModernizationLabor> LaborEntries => Set<ModernizationLabor>();
    public DbSet<ModernizationComment> Comments => Set<ModernizationComment>();
}
