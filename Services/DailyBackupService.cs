namespace BreakdownReport.Services;

public sealed class DailyBackupService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<DailyBackupService> _logger;

    public DailyBackupService(IServiceProvider services, ILogger<DailyBackupService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var next = now.Date.AddHours(8);         // 8:00 сегодня
            if (next <= now) next = next.AddDays(1); // или завтра
            _logger.LogInformation("Следующий резервный копир в {Next}", next);

            try { await Task.Delay(next - now, stoppingToken); }
            catch (OperationCanceledException) { break; }

            try
            {
                var maker = _services.GetRequiredService<BackupMaker>();
                var path = maker.CreateBackup();
                _logger.LogInformation("Резервная копия создана: {Path}", path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка создания резервной копии");
            }
        }
    }
}