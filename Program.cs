using System.Text.Json;
using BreakdownReport.Data;
using BreakdownReport.Models;
using BreakdownReport.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();

var useWinAuth = builder.Configuration.GetValue<bool>("UseWindowsAuth");
if (useWinAuth)
{
    builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
    builder.Services.AddAuthorization(options =>
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build());
}

// Справочники — по-прежнему из JSON
builder.Services.AddSingleton(_ =>
    DictionaryStore.LoadFromFolder(
        Path.Combine(builder.Environment.ContentRootPath, "Config")));

// Папка данных: вложения, журналы, бэкапы (базы теперь в PostgreSQL)
var dataFolder = Path.Combine(builder.Environment.ContentRootPath, "Data");
Directory.CreateDirectory(dataFolder);

// ----- Базы в PostgreSQL -----
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Breakdowns")));
builder.Services.AddScoped<BreakdownService>();

builder.Services.AddDbContext<ModernizationDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Modernizations")));
builder.Services.AddScoped<ModernizationService>();

builder.Services.AddSingleton(_ => new ModernizationAttachmentService(
    Path.Combine(dataFolder, "attachments-mod")));
builder.Services.AddSingleton(_ => new ModernizationAuditService(
    Path.Combine(dataFolder, "modernizations-audit.json")));

// Сервис excel
builder.Services.AddSingleton<ExcelExportService>();

// Сервис вложений поломок
builder.Services.AddSingleton(_ => new AttachmentService(
    Path.Combine(dataFolder, "attachments")));

// Сервис учета изменений заявок поломок
builder.Services.AddSingleton(_ => new AuditService(
    Path.Combine(dataFolder, "audit.json")));

// Резервное копирование PostgreSQL: ручное + ежедневное в 8:00, храним 3 копии
builder.Services.AddSingleton(sp => new BackupMaker(dataFolder, sp.GetRequiredService<IConfiguration>()));
builder.Services.AddHostedService<DailyBackupService>();

// Одноразовый перенос данных из SQLite в PostgreSQL
builder.Services.AddSingleton<SqliteToPostgresMigrator>();

var app = builder.Build();

//Для отладки
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// Создание структуры БД + миграция из SQLite + импорт из старого JSON
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    scope.ServiceProvider.GetRequiredService<ModernizationDbContext>().Database.EnsureCreated();

    // Перенос данных из SQLite (по флагу MigrateFromSqlite в appsettings.json)
    if (app.Configuration.GetValue<bool>("MigrateFromSqlite", false))
    {
        var sqliteFolder = app.Configuration.GetValue<string>("SqliteDataFolder") ?? dataFolder;
        scope.ServiceProvider.GetRequiredService<SqliteToPostgresMigrator>()
            .Run(Path.GetFullPath(sqliteFolder));
    }

    var historyFile = Path.Combine(dataFolder, "history.json");
    if (File.Exists(historyFile) && !db.Breakdowns.Any())
    {
        var old = JsonSerializer.Deserialize<List<Breakdown>>(
                      File.ReadAllText(historyFile),
                      new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        db.Breakdowns.AddRange(old);
        db.SaveChanges();
        File.Move(historyFile, historyFile + ".imported");
    }
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// Скрываем модуль модернизаций, если флаг ShowModernizations выключен
var showModernizations = app.Configuration.GetValue<bool>("ShowModernizations", true);
app.Use(async (context, next) =>
{
    if (!showModernizations &&
        (context.Request.Path.StartsWithSegments("/Modernizations") ||
         context.Request.Path.StartsWithSegments("/attachments-mod")))
    {
        context.Response.StatusCode = 404;
        return;
    }
    await next();
});

// Если NTLM-рукопожатие сломалось — возвращаем честный 401 вместо падения
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (InvalidOperationException ex)
        when (ex.Message.Contains("anonymous request was received"))
    {
        context.Response.Clear();
        context.Response.StatusCode = 401;
        context.Response.Headers.WWWAuthenticate = "Negotiate";
        await context.Response.WriteAsync("Требуется вход в Windows. Обновите страницу (F5).");
    }
});

if (useWinAuth)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapRazorPages();

// Установка текущего пользователя — работает без Razor Pages
app.MapGet("/SetUser", (int userId, string? returnUrl, HttpResponse response) =>
{
    response.Cookies.Append("currentUserId", userId.ToString(), new CookieOptions
    {
        Expires = DateTimeOffset.Now.AddYears(1),
        IsEssential = true,
        Path = "/",
        SameSite = SameSiteMode.Lax
    });

    var isLocal = !string.IsNullOrEmpty(returnUrl)
                  && returnUrl.StartsWith("/")
                  && !returnUrl.StartsWith("//")
                  && !returnUrl.StartsWith("/\\");

    return Results.Redirect(isLocal ? returnUrl! : "/Breakdowns");
});

// Ручной бэкап: создаёт копию и сразу скачивает её
app.MapGet("/Backup", (BackupMaker maker) =>
{
    try
    {
        var path = maker.CreateBackup();
        return Results.File(path, "application/zip", Path.GetFileName(path));
    }
    catch (Exception ex)
    {
        return Results.Text(
            "Ошибка создания резервной копии: " + ex.Message,
            "text/plain; charset=utf-8",
            statusCode: StatusCodes.Status500InternalServerError);
    }
});
// Отдача вложений поломок
app.MapGet("/attachments/{id:int}/{fileName}", (int id, string fileName, AttachmentService attachments) =>
{
    var path = attachments.GetFullPath(id, fileName);
    return path is null
        ? Results.NotFound()
        : Results.File(path, AttachmentService.MimeFor(path));
});

// Отдача вложений модернизаций
app.MapGet("/attachments-mod/{id:int}/{fileName}",
    (int id, string fileName, ModernizationAttachmentService attachments) =>
    {
        var path = attachments.GetFullPath(id, fileName);
        return path is null
            ? Results.NotFound()
            : Results.File(path, AttachmentService.MimeFor(path));
    });

app.Run();