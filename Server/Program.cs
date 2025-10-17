using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors();
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 100L * 1024L * 1024L; // 100 MB
});

var app = builder.Build();

// Enable CORS for the Blazor client
app.UseCors(policy =>
    policy.AllowAnyOrigin()
          .AllowAnyMethod()
          .AllowAnyHeader());

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
    var id = indexStore.GenerateNextId();
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

    var relativePath = Path.Combine(safeCategory, $"{safeName}{ext}");
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
        FilePath = $"/media/{relativePath.Replace('\\', '/')}",
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
            // Use incremental ID for new files
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(audioFile.Name);
            string id;
            string name;

            // Check if file already has an ID in the index
            var existingRelativePath = Path.Combine(categoryId, audioFile.Name);
            var existingId = indexStore.GetAllEntries().FirstOrDefault(kvp => kvp.Value == existingRelativePath).Key;

            if (!string.IsNullOrEmpty(existingId))
            {
                id = existingId;
                name = fileNameWithoutExt;
            }
            else
            {
                // Generate new incremental ID for new files
                id = indexStore.GenerateNextId();
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
                FilePath = $"/media/{relativePath.Replace('\\', '/')}",
                FileSize = audioFile.Length,
                Duration = 0,
                Format = ext,
                CreatedAt = audioFile.CreationTimeUtc
            });
        }
    }

    return Results.Ok(scannedSounds);
});

app.MapDelete("/media/sounds/{id}", (string id) =>
{
    if (string.IsNullOrWhiteSpace(absoluteMediaRoot))
    {
        return Results.Problem("MEDIA_ROOT not configured");
    }

    // Get relative path from index
    var relativePath = indexStore.ResolveRelativePathFromId(id);
    if (relativePath == null)
    {
        return Results.NotFound($"Sound with ID {id} not found in index");
    }

    // Delete physical file
    var physicalPath = Path.Combine(indexStore.MediaRoot, relativePath);
    try
    {
        if (File.Exists(physicalPath))
        {
            File.Delete(physicalPath);
        }
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to delete file: {ex.Message}");
    }

    // Remove from index
    indexStore.RemoveIndex(id);

    return Results.Ok(new { message = $"Sound {id} deleted successfully" });
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

app.MapPost("/media/create-category", async (HttpRequest req) =>
{
    if (string.IsNullOrWhiteSpace(absoluteMediaRoot))
    {
        return Results.Problem("MEDIA_ROOT not configured");
    }

    var categoryRequest = await req.ReadFromJsonAsync<CreateCategoryRequest>();
    if (categoryRequest == null || string.IsNullOrWhiteSpace(categoryRequest.CategoryId))
    {
        return Results.BadRequest("Invalid category request");
    }

    // Validate categoryId: must not be empty, must not be a GUID, and must be a valid folder name
    if (string.IsNullOrWhiteSpace(categoryRequest.CategoryId))
    {
        return Results.BadRequest("categoryId is required");
    }

    // Reject GUID-like categoryIds
    if (Guid.TryParse(categoryRequest.CategoryId, out _))
    {
        return Results.BadRequest("categoryId cannot be a GUID. Use readable category identifiers.");
    }

    var safeCategory = RemoveInvalidFileNameChars(categoryRequest.CategoryId);
    if (string.IsNullOrWhiteSpace(safeCategory))
    {
        return Results.BadRequest("Invalid categoryId");
    }

    // Create the category directory
    var categoryPath = Path.Combine(indexStore.MediaRoot, safeCategory);
    try
    {
        Directory.CreateDirectory(categoryPath);
        return Results.Ok(new { message = $"Category '{safeCategory}' created successfully", categoryId = safeCategory });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to create category directory: {ex.Message}");
    }
});

app.MapGet("/media/categories", () =>
{
    if (string.IsNullOrWhiteSpace(absoluteMediaRoot))
    {
        return Results.Problem("MEDIA_ROOT not configured");
    }

    var mediaDir = new DirectoryInfo(indexStore.MediaRoot);
    if (!mediaDir.Exists)
    {
        return Results.Ok(new List<CategoryInfo>());
    }

    var categories = new List<CategoryInfo>();

    foreach (var categoryDir in mediaDir.GetDirectories())
    {
        // Skip hidden directories and index file
        if (categoryDir.Name.StartsWith("."))
            continue;

        var categoryId = categoryDir.Name;

        // Count audio files in this category
        var audioFiles = categoryDir.GetFiles("*.mp3")
            .Concat(categoryDir.GetFiles("*.wav"))
            .Concat(categoryDir.GetFiles("*.ogg"))
            .Concat(categoryDir.GetFiles("*.aac"))
            .Concat(categoryDir.GetFiles("*.m4a"))
            .Concat(categoryDir.GetFiles("*.flac"))
            .ToList();

        categories.Add(new CategoryInfo
        {
            Id = categoryId,
            Name = FormatCategoryNameFromId(categoryId),
            SoundCount = audioFiles.Count
        });
    }

    return Results.Ok(categories);
});

app.MapDelete("/media/categories/{categoryId}", (string categoryId) =>
{
    if (string.IsNullOrWhiteSpace(absoluteMediaRoot))
    {
        return Results.Problem("MEDIA_ROOT not configured");
    }

    // Validate categoryId: must not be empty, must not be a GUID
    if (string.IsNullOrWhiteSpace(categoryId))
    {
        return Results.BadRequest("categoryId is required");
    }

    // Reject GUID-like categoryIds
    if (Guid.TryParse(categoryId, out _))
    {
        return Results.BadRequest("categoryId cannot be a GUID. Use readable category identifiers.");
    }

    var safeCategory = RemoveInvalidFileNameChars(categoryId);
    if (string.IsNullOrWhiteSpace(safeCategory))
    {
        return Results.BadRequest("Invalid categoryId");
    }

    // Check if category directory exists
    var categoryPath = Path.Combine(indexStore.MediaRoot, safeCategory);
    if (!Directory.Exists(categoryPath))
    {
        return Results.NotFound($"Category '{safeCategory}' not found");
    }

    // Check if directory has audio files
    var categoryDir = new DirectoryInfo(categoryPath);
    var audioFiles = categoryDir.GetFiles("*.mp3")
        .Concat(categoryDir.GetFiles("*.wav"))
        .Concat(categoryDir.GetFiles("*.ogg"))
        .Concat(categoryDir.GetFiles("*.aac"))
        .Concat(categoryDir.GetFiles("*.m4a"))
        .Concat(categoryDir.GetFiles("*.flac"))
        .ToList();

    if (audioFiles.Count > 0)
    {
        return Results.BadRequest($"Cannot delete category '{safeCategory}' - it contains {audioFiles.Count} audio files");
    }

    try
    {
        // Delete the category directory (should be empty)
        Directory.Delete(categoryPath, false); // false = don't recurse, should be empty
        return Results.Ok(new { message = $"Category '{safeCategory}' deleted successfully" });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to delete category directory: {ex.Message}");
    }
});

app.MapPost("/media/move-files", async (HttpRequest req) =>
{
    if (string.IsNullOrWhiteSpace(absoluteMediaRoot))
    {
        return Results.Problem("MEDIA_ROOT not configured");
    }

    var moveRequests = await req.ReadFromJsonAsync<List<MoveFileRequest>>();
    if (moveRequests == null || moveRequests.Count == 0)
    {
        return Results.BadRequest("No move requests provided");
    }

    var results = new List<MoveFileResult>();
    var mediaDir = new DirectoryInfo(indexStore.MediaRoot);

    foreach (var request in moveRequests)
    {
        var result = new MoveFileResult { SoundId = request.SoundId };

        try
        {
            // Get current path from index
            var currentRelativePath = indexStore.ResolveRelativePathFromId(request.SoundId);
            if (currentRelativePath == null)
            {
                result.Success = false;
                result.Message = $"Sound {request.SoundId} not found in index";
                results.Add(result);
                continue;
            }

            var currentPhysicalPath = Path.Combine(indexStore.MediaRoot, currentRelativePath);
            if (!File.Exists(currentPhysicalPath))
            {
                result.Success = false;
                result.Message = $"File not found: {currentRelativePath}";
                results.Add(result);
                continue;
            }

            // Validate target category
            if (string.IsNullOrWhiteSpace(request.TargetCategory))
            {
                result.Success = false;
                result.Message = "Target category cannot be empty";
                results.Add(result);
                continue;
            }

            // Reject GUID-like target categories
            if (Guid.TryParse(request.TargetCategory, out _))
            {
                result.Success = false;
                result.Message = "Target category cannot be a GUID. Use readable category identifiers.";
                results.Add(result);
                continue;
            }

            // Sanitize target category name
            var safeTargetCategory = RemoveInvalidFileNameChars(request.TargetCategory);
            if (string.IsNullOrWhiteSpace(safeTargetCategory))
            {
                result.Success = false;
                result.Message = "Invalid target category name";
                results.Add(result);
                continue;
            }

            // Get filename from current path
            var fileName = Path.GetFileName(currentRelativePath);
            var newRelativePath = Path.Combine(safeTargetCategory, fileName);
            var newPhysicalPath = Path.Combine(indexStore.MediaRoot, newRelativePath);

            // Check if target is different from current
            if (string.Equals(currentRelativePath, newRelativePath, StringComparison.OrdinalIgnoreCase))
            {
                result.Success = true;
                result.Message = "File already in target category";
                results.Add(result);
                continue;
            }

            // Create target directory if it doesn't exist
            var targetDir = Path.GetDirectoryName(newPhysicalPath)!;
            Directory.CreateDirectory(targetDir);

            // Move the file
            File.Move(currentPhysicalPath, newPhysicalPath, overwrite: false);

            // Update index
            indexStore.SaveIndex(request.SoundId, newRelativePath);

            result.Success = true;
            result.Message = $"Moved from {currentRelativePath} to {newRelativePath}";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Error: {ex.Message}";
        }

        results.Add(result);
    }

    // Clean up empty directories
    try
    {
        foreach (var dir in mediaDir.GetDirectories("*", SearchOption.AllDirectories))
        {
            if (!dir.GetFiles("*", SearchOption.AllDirectories).Any() &&
                !dir.GetDirectories("*", SearchOption.AllDirectories).Any())
            {
                try
                {
                    dir.Delete();
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }
    catch
    {
        // Ignore cleanup errors
    }

    var successCount = results.Count(r => r.Success);
    var totalCount = results.Count;

    return Results.Ok(new
    {
        message = $"Processed {totalCount} files, {successCount} successful",
        results = results
    });
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

static string FormatCategoryNameFromId(string categoryId)
{
    if (string.IsNullOrWhiteSpace(categoryId))
        return "Sem Categoria";

    // Replace hyphens and underscores with spaces
    var name = categoryId.Replace('-', ' ').Replace('_', ' ');

    // Title case
    var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    for (int i = 0; i < words.Length; i++)
    {
        if (words[i].Length > 0)
        {
            words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1).ToLower();
        }
    }

    return string.Join(' ', words);
}

public sealed class MediaIndexStore
{
    private readonly string _indexFilePath;
    private readonly string _idCounterFilePath;
    private readonly object _sync = new();
    private int _nextId;

    public string MediaRoot { get; }

    public MediaIndexStore(string mediaRoot)
    {
        MediaRoot = mediaRoot;
        _indexFilePath = Path.Combine(MediaRoot, ".media-index.json");
        _idCounterFilePath = Path.Combine(MediaRoot, ".id-counter.txt");

        if (!File.Exists(_indexFilePath))
        {
            SaveDictionary(new Dictionary<string, string>());
        }

        // Initialize ID counter
        LoadIdCounter();
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

    public void RemoveIndex(string id)
    {
        lock (_sync)
        {
            var map = LoadDictionary();
            map.Remove(id);
            SaveDictionary(map);
        }
    }

    public Dictionary<string, string> GetAllEntries()
    {
        return LoadDictionary();
    }

    public string GenerateNextId()
    {
        lock (_sync)
        {
            var id = _nextId.ToString();
            _nextId++;
            SaveIdCounter();
            return id;
        }
    }

    private void LoadIdCounter()
    {
        try
        {
            if (File.Exists(_idCounterFilePath))
            {
                var content = File.ReadAllText(_idCounterFilePath);
                if (int.TryParse(content, out var counter))
                {
                    _nextId = counter;
                }
                else
                {
                    _nextId = 1;
                }
            }
            else
            {
                _nextId = 1;
            }
        }
        catch
        {
            _nextId = 1;
        }
    }

    private void SaveIdCounter()
    {
        try
        {
            File.WriteAllText(_idCounterFilePath, _nextId.ToString());
        }
        catch
        {
            // Ignore errors
        }
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

public sealed class CreateCategoryRequest
{
    public string CategoryId { get; set; } = string.Empty;
}

public sealed class CategoryInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SoundCount { get; set; }
}

public sealed class MoveFileRequest
{
    public string SoundId { get; set; } = string.Empty;
    public string TargetCategory { get; set; } = string.Empty;
}

public sealed class MoveFileResult
{
    public string SoundId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}



