using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 100L * 1024L * 1024L; // 100 MB
});

var app = builder.Build();

var mediaRoot = Environment.GetEnvironmentVariable("MEDIA_ROOT");
string? absoluteMediaRoot = null;
if (string.IsNullOrWhiteSpace(mediaRoot))
{
    app.Logger.LogWarning("MEDIA_ROOT is not configured. Set MEDIA_ROOT to a writable directory.");
}
else
{
    absoluteMediaRoot = Path.IsPathRooted(mediaRoot)
        ? mediaRoot
        : Path.Combine(app.Environment.ContentRootPath, mediaRoot);
    Directory.CreateDirectory(absoluteMediaRoot);
}

var indexStore = new MediaIndexStore(absoluteMediaRoot ?? Path.Combine(app.Environment.ContentRootPath, "media"));

app.MapGet("/media/{id}", (string id, HttpContext ctx) =>
{
    var relativePath = indexStore.ResolveRelativePathFromId(id);
    if (relativePath is null) return Results.NotFound();

    var filePath = Path.Combine(indexStore.MediaRoot, relativePath);
    if (!System.IO.File.Exists(filePath)) return Results.NotFound();

    var fileInfo = new FileInfo(filePath);
    var eTag = $"\"{fileInfo.Length}-{fileInfo.LastWriteTimeUtc.Ticks}\"";
    ctx.Response.Headers.ETag = eTag;
    ctx.Response.Headers.AcceptRanges = "bytes";
    ctx.Response.Headers.LastModified = fileInfo.LastWriteTimeUtc.ToString("R");
    ctx.Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";

    // ETag revalidation (If-None-Match)
    if (ctx.Request.Headers.TryGetValue("If-None-Match", out var inm) && inm.Count > 0)
    {
        if (string.Equals(inm[0], eTag, StringComparison.Ordinal))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }
    }

    var contentType = GetMimeType(fileInfo.Extension);
    return Results.File(filePath, contentType, enableRangeProcessing: true);
});

app.MapPost("/upload", async (HttpRequest req) =>
{
    if (string.IsNullOrWhiteSpace(absoluteMediaRoot))
    {
        return Results.Problem("MEDIA_ROOT not configured");
    }
    var form = await req.ReadFormAsync();
    var file = form.Files.GetFile("file");
    var categoryId = form["categoryId"].FirstOrDefault();

    if (file is null || file.Length == 0) return Results.BadRequest("No file provided");

    // Validate extension
    var id = Guid.NewGuid().ToString("n");
    var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
    var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".ogg", ".aac", ".m4a", ".flac"
    };
    if (!allowed.Contains(ext))
    {
        return Results.BadRequest($"Unsupported file type: {ext}");
    }

    // Validate categoryId: must not be empty, must not be a GUID, and must be a valid folder name
    if (string.IsNullOrWhiteSpace(categoryId))
    {
        return Results.BadRequest("categoryId is required");
    }

    // Reject GUID-like categoryIds
    if (Guid.TryParse(categoryId, out _))
    {
        return Results.BadRequest("categoryId cannot be a GUID. Use readable category identifiers.");
    }

    var safeName = RemoveInvalidFileNameChars(Path.GetFileNameWithoutExtension(file.FileName));
    var safeCategory = RemoveInvalidFileNameChars(categoryId);

    if (string.IsNullOrWhiteSpace(safeCategory))
    {
        return Results.BadRequest("Invalid categoryId");
    }

    var relativePath = Path.Combine(safeCategory, $"{safeName}-{id}{ext}");
    var physicalPath = Path.Combine(indexStore.MediaRoot, relativePath);
    Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);

    await using (var fs = File.Create(physicalPath))
    {
        await file.CopyToAsync(fs);
    }

    var fi = new FileInfo(physicalPath);

    var sound = new SoundDto
    {
        Id = id,
        Name = safeName,
        Description = string.Empty,
        CategoryId = safeCategory,
        FileName = Path.GetFileName(physicalPath),
        FilePath = $"/media/{id}",
        FileSize = fi.Length,
        Duration = 0,
        Format = ext.TrimStart('.'),
        CreatedAt = DateTime.UtcNow
    };

    indexStore.SaveIndex(id, relativePath);

    return Results.Ok(sound);
});

app.MapGet("/media/scan", () =>
{
    if (string.IsNullOrWhiteSpace(absoluteMediaRoot))
    {
        return Results.Problem("MEDIA_ROOT not configured");
    }

    var scannedSounds = new List<SoundDto>();
    var mediaDir = new DirectoryInfo(indexStore.MediaRoot);

    if (!mediaDir.Exists)
    {
        return Results.Ok(scannedSounds);
    }

    // Scan all subdirectories (categories)
    foreach (var categoryDir in mediaDir.GetDirectories())
    {
        // Skip hidden directories and index file
        if (categoryDir.Name.StartsWith("."))
            continue;

        var categoryId = categoryDir.Name;

        foreach (var audioFile in categoryDir.GetFiles("*.mp3")
            .Concat(categoryDir.GetFiles("*.wav"))
            .Concat(categoryDir.GetFiles("*.ogg"))
            .Concat(categoryDir.GetFiles("*.aac"))
            .Concat(categoryDir.GetFiles("*.m4a"))
            .Concat(categoryDir.GetFiles("*.flac")))
        {
            // Try to extract ID from filename pattern: name-{id}.ext
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(audioFile.Name);
            var lastDashIndex = fileNameWithoutExt.LastIndexOf('-');
            
            string id;
            string name;
            
            if (lastDashIndex > 0 && lastDashIndex < fileNameWithoutExt.Length - 1)
            {
                var potentialId = fileNameWithoutExt.Substring(lastDashIndex + 1);
                if (potentialId.Length == 32) // GUID without hyphens
                {
                    id = potentialId;
                    name = fileNameWithoutExt.Substring(0, lastDashIndex);
                }
                else
                {
                    // Generate new ID if pattern doesn't match
                    id = Guid.NewGuid().ToString("n");
                    name = fileNameWithoutExt;
                }
            }
            else
            {
                // Generate new ID
                id = Guid.NewGuid().ToString("n");
                name = fileNameWithoutExt;
            }

            var relativePath = Path.Combine(categoryId, audioFile.Name);
            var ext = audioFile.Extension.TrimStart('.').ToLowerInvariant();

            // Update index if not present
            if (indexStore.ResolveRelativePathFromId(id) == null)
            {
                indexStore.SaveIndex(id, relativePath);
            }

            scannedSounds.Add(new SoundDto
            {
                Id = id,
                Name = name,
                Description = string.Empty,
                CategoryId = categoryId,
                FileName = audioFile.Name,
                FilePath = $"/media/{id}",
                FileSize = audioFile.Length,
                Duration = 0,
                Format = ext,
                CreatedAt = audioFile.CreationTimeUtc
            });
        }
    }

    return Results.Ok(scannedSounds);
});

app.MapPost("/media/migrate", async () =>
{
    if (string.IsNullOrWhiteSpace(absoluteMediaRoot))
    {
        return Results.Problem("MEDIA_ROOT not configured");
    }

    var migrationLog = new List<string>();
    var mediaDir = new DirectoryInfo(indexStore.MediaRoot);

    if (!mediaDir.Exists)
    {
        return Results.Ok(new { message = "No media directory found", log = migrationLog });
    }

    // Load current index
    var allEntries = indexStore.GetAllEntries();

    foreach (var entry in allEntries)
    {
        var id = entry.Key;
        var oldRelativePath = entry.Value;
        var oldPhysicalPath = Path.Combine(indexStore.MediaRoot, oldRelativePath);

        if (!File.Exists(oldPhysicalPath))
        {
            migrationLog.Add($"SKIP: File not found - {oldRelativePath}");
            continue;
        }

        // Check if path contains GUID directory
        var parts = oldRelativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Length < 2)
        {
            migrationLog.Add($"SKIP: Invalid path structure - {oldRelativePath}");
            continue;
        }

        var firstDir = parts[0];
        
        // Check if first directory is a GUID
        if (!Guid.TryParse(firstDir, out _))
        {
            migrationLog.Add($"OK: Already migrated - {oldRelativePath}");
            continue;
        }

        // Need to determine correct category - this requires external data (sounds.json)
        // For now, move to "uncategorized"
        var fileName = Path.GetFileName(oldPhysicalPath);
        var newRelativePath = Path.Combine("uncategorized", fileName);
        var newPhysicalPath = Path.Combine(indexStore.MediaRoot, newRelativePath);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(newPhysicalPath)!);
            File.Move(oldPhysicalPath, newPhysicalPath, overwrite: false);
            indexStore.SaveIndex(id, newRelativePath);
            migrationLog.Add($"MIGRATED: {oldRelativePath} -> {newRelativePath}");
        }
        catch (Exception ex)
        {
            migrationLog.Add($"ERROR: {oldRelativePath} - {ex.Message}");
        }
    }

    // Clean up empty GUID directories
    foreach (var dir in mediaDir.GetDirectories())
    {
        if (Guid.TryParse(dir.Name, out _))
        {
            try
            {
                if (!dir.GetFiles("*", SearchOption.AllDirectories).Any())
                {
                    dir.Delete(recursive: true);
                    migrationLog.Add($"CLEANED: Removed empty directory - {dir.Name}");
                }
            }
            catch (Exception ex)
            {
                migrationLog.Add($"ERROR: Could not delete directory {dir.Name} - {ex.Message}");
            }
        }
    }

    return Results.Ok(new { message = "Migration completed", log = migrationLog });
});

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();

static string GetMimeType(string ext) => ext.ToLowerInvariant() switch
{
    ".mp3" => "audio/mpeg",
    ".wav" => "audio/wav",
    ".ogg" => "audio/ogg",
    ".aac" => "audio/aac",
    ".m4a" => "audio/mp4",
    ".flac" => "audio/flac",
    _ => "application/octet-stream"
};

static string RemoveInvalidFileNameChars(string input)
{
    if (string.IsNullOrEmpty(input)) return string.Empty;
    var invalid = Path.GetInvalidFileNameChars();
    var sb = new StringBuilder(input.Length);
    for (int i = 0; i < input.Length; i++)
    {
        var ch = input[i];
        // Filter out invalid characters and directory separators
        if (Array.IndexOf(invalid, ch) == -1 && ch != '/' && ch != '\\')
        {
            sb.Append(ch);
        }
    }
    var result = sb.ToString();
    return string.IsNullOrWhiteSpace(result) ? "file" : result;
}

public sealed class MediaIndexStore
{
    private readonly string _indexFilePath;
    private readonly object _sync = new();

    public string MediaRoot { get; }

    public MediaIndexStore(string mediaRoot)
    {
        MediaRoot = mediaRoot;
        _indexFilePath = Path.Combine(MediaRoot, ".media-index.json");
        if (!File.Exists(_indexFilePath))
        {
            SaveDictionary(new Dictionary<string, string>());
        }
    }

    public string? ResolveRelativePathFromId(string id)
    {
        var map = LoadDictionary();
        return map.TryGetValue(id, out var rel) ? rel : null;
    }

    public void SaveIndex(string id, string relativePath)
    {
        lock (_sync)
        {
            var map = LoadDictionary();
            map[id] = relativePath;
            SaveDictionary(map);
        }
    }

    public Dictionary<string, string> GetAllEntries()
    {
        return LoadDictionary();
    }

    private Dictionary<string, string> LoadDictionary()
    {
        try
        {
            var json = File.ReadAllText(_indexFilePath);
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return map ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private void SaveDictionary(Dictionary<string, string> map)
    {
        var json = JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_indexFilePath, json);
    }
}

public sealed class SoundDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CategoryId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public double Duration { get; set; }
    public string Format { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}



