namespace fakeinstants.Models;

public class Sound
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CategoryId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public double Duration { get; set; }
    public string Format { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastPlayed { get; set; }
    public int PlayCount { get; set; }
    public bool Favorite { get; set; }

    // Computed properties
    public string DisplayName => string.IsNullOrEmpty(Name) ? FileName : Name;
    public string FileSizeFormatted => FormatFileSize(FileSize);
    public string DurationFormatted => FormatDuration(Duration);

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }

    private static string FormatDuration(double seconds)
    {
        var timeSpan = TimeSpan.FromSeconds(seconds);
        if (timeSpan.TotalMinutes >= 1)
        {
            return $"{(int)timeSpan.TotalMinutes}:{timeSpan.Seconds:D2}";
        }
        return $"{timeSpan.Seconds}.{timeSpan.Milliseconds / 100}s";
    }
}

