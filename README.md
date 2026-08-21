Проект создан для упрощения сбора данных но простоям оборудования.
Проект в стадии разработки...

## Сначала клонируйте репозиторий:
https://github.com/Bezlevv/BreakdownReport.git

## Запуск в Localhost:

cd путь к папке с клонированным репозиторием\bin\Debug\net10.0
$env:ASPNETCORE_ENVIRONMENT="Development"
$env:ASPNETCORE_URLS="http://localhost:5111"
.\BreakdownReport.exe

## Публикация :
командная строка (Администратор)
dotnet publish BreakdownReport.csproj -c Release -r win-x64 --self-contained true -o C:\Publish\BreakdownReport
где C:\Publish\BreakdownReport путь к папке в которую хотите публиковать

## Настройка:
#Что проверить после публикации
1. В C:\Publish\BreakdownReport должны оказаться:
BreakdownReport.exe;
папка Config с вашими четырьмя JSON-справочниками (Сотрудники, Участки, Оборудование, Типы простоев),
корректиовки вносить под ващи нужды (пока заполнены тестовыми данными)
appsettings.json.

2. Открой appsettings.json и замени содержимое на:

{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Urls": "http://0.0.0.0:5000",
  "UseWindowsAuth": false,
  "ShowModernizations": true,
  "MigrateFromSqlite": false,
  "SqliteDataFolder": "Путь к BD SQLlite",
  "PgDumpPath": "C:\\Program Files\\PostgreSQL\\16\\bin\\pg_dump.exe",
  "ConnectionStrings": {
    "Breakdowns": "Host=localhost;Port=5432;Database=breakdowns;Username=br_app;Password=Добавить пароль",
    "Modernizations": "Host=localhost;Port=5432;Database=modernizations;Username=br_app;Password=Добавить пароль"
  }
}

3. Пересобери Ctrl + Shift + B

4. Разрешение для Брандмауэра. Командная строка (Администратор):

netsh advfirewall firewall add rule name="BreakdownReport" dir=in action=allow protocol=TCP localport=5000

5. Проверка вручную: запустить exe из опубликованной папки двойным кликом, открыть с другого ПК http://ИМЯ-ПК:5000.

6. Это команды регистрации и автозапуска приложения как службы Windows если есть сервер:
sc create BreakdownReport binPath= "C:\Publish\BreakdownReport\BreakdownReport.exe" start= auto
sc start BreakdownReport

## Далее по мере разработки дополню...
