using BreakdownReport.Models;
using ClosedXML.Excel;

namespace BreakdownReport.Services;

public sealed class ReportSnapshot
{
    public int TotalCount { get; set; }
    public int ThirdPartyCount { get; set; }
    public int TotalLineDowntime { get; set; }
    public int TotalEquipmentDowntime { get; set; }
    public int TotalLabor { get; set; }

    public List<RepeatRow> Repeats { get; set; } = [];
    public List<AreaRow> Areas { get; set; } = [];
    public List<EquipmentRow> Equipment { get; set; } = [];
    public List<EmployeeRow> Employees { get; set; } = [];

    public sealed record RepeatRow(string Description, int Count, int LineDowntime, DateTime Last);
    public sealed record AreaRow(string AreaName, int Count, int LineDowntime, int EquipmentDowntime);
    public sealed record EquipmentRow(string AreaName, string EquipmentName, int Count, int LineDowntime, int EquipmentDowntime);
    public sealed record EmployeeRow(string Name, int Participations, int Minutes);
}

public sealed class ExcelExportService
{
    private readonly DictionaryStore _store;

    public ExcelExportService(DictionaryStore store) => _store = store;

    // ---------- список поломок (формат приложения) ----------
    public byte[] ExportBreakdowns(IReadOnlyList<Breakdown> items)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Поломки");

        string[] headers =
        [
            "№", "Дата и время", "Автор", "Участок", "Оборудование", "Короткое описание",
            "Подробное описание", "Тип", "Простой линии, мин", "Простой оборудования, мин",
            "Трудозатраты, мин", "Ремонтная бригада", "Вина третьих лиц"
        ];
        for (var i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];

        var row = 2;
        foreach (var b in items.OrderByDescending(b => b.OccurredAt))
        {
            var eq = _store.Equipment.FirstOrDefault(e => e.Id == b.EquipmentId);
            var area = eq is not null ? _store.FindArea(eq.AreaId) : null;
            var author = _store.Employees.FirstOrDefault(e => e.Id == b.AuthorId);
            var type = _store.FailureTypes.FirstOrDefault(t => t.Id == b.FailureTypeId);

            ws.Cell(row, 1).Value = b.Id;
            ws.Cell(row, 2).Value = b.OccurredAt;
            ws.Cell(row, 2).Style.NumberFormat.Format = "dd.mm.yyyy hh:mm";
            ws.Cell(row, 3).Value = author?.FullName ?? "";
            ws.Cell(row, 4).Value = area?.Name ?? "";
            ws.Cell(row, 5).Value = eq?.Name ?? "";
            ws.Cell(row, 6).Value = b.ShortDescription;
            ws.Cell(row, 7).Value = b.DetailedDescription;
            ws.Cell(row, 8).Value = type?.Name ?? "";
            ws.Cell(row, 9).Value = b.LineDowntimeMinutes;
            ws.Cell(row, 10).Value = b.EquipmentDowntimeMinutes;
            ws.Cell(row, 11).Value = b.TotalLaborMinutes;
            ws.Cell(row, 12).Value = string.Join(", ", b.LaborEntries
                .Select(l => _store.Employees.FirstOrDefault(e => e.Id == l.EmployeeId)?.LastName ?? "?"));
            ws.Cell(row, 13).Value = b.ThirdPartyFault ? "ДА" : "нет";
            row++;
        }

        ws.Row(1).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ---------- сводные отчёты + лист «как в исходной таблице» ----------
    public byte[] ExportReports(ReportSnapshot s, IReadOnlyList<Breakdown> items)
    {
        using var wb = new XLWorkbook();

        AddOriginalStyleSheet(wb, items);

        var ws = wb.Worksheets.Add("Сводка");
        ws.Cell(1, 1).Value = "Поломок всего"; ws.Cell(1, 2).Value = s.TotalCount;
        ws.Cell(2, 1).Value = "Из них вина третьих лиц"; ws.Cell(2, 2).Value = s.ThirdPartyCount;
        ws.Cell(3, 1).Value = "Простой линии, мин"; ws.Cell(3, 2).Value = s.TotalLineDowntime;
        ws.Cell(4, 1).Value = "Простой оборудования, мин"; ws.Cell(4, 2).Value = s.TotalEquipmentDowntime;
        ws.Cell(5, 1).Value = "Трудозатраты, мин"; ws.Cell(5, 2).Value = s.TotalLabor;
        ws.Column(1).AdjustToContents();

        FillTable(wb, "Повторяющиеся",
            ["Описание", "Кол-во", "Простой линии, мин", "Последний случай"],
            s.Repeats.Select(r => new object[] { r.Description, r.Count, r.LineDowntime, r.Last.ToString("dd.MM.yyyy HH:mm") }));

        FillTable(wb, "По участкам",
            ["Участок", "Поломок", "Простой линии, мин", "Простой оборудования, мин"],
            s.Areas.Select(r => new object[] { r.AreaName, r.Count, r.LineDowntime, r.EquipmentDowntime }));

        FillTable(wb, "По оборудованию",
            ["Участок", "Оборудование", "Поломок", "Простой линии, мин", "Простой оборудования, мин"],
            s.Equipment.Select(r => new object[] { r.AreaName, r.EquipmentName, r.Count, r.LineDowntime, r.EquipmentDowntime }));

        FillTable(wb, "По сотрудникам",
            ["Сотрудник", "Участий", "Минут"],
            s.Employees.Select(r => new object[] { r.Name, r.Participations, r.Minutes }));

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ---------- список заявок на модернизацию ----------
    public byte[] ExportModernizations(IReadOnlyList<Modernization> items)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Модернизации");
        string[] headers =
        [
            "№", "Статус", "Участок", "Оборудование", "Заказчик",
        "Краткое описание", "Полное описание", "Необходимая дата",
        "Инженер, согласовавший заявку", "Назначенные сотрудники",
        "Материалы", "Оформил", "Дата оформления","Трудозатраты, мин",
        "Трудозатраты по людям"
        ];
        for (var i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];

        var row = 2;
        foreach (var m in items.OrderBy(m => m.RequiredDate))
        {
            var eq = _store.Equipment.FirstOrDefault(e => e.Id == m.EquipmentId);
            var area = eq is not null ? _store.FindArea(eq.AreaId) : null;
            var approver = _store.Employees.FirstOrDefault(e => e.Id == m.ApproverId);
            var author = _store.Employees.FirstOrDefault(e => e.Id == m.AuthorId);

            ws.Cell(row, 1).Value = m.Id;
            ws.Cell(row, 2).Value = m.Status;
            ws.Cell(row, 3).Value = area?.Name ?? "";
            ws.Cell(row, 4).Value = eq?.Name ?? "";
            ws.Cell(row, 5).Value = m.Customer;
            ws.Cell(row, 6).Value = m.ShortDescription;
            ws.Cell(row, 7).Value = m.DetailedDescription;
            ws.Cell(row, 8).Value = m.RequiredDate;
            ws.Cell(row, 8).Style.NumberFormat.Format = "dd.mm.yyyy";
            ws.Cell(row, 9).Value = approver?.FullName ?? "";
            ws.Cell(row, 10).Value = string.Join(", ", m.Assignees
                .Select(a => _store.Employees.FirstOrDefault(e => e.Id == a.EmployeeId)?.FullName ?? "?"));
            ws.Cell(row, 11).Value = string.Join("; ", m.Materials
                .Select(x => string.IsNullOrWhiteSpace(x.Qty) ? x.Name : $"{x.Name} — {x.Qty}"));
            ws.Cell(row, 12).Value = author?.FullName ?? "";
            ws.Cell(row, 13).Value = m.CreatedAt;
            ws.Cell(row, 13).Style.NumberFormat.Format = "dd.mm.yyyy hh:mm";
            ws.Cell(row, 14).Value = m.LaborEntries.Sum(l => l.Minutes);
            ws.Cell(row, 15).Value = string.Join("; ", m.LaborEntries
                .Select(l => $"{_store.Employees.FirstOrDefault(e => e.Id == l.EmployeeId)?.LastName ?? "?"} — {l.Minutes}"));
            row++;
        }

        ws.Row(1).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();
        ws.Column(6).Width = 40;
        ws.Column(7).Width = 60;
        ws.Column(11).Width = 40;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ---------- сводный отчёт по модернизациям ----------
    public byte[] ExportModernizationReports(IReadOnlyList<Modernization> items)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Сводка");
        ws.Cell(1, 1).Value = "Заявок всего"; ws.Cell(1, 2).Value = items.Count;
        ws.Cell(2, 1).Value = "Ожидает утверждения"; ws.Cell(2, 2).Value = items.Count(m => m.Status == "Ожидает утверждения");
        ws.Cell(3, 1).Value = "Новая"; ws.Cell(3, 2).Value = items.Count(m => m.Status == "Новая");
        ws.Cell(4, 1).Value = "В процессе"; ws.Cell(4, 2).Value = items.Count(m => m.Status == "В процессе");
        ws.Cell(5, 1).Value = "Завершена"; ws.Cell(5, 2).Value = items.Count(m => m.Status == "Завершена");
        ws.Cell(6, 1).Value = "Просроченные"; ws.Cell(6, 2).Value = items.Count(m => m.Status != "Завершена" && m.RequiredDate.Date < DateTime.Today);
        ws.Cell(7, 1).Value = "Трудозатраты, мин"; ws.Cell(7, 2).Value = items.Sum(m => m.TotalLaborMinutes);
        ws.Column(1).AdjustToContents();

        var byArea = items
            .GroupBy(m => _store.Equipment.FirstOrDefault(e => e.Id == m.EquipmentId)?.AreaId ?? 0)
            .Select(g => new object[]
            {
            _store.FindArea(g.Key)?.Name ?? "—",
            g.Count(),
            g.Sum(m => m.TotalLaborMinutes)
            })
            .OrderByDescending(r => (int)r[1]).ToList();
        FillTable(wb, "По участкам", ["Участок", "Заявок", "Трудозатраты, мин"], byArea);

        var byEq = items
            .GroupBy(m => m.EquipmentId)
            .Select(g =>
            {
                var eq = _store.Equipment.FirstOrDefault(e => e.Id == g.Key);
                return new object[]
                {
                eq is null ? "—" : _store.FindArea(eq.AreaId)?.Name ?? "—",
                eq?.Name ?? "—",
                g.Count(),
                g.Sum(m => m.TotalLaborMinutes)
                };
            })
            .OrderByDescending(r => (int)r[2]).ToList();
        FillTable(wb, "По оборудованию", ["Участок", "Оборудование", "Заявок", "Трудозатраты, мин"], byEq);

        var byEmp = items
            .SelectMany(m => m.LaborEntries)
            .GroupBy(l => l.EmployeeId)
            .Select(g => new object[]
            {
            _store.Employees.FirstOrDefault(e => e.Id == g.Key)?.FullName ?? "—",
            g.Count(),
            g.Sum(l => l.Minutes)
            })
            .OrderByDescending(r => (int)r[2]).ToList();
        FillTable(wb, "По сотрудникам", ["Сотрудник", "Участий", "Минут"], byEmp);

        var byStatus = items
            .GroupBy(m => m.Status)
            .Select(g => new object[] { g.Key, g.Count() })
            .OrderByDescending(r => (int)r[1]).ToList();
        FillTable(wb, "По статусам", ["Статус", "Заявок"], byStatus);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ---------- лист в формате исходной таблицы ----------
    private void AddOriginalStyleSheet(XLWorkbook wb, IReadOnlyList<Breakdown> items)
    {
        var ws = wb.Worksheets.Add("Сводная таблица поломок");

        ws.Cell(1, 1).Value = "Сводная таблица поломок";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Range("A1:P1").Merge();

        string[] headers =
        [
            "№", "№", "ФИО автора", "Короткое описание поломки", "Подробное описание поломки",
            "Номер оборудования", "Номер ОС", "Участок", "Оборудование", "Тип поломки",
            "Время простоя линии, мин", "Время простоя оборудования, мин",
            "Дата и время возникновения поломки", "Трудозатраты на ремонт, мин",
            "Вина третьих лиц (производство)", "Состав ремонтной бригады"
        ];
        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(2, i + 1).Value = headers[i];
            ws.Cell(2, i + 1).Style.Font.Bold = true;
            ws.Cell(2, i + 1).Style.Alignment.WrapText = true;
        }

        var row = 3;
        var n = 1;
        foreach (var b in items.OrderBy(b => b.OccurredAt))
        {
            var eq = _store.Equipment.FirstOrDefault(e => e.Id == b.EquipmentId);
            var area = eq is not null ? _store.FindArea(eq.AreaId) : null;
            var author = _store.Employees.FirstOrDefault(e => e.Id == b.AuthorId);
            var type = _store.FailureTypes.FirstOrDefault(t => t.Id == b.FailureTypeId);

            ws.Cell(row, 1).Value = n++;
            ws.Cell(row, 2).Value = b.Id;
            ws.Cell(row, 3).Value = author?.FullName ?? "";
            ws.Cell(row, 4).Value = b.ShortDescription;
            ws.Cell(row, 5).Value = b.DetailedDescription;
            ws.Cell(row, 6).Value = eq?.InventoryNumber ?? "";
            ws.Cell(row, 7).Value = eq?.OsNumber ?? "";
            ws.Cell(row, 8).Value = area?.Name ?? "";
            ws.Cell(row, 9).Value = eq?.Name ?? "";
            ws.Cell(row, 10).Value = type?.Code ?? "";
            ws.Cell(row, 11).Value = b.LineDowntimeMinutes;
            ws.Cell(row, 12).Value = b.EquipmentDowntimeMinutes;
            ws.Cell(row, 13).Value = b.OccurredAt;
            ws.Cell(row, 13).Style.NumberFormat.Format = "dd.mm.yyyy h:mm";
            ws.Cell(row, 14).Value = FormatLabor(b);
            ws.Cell(row, 15).Value = b.ThirdPartyFault ? "ДА" : "нет";

            var crewCell = ws.Cell(row, 16);
            crewCell.Value = string.Join("\n", b.LaborEntries
                .Select(l => _store.Employees.FirstOrDefault(e => e.Id == l.EmployeeId)?.FullName ?? "?"));
            crewCell.Style.Alignment.WrapText = true;

            row++;
        }

        ws.Columns().AdjustToContents();
        ws.Column(4).Width = 40;
        ws.Column(5).Width = 60;
        ws.Column(16).Width = 22;
    }

    // «3 * 180», если у всех одинаковые минуты; иначе суммарно
    private static string FormatLabor(Breakdown b)
    {
        if (b.LaborEntries.Count == 0) return "";
        var distinct = b.LaborEntries.Select(l => l.Minutes).Distinct().ToList();
        if (b.LaborEntries.Count > 1 && distinct.Count == 1)
            return $"{b.LaborEntries.Count} * {distinct[0]}";
        if (b.LaborEntries.Count == 1)
            return distinct[0].ToString();
        return b.TotalLaborMinutes.ToString();
    }

    private static void FillTable(XLWorkbook wb, string sheetName, string[] headers, IEnumerable<object[]> rows)
    {
        var ws = wb.Worksheets.Add(sheetName);
        for (var i = 0; i < headers.Length; i++)
            ws.Cell(1, i + 1).Value = headers[i];

        var row = 2;
        foreach (var r in rows)
        {
            for (var i = 0; i < r.Length; i++)
            {
                switch (r[i])
                {
                    case int num: ws.Cell(row, i + 1).Value = num; break;
                    case double d: ws.Cell(row, i + 1).Value = d; break;
                    default: ws.Cell(row, i + 1).Value = r[i]?.ToString() ?? ""; break;
                }
            }
            row++;
        }
        ws.Row(1).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();
    }
}