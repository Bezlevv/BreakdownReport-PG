using System.Text.Json;
using Serilog;
using BreakdownReport.Data;
using BreakdownReport.Models;
using BreakdownReport.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
var builder = WebApplication.CreateBuilder(args);

// Папка данных: вложения, журналы, бэкапы (PostgreSQL)
var dataFolder = Path.Combine(builder.Environment.ContentRootPath, "Data");
Directory.CreateDirectory(dataFolder);

//Логирование в файл дял сервера
builder.Host.UseSerilog((ctx, cfg) => cfg
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(dataFolder,"logs", "app-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 31));
builder.Services.AddRazorPages();


//Проверка авторизации UseWindowsAuth если отключено пользователь выбирается из списка ( надо будет доработаьт...)
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

// Парольная аутентификация — запасной вход, когда Windows недоступна
builder.Services.AddSingleton(sp => new PasswordAuthService(
    Path.Combine(dataFolder, "users.json"),
    sp.GetRequiredService<DictionaryStore>(),
    builder.Configuration.GetValue<string>("DefaultPassword") ?? "romanov2026"));

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

// Парольная сессия: валидная кука br_session = пользователь аутентифицирован
app.Use(async (context, next) =>
{
    var empId = context.RequestServices.GetRequiredService<PasswordAuthService>()
        .ValidateToken(context.Request.Cookies["br_session"]);
    if (empId is not null)
    {
        var emp = context.RequestServices.GetRequiredService<DictionaryStore>()
            .Employees.FirstOrDefault(e => e.Id == empId);
        if (emp is not null)
        {
            context.User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(new[]
                {
                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, emp.FullName),
                    new System.Security.Claims.Claim("EmployeeId", emp.Id.ToString())
                }, "Password"));
        }
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
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(
            "<html><body style='font-family:Segoe UI,sans-serif;padding:40px'>" +
            "<h3>Требуется вход</h3>" +
            "<p><a href='/Login'>Войти по сотруднику и паролю</a> &nbsp;|&nbsp; " +
            "<a href='/'>Повторить с Windows-логином (F5)</a></p>" +
            "</body></html>");
    }
});

if (useWinAuth)
{
    app.UseAuthentication();
    app.UseAuthorization();
    app.Use(async (context, next) =>
    {
        if (context.User.Identity?.IsAuthenticated == true &&
            context.User.Identity.AuthenticationType == "Negotiate" &&
            string.IsNullOrEmpty(context.Request.Cookies["currentUserId"]))
        {
            var login = context.User.Identity.Name?.Split('\\').Last();
            var emp = context.RequestServices.GetRequiredService<DictionaryStore>()
                .Employees.FirstOrDefault(e =>
                    !string.IsNullOrEmpty(e.Login) &&
                    string.Equals(e.Login, login, StringComparison.OrdinalIgnoreCase));
            if (emp is not null)
                context.Response.Cookies.Append("currentUserId", emp.Id.ToString(),
                    new CookieOptions { Expires = DateTimeOffset.Now.AddYears(1), IsEssential = true, Path = "/", SameSite = SameSiteMode.Lax });
        }
        await next();
    });
}

app.MapRazorPages();

// Установка текущего пользователя — работает без Razor Pages
app.MapGet("/SetUser", (int userId, string? returnUrl, HttpContext context, HttpResponse response) =>
{
    // Парольная сессия не даёт выбирать чужое имя
    var sessionEmp = context.User.FindFirst("EmployeeId");
    if (sessionEmp is not null && int.TryParse(sessionEmp.Value, out var sid) && sid != userId)
        userId = sid;

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

// Выход из парольной сессии
app.MapGet("/Logout", (HttpResponse response) =>
{
    response.Cookies.Delete("br_session");
    response.Cookies.Delete("currentUserId");
    return Results.Redirect("/Login");
}).AllowAnonymous();

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
