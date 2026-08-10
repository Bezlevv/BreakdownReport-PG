namespace BreakdownReport.Services;

public class AttachmentService
{
    private readonly string _root;

    public AttachmentService(string rootFolder) => _root = rootFolder;

    public string FolderFor(int breakdownId) => Path.Combine(_root, breakdownId.ToString());

    public IReadOnlyList<string> ListFiles(int breakdownId)
    {
        var dir = FolderFor(breakdownId);
        if (!Directory.Exists(dir)) return [];
        return Directory.GetFiles(dir)
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .OrderBy(n => n)
            .ToList()!;
    }

    public async Task SaveAsync(int breakdownId, IFormFile file)
    {
        var dir = FolderFor(breakdownId);
        Directory.CreateDirectory(dir);

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(ext)) ext = ".bin";
        var name = $"{Guid.NewGuid()}{ext.ToLowerInvariant()}";

        await using var stream = File.Create(Path.Combine(dir, name));
        await file.CopyToAsync(stream);
    }

    public string? GetFullPath(int breakdownId, string fileName)
    {
        var safe = Path.GetFileName(fileName); // защита от ../
        var path = Path.Combine(FolderFor(breakdownId), safe);
        return File.Exists(path) ? path : null;
    }

    public bool Delete(int breakdownId, string fileName)
    {
        var path = GetFullPath(breakdownId, fileName);
        if (path is null) return false;
        File.Delete(path);
        return true;
    }

    public void DeleteAll(int breakdownId)
    {
        var dir = FolderFor(breakdownId);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    public static string MimeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".webp" => "image/webp",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream"
    };
}