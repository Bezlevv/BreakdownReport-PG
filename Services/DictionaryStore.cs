using BreakdownReport.Models;
using System.Text.Json;


namespace BreakdownReport.Services;

public sealed class DictionaryStore
{
    public IReadOnlyList<Area> Areas { get; }
    public IReadOnlyList<Equipment> Equipment { get; }
    public IReadOnlyList<Employee> Employees { get; }
    public IReadOnlyList<FailureType> FailureTypes { get; }

    private DictionaryStore(
        IReadOnlyList<Area> areas,
        IReadOnlyList<Equipment> equipment,
        IReadOnlyList<Employee> employees,
        IReadOnlyList<FailureType> failureTypes)
    {
        Areas = areas;
        Equipment = equipment;
        Employees = employees;
        FailureTypes = failureTypes;
    }

    public Area? FindArea(int id) => Areas.FirstOrDefault(a => a.Id == id);
    public IEnumerable<Equipment> EquipmentByArea(int areaId) => Equipment.Where(e => e.AreaId == areaId);
    public IEnumerable<Employee> Authors => Employees.Where(e => e.CanBeAuthor);

    public static DictionaryStore LoadFromFolder(string folder)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        var areas = Read<Area>(options, folder, "areas.json");
        var equipment = Read<Equipment>(options, folder, "equipment.json");
        var employees = Read<Employee>(options, folder, "employees.json");
        var failureTypes = Read<FailureType>(options, folder, "failureTypes.json");

        Validate(areas, equipment, employees, failureTypes);

        return new DictionaryStore(areas, equipment, employees, failureTypes);
    }

    private static List<T> Read<T>(JsonSerializerOptions options, string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Не найден файл справочника: {path}", path);

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<List<T>>(stream, options)
               ?? throw new InvalidDataException($"Файл {fileName} пуст или некорректен");
    }

    private static void Validate(
        List<Area> areas,
        List<Equipment> equipment,
        List<Employee> employees,
        List<FailureType> failureTypes)
    {
        var errors = new List<string>();

        CheckUniqueIds("areas.json", areas.Select(a => a.Id), errors);
        CheckUniqueIds("equipment.json", equipment.Select(e => e.Id), errors);
        CheckUniqueIds("employees.json", employees.Select(e => e.Id), errors);
        CheckUniqueIds("failureTypes.json", failureTypes.Select(f => f.Id), errors);

        var areaIds = areas.Select(a => a.Id).ToHashSet();
        foreach (var e in equipment.Where(e => !areaIds.Contains(e.AreaId)))
            errors.Add($"equipment.json: оборудование '{e.Name}' (id={e.Id}) ссылается на несуществующий участок areaId={e.AreaId}");

        var dupNames = employees.GroupBy(e => e.LastName.Trim(), StringComparer.OrdinalIgnoreCase)
                                .Where(g => g.Count() > 1);
        foreach (var g in dupNames)
            errors.Add($"employees.json: несколько записей с фамилией '{g.Key}' — объедините их через aliases");

        if (errors.Count > 0)
            throw new InvalidOperationException(
                "Ошибки в справочниках:\n" + string.Join("\n", errors));
    }

    private static void CheckUniqueIds(string file, IEnumerable<int> ids, List<string> errors)
    {
        var dup = ids.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key);
        foreach (var id in dup)
            errors.Add($"{file}: дублируется id={id}");
    }
    public Employee? FindByLogin(string login) =>
    Employees.FirstOrDefault(e =>
        string.Equals(e.Login, login, StringComparison.OrdinalIgnoreCase));
}