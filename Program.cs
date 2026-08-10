using System.Text.Json;
using BreakdownReport.Data;
using BreakdownReport.Models;
using BreakdownReport.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;

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

// Определяем папку данных
var dataFolder = Path.Combine(builder.Environment.ContentRootPath, "Data");
Directory.CreateDirectory(dataFolder);
var dbPath = Path.Combine(dataFolder, "breakdowns.db");
//База данных
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
builder.Services.AddScoped<BreakdownService>();

// ----- Модуль модернизаций: отдельная БД, не смешивается с поломками -----
var modDbPath = Path.Combine(dataFolder, "modernizations.db");
builder.Services.AddDbContext<ModernizationDbContext>(o => o.UseSqlite($"Data Source={modDbPath}"));
builder.Services.AddScoped<ModernizationService>();
builder.Services.AddSingleton(_ => new ModernizationAttachmentService(
    Path.Combine(dataFolder, "attachments-mod")));
builder.Services.AddSingleton(_ => new ModernizationAuditService(
    Path.Combine(dataFolder, "modernizations-audit.json")));
// Сервис excel
builder.Services.AddSingleton<ExcelExportService>();
//Сервис вложений(фото, скрины,файлы)
builder.Services.AddSingleton(_ => new AttachmentService(
    Path.Combine(dataFolder, "attachments")));
//Сервис учета изменений заявок
builder.Services.AddSingleton(_ => new AuditService(
    Path.Combine(dataFolder, "audit.json")));

var app = builder.Build();

//Для отладки
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// Создание структуры БД + одноразовый импорт из старого JSON
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    // таблица по трудозатратам при выполнении модернизаций
    scope.ServiceProvider.GetRequiredService<ModernizationDbContext>()
    .Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "LaborEntries" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_LaborEntries" PRIMARY KEY AUTOINCREMENT,
            "ModernizationId" INTEGER NOT NULL,
            "EmployeeId" INTEGER NOT NULL,
            "Minutes" INTEGER NOT NULL
        );
        """);

    scope.ServiceProvider.GetRequiredService<ModernizationDbContext>()
    .Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "Comments" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_Comments" PRIMARY KEY AUTOINCREMENT,
            "ModernizationId" INTEGER NOT NULL,
            "AuthorName" TEXT NOT NULL,
            "At" TEXT NOT NULL,
            "Text" TEXT NOT NULL
        );
        """);


    scope.ServiceProvider.GetRequiredService<ModernizationDbContext>().Database.EnsureCreated();

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

// Отдача вложений
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