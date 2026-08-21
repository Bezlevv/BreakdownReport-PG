using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BreakdownReport.Data;

namespace BreakdownReport.Services;

/// <summary>
/// Парольная аутентификация — запасной вход, когда Windows-аутентификация недоступна.
/// Пользователи: Data/users.json (соль+хэш, вне git). Кука подписана HMAC.
/// </summary>
public sealed class PasswordAuthService
{
    private sealed record UserRecord(int EmployeeId, string Salt, string Hash);

    private readonly string _usersFile;
    private readonly string _keyFile;
    private readonly DictionaryStore _store;
    private readonly string _defaultPassword;
    private readonly object _lock = new();
    private List<UserRecord> _users = [];

    public PasswordAuthService(string usersFile, DictionaryStore store, string defaultPassword)
    {
        _usersFile = Path.GetFullPath(usersFile);
        _keyFile = Path.Combine(Path.GetDirectoryName(_usersFile)!, "auth.key");
        _store = store;
        _defaultPassword = defaultPassword;
        Load();
    }

    private void Load()
    {
        lock (_lock)
        {
            _users = File.Exists(_usersFile)
                ? JsonSerializer.Deserialize<List<UserRecord>>(File.ReadAllText(_usersFile)) ?? []
                : [];

            // каждому сотруднику без записи — стартовый пароль
            var changed = false;
            foreach (var emp in _store.Employees)
            {
                if (_users.All(u => u.EmployeeId != emp.Id))
                {
                    _users.Add(MakeRecord(emp.Id, _defaultPassword));
                    changed = true;
                }
            }
            if (changed) Save();
        }
    }

    private void Save() =>
        File.WriteAllText(_usersFile,
            JsonSerializer.Serialize(_users, new JsonSerializerOptions { WriteIndented = true }));

    private static UserRecord MakeRecord(int employeeId, string password)
    {
        var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        return new UserRecord(employeeId, salt, Hash(salt, password));
    }

    private static string Hash(string salt, string password) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(salt + password)));

    public bool Verify(int employeeId, string password)
    {
        lock (_lock)
        {
            var u = _users.FirstOrDefault(x => x.EmployeeId == employeeId);
            return u is not null && u.Hash == Hash(u.Salt, password);
        }
    }

    public void ChangePassword(int employeeId, string newPassword)
    {
        lock (_lock)
        {
            _users = _users.Where(x => x.EmployeeId != employeeId).ToList();
            _users.Add(MakeRecord(employeeId, newPassword));
            Save();
        }
    }

    // ----- подписанная кука -----

    private byte[] GetKey()
    {
        if (File.Exists(_keyFile)) return File.ReadAllBytes(_keyFile);
        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(_keyFile, key);
        return key;
    }

    public string CreateToken(int employeeId)
    {
        var payload = employeeId + "." + DateTimeOffset.Now.AddYears(1).ToUnixTimeSeconds();
        return payload + "." + Sign(payload);
    }

    public int? ValidateToken(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var parts = token.Split('.');
        if (parts.Length != 3) return null;
        if (!string.Equals(Sign(parts[0] + "." + parts[1]), parts[2], StringComparison.Ordinal)) return null;
        if (!int.TryParse(parts[0], out var empId)) return null;
        if (!long.TryParse(parts[1], out var exp) || DateTimeOffset.Now.ToUnixTimeSeconds() > exp) return null;
        return empId;
    }

    private string Sign(string payload)
    {
        using var h = new HMACSHA256(GetKey());
        return Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }
}