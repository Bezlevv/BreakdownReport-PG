using BreakdownReport.Models;
using Microsoft.EntityFrameworkCore;

namespace BreakdownReport.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Breakdown> Breakdowns => Set<Breakdown>();
    public DbSet<LaborEntry> LaborEntries => Set<LaborEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Breakdown>(e =>
        {
            e.HasKey(b => b.Id);
            e.HasMany(b => b.LaborEntries)
             .WithOne()
             .HasForeignKey(l => l.BreakdownId)
             .OnDelete(DeleteBehavior.Cascade); // удалили поломку — удалились и трудозатраты
        });

        modelBuilder.Entity<LaborEntry>(e => e.HasKey(l => l.Id));
    }
}