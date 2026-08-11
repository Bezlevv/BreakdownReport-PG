using BreakdownReport.Data;
using BreakdownReport.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BreakdownReport.Services;

/// <summary>
/// Одноразовый перенос всех данных из SQLite-баз в PostgreSQL.
/// Вызывается из Program.cs при MigrateFromSqlite = true и пустых PG-базах.
/// </summary>
public sealed class SqliteToPostgresMigrator
{
	private readonly IServiceProvider _services;
	private readonly ILogger<SqliteToPostgresMigrator> _logger;

	public SqliteToPostgresMigrator(IServiceProvider services, ILogger<SqliteToPostgresMigrator> logger)
	{
		_services = services;
		_logger = logger;
	}

	public void Run(string sqliteDataFolder)
	{
		var bdnPath = Path.Combine(sqliteDataFolder, "breakdowns.db");
		var modPath = Path.Combine(sqliteDataFolder, "modernizations.db");

		if (!File.Exists(bdnPath) && !File.Exists(modPath))
		{
			_logger.LogWarning("SQLite-файлы не найдены в {Folder} — миграция пропущена", sqliteDataFolder);
			return;
		}

		using var scope = _services.CreateScope();
		var bdnDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var modDb = scope.ServiceProvider.GetRequiredService<ModernizationDbContext>();

		// Проверка: если в PG уже есть данные — не перезаписываем
		if (bdnDb.Breakdowns.Any() || modDb.Modernizations.Any())
		{
			_logger.LogWarning("PG-базы не пусты — миграция пропущена (чтобы не дублировать данные)");
			return;
		}

		_logger.LogInformation("=== Начало миграции из SQLite ({Folder}) ===", sqliteDataFolder);

		if (File.Exists(bdnPath)) MigrateBreakdowns(bdnPath, bdnDb);
		if (File.Exists(modPath)) MigrateModernizations(modPath, modDb);

		_logger.LogInformation("=== Миграция завершена успешно ===");
	}

	private void MigrateBreakdowns(string dbPath, AppDbContext target)
	{
		using var conn = new SqliteConnection($"Data Source={dbPath}");
		conn.Open();

		// Считываем все поломки
		var breakdowns = new List<Breakdown>();
		using (var cmd = conn.CreateCommand())
		{
			cmd.CommandText = @"SELECT Id, OccurredAt, AuthorId, EquipmentId, FailureTypeId,
                           ShortDescription, DetailedDescription,
                           LineDowntimeMinutes, EquipmentDowntimeMinutes,
                           ThirdPartyFault, CreatedAt
                    FROM Breakdowns ORDER BY Id";
			using var r = cmd.ExecuteReader();
			while (r.Read())
			{
				breakdowns.Add(new Breakdown
				{
					OccurredAt = r.GetDateTime(1),
					AuthorId = r.IsDBNull(2) ? 0 : r.GetInt32(2),
					EquipmentId = r.IsDBNull(3) ? 0 : r.GetInt32(3),
					FailureTypeId = r.IsDBNull(4) ? 0 : r.GetInt32(4),
					ShortDescription = r.IsDBNull(5) ? "" : r.GetString(5),
					DetailedDescription = r.IsDBNull(6) ? "" : r.GetString(6),
					LineDowntimeMinutes = r.IsDBNull(7) ? 0 : r.GetInt32(7),
					EquipmentDowntimeMinutes = r.IsDBNull(8) ? 0 : r.GetInt32(8),
					ThirdPartyFault = !r.IsDBNull(9) && r.GetInt32(9) != 0,
					CreatedAt = r.IsDBNull(10) ? DateTime.Now : r.GetDateTime(10)
				});
			}
		}

		// Считываем трудозатраты
		var laborByBreakdown = new Dictionary<int, List<LaborEntry>>();
		using (var cmd = conn.CreateCommand())
		{
			cmd.CommandText = "SELECT BreakdownId, EmployeeId, Minutes FROM LaborEntries";
			using var r = cmd.ExecuteReader();
			while (r.Read())
			{
				var bid = r.GetInt32(0);
				if (!laborByBreakdown.TryGetValue(bid, out var list))
					laborByBreakdown[bid] = list = [];
				list.Add(new LaborEntry
				{
					EmployeeId = r.GetInt32(1),
					Minutes = r.GetInt32(2)
				});
			}
		}

		// Привязываем трудозатраты к поломкам (сопоставляем по порядку Id)
			using (var cmd = conn.CreateCommand())
		{
			cmd.CommandText = "SELECT Id FROM Breakdowns ORDER BY Id";
			using var r = cmd.ExecuteReader();
			var i = 0;
			while (r.Read())
			{
				var sqliteId = r.GetInt32(0);
				var b = breakdowns[i++];
				if (laborByBreakdown.TryGetValue(sqliteId, out var list))
					foreach (var l in list) b.LaborEntries.Add(l);
			}
		}

		foreach (var b in breakdowns) target.Breakdowns.Add(b);
		target.SaveChanges();
		_logger.LogInformation("Поломки: перенесено {N} записей", breakdowns.Count);
	}

	private void MigrateModernizations(string dbPath, ModernizationDbContext target)
	{
		using var conn = new SqliteConnection($"Data Source={dbPath}");
		conn.Open();

		// Id-соответствие понадобится для связей
		var idMap = new Dictionary<int, int>(); // sqliteId -> pgId

		using (var cmd = conn.CreateCommand())
		{
			cmd.CommandText = @"SELECT Id, EquipmentId, Customer, ShortDescription, DetailedDescription,
                                       RequiredDate, ApproverId, Status, CreatedAt
                                FROM Modernizations";
			using var r = cmd.ExecuteReader();
			while (r.Read())
			{
				var sqliteId = r.GetInt32(0);
				var m = new Modernization
				{
					EquipmentId = r.IsDBNull(1) ? 0 : r.GetInt32(1),
					Customer = r.IsDBNull(2) ? "" : r.GetString(2),
					ShortDescription = r.IsDBNull(3) ? "" : r.GetString(3),
					DetailedDescription = r.IsDBNull(4) ? "" : r.GetString(4),
					RequiredDate = r.IsDBNull(5) ? DateTime.Now : r.GetDateTime(5),
					ApproverId = r.IsDBNull(6) ? 0 : r.GetInt32(6),
					Status = r.IsDBNull(7) ? "Новая" : r.GetString(7),
					CreatedAt = r.IsDBNull(8) ? DateTime.Now : r.GetDateTime(8)
				};
				target.Modernizations.Add(m);
				target.SaveChanges();
				idMap[sqliteId] = m.Id;
			}
		}
		_logger.LogInformation("Модернизации: перенесено {N} записей", idMap.Count);

		// Materials
		using (var cmd = conn.CreateCommand())
		{
			cmd.CommandText = "SELECT ModernizationId, Name, Qty FROM Materials";
			using var r = cmd.ExecuteReader();
			while (r.Read())
			{
				var sid = r.GetInt32(0);
				if (!idMap.TryGetValue(sid, out var pgId)) continue;
				target.Materials.Add(new MaterialItem
				{
					ModernizationId = pgId,
					Name = r.IsDBNull(1) ? "" : r.GetString(1),
					Qty = r.IsDBNull(2) ? "" : r.GetString(2)
				});
			}
		}

		// Assignees
		using (var cmd = conn.CreateCommand())
		{
			cmd.CommandText = "SELECT ModernizationId, EmployeeId FROM Assignees";
			using var r = cmd.ExecuteReader();
			while (r.Read())
			{
				var sid = r.GetInt32(0);
				if (!idMap.TryGetValue(sid, out var pgId)) continue;
				target.Assignees.Add(new ModernizationAssignee
				{
					ModernizationId = pgId,
					EmployeeId = r.GetInt32(1)
				});
			}
		}

		// LaborEntries (модернизаций)
		using (var cmd = conn.CreateCommand())
		{
			cmd.CommandText = "SELECT ModernizationId, EmployeeId, Minutes FROM LaborEntries";
			using var r = cmd.ExecuteReader();
			while (r.Read())
			{
				var sid = r.GetInt32(0);
				if (!idMap.TryGetValue(sid, out var pgId)) continue;
				target.LaborEntries.Add(new ModernizationLabor
				{
					ModernizationId = pgId,
					EmployeeId = r.GetInt32(1),
					Minutes = r.GetInt32(2)
				});
			}
		}

		// Comments
		using (var cmd = conn.CreateCommand())
		{
			cmd.CommandText = "SELECT ModernizationId, AuthorName, At, Text FROM Comments";
			using var r = cmd.ExecuteReader();
			while (r.Read())
			{
				var sid = r.GetInt32(0);
				if (!idMap.TryGetValue(sid, out var pgId)) continue;
				target.Comments.Add(new ModernizationComment
				{
					ModernizationId = pgId,
					AuthorName = r.IsDBNull(1) ? "" : r.GetString(1),
					At = r.IsDBNull(2) ? DateTime.Now : r.GetDateTime(2),
					Text = r.IsDBNull(3) ? "" : r.GetString(3)
				});
			}
		}

		target.SaveChanges();
		_logger.LogInformation("Дочерние записи модернизаций перенесены");
	}
}