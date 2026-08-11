using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Extensions.Configuration;

namespace BreakdownReport.Services;

public sealed class BackupMaker
{
	private readonly string _dataFolder;
	private readonly IConfiguration _config;
	private readonly string _backupsFolder;
	private readonly object _lock = new();

	public BackupMaker(string dataFolder, IConfiguration config)
	{
		_dataFolder = dataFolder;
		_config = config;
		_backupsFolder = Path.Combine(dataFolder, "backups");
		Directory.CreateDirectory(_backupsFolder);
	}

	public string BackupsFolder => _backupsFolder;

	public string? Latest() =>
		Directory.GetFiles(_backupsFolder, "backup_*.zip")
			.OrderByDescending(f => f).FirstOrDefault();

	public string CreateBackup()
	{
		lock (_lock)
		{
			var temp = Path.Combine(Path.GetTempPath(), "brbackup_" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(temp);
			try
			{
				// 1. Дампы обеих баз PostgreSQL через pg_dump
				Dump("Breakdowns", "breakdowns.sql", temp);
				Dump("Modernizations", "modernizations.sql", temp);

				// 2. Журналы аудита обоих модулей
				CopyIfExists("audit.json", temp);
				CopyIfExists("modernizations-audit.json", temp);

				// 3. Вложения
				CopyDirIfExists("attachments", temp);
				CopyDirIfExists("attachments-mod", temp);

				// 4. Упаковываем
				var zipPath = Path.Combine(_backupsFolder,
					$"backup_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.zip");
				ZipFile.CreateFromDirectory(temp, zipPath);

				// 5. Храним максимум 3 копии
				foreach (var old in Directory.GetFiles(_backupsFolder, "backup_*.zip")
							 .OrderByDescending(f => f).Skip(3))
					File.Delete(old);

				return zipPath;
			}
			finally
			{
				try { Directory.Delete(temp, true); } catch { }
			}
		}
	}

	private void Dump(string connName, string fileName, string temp)
	{
		var conn = _config.GetConnectionString(connName)
				   ?? throw new InvalidOperationException($"Нет строки подключения {connName}");
		var pgDump = _config.GetValue<string>("PgDumpPath")
					 ?? @"C:\Program Files\PostgreSQL\17\bin\pg_dump.exe";

		var psi = new ProcessStartInfo
		{
			FileName = pgDump,
			UseShellExecute = false,
			RedirectStandardError = true
		};
		psi.ArgumentList.Add($"--dbname={conn}");
		psi.ArgumentList.Add("--format=plain");
		psi.ArgumentList.Add($"--file={Path.Combine(temp, fileName)}");

		using var p = Process.Start(psi)
				  ?? throw new InvalidOperationException("Не удалось запустить pg_dump");
		p.WaitForExit();
		if (p.ExitCode != 0)
			throw new InvalidOperationException($"pg_dump ({connName}) ошибка: {p.StandardError.ReadToEnd()}");
	}

	private void CopyIfExists(string fileName, string temp)
	{
		var srcPath = Path.Combine(_dataFolder, fileName);
		if (File.Exists(srcPath))
		{
			using var src = new FileStream(srcPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using var dst = new FileStream(Path.Combine(temp, fileName), FileMode.Create, FileAccess.Write);
			src.CopyTo(dst);
		}
	}

	private void CopyDirIfExists(string dirName, string temp)
	{
		var src = Path.Combine(_dataFolder, dirName);
		if (!Directory.Exists(src)) return;
		var dst = Path.Combine(temp, dirName);
		Directory.CreateDirectory(dst);
		foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
			Directory.CreateDirectory(dir.Replace(src, dst));
		foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
			File.Copy(file, file.Replace(src, dst), true);
	}
}