namespace fakeinstants.Models;

public class Category
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Color { get; set; } = "#007bff"; // Default blue
    public string Icon { get; set; } = "fas fa-music"; // Default music icon
    public int SoundCount { get; set; }

    public Category()
    {
    }

    public Category(string name, string description = "")
    {
        Name = name;
        Description = description;
        Id = GenerateIdFromName(name);
    }

    /// <summary>
    /// Generates a URL-safe slug from a category name
    /// Best practice: Keep original name for display, use slug for filesystem operations
    /// </summary>
    public static string GenerateSlug(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "categoria";

        // Step 1: Normalize unicode characters (remove accents)
        var normalized = name.ToLowerInvariant()
            .Normalize(System.Text.NormalizationForm.FormD);

        // Step 2: Keep only ASCII letters, digits, spaces, hyphens, and underscores
        var chars = normalized.ToCharArray()
            .Where(c => char.IsLetterOrDigit(c) ||
                       char.IsWhiteSpace(c) ||
                       c == '-' ||
                       c == '_')
            .ToArray();

        var cleaned = new string(chars);

        // Step 3: Normalize whitespace (replace multiple spaces with single space)
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ").Trim();

        // Step 4: Replace spaces with hyphens
        var slug = cleaned.Replace(' ', '-');

        // Step 5: Remove multiple consecutive hyphens
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"-+", "-");

        // Step 6: Remove leading/trailing hyphens
        slug = slug.Trim('-');

        // Step 7: Ensure minimum length and fallback
        if (string.IsNullOrEmpty(slug) || slug.Length < 2)
        {
            // Generate a fallback based on original name hash to avoid collisions
            var hash = Math.Abs(name.GetHashCode()).ToString();
            slug = $"categoria-{hash}";
        }

        return slug;
    }

    /// <summary>
    /// Legacy method - kept for backward compatibility
    /// </summary>
    [Obsolete("Use GenerateSlug instead for better handling of spaces and special characters")]
    public static string GenerateIdFromName(string name) => GenerateSlug(name);
}

/// <summary>
/// Best practices for handling names with spaces in filesystem and URLs
/// </summary>
public static class SlugUtils
{
    /// <summary>
    /// Comprehensive slug generation with collision handling
    /// Best practice: Always store both display name and slug separately
    /// </summary>
    public static string GenerateUniqueSlug(string name, Func<string, bool> existsChecker)
    {
        var baseSlug = Category.GenerateSlug(name);
        var slug = baseSlug;
        var counter = 1;

        // Handle collisions by appending counter
        while (existsChecker(slug))
        {
            slug = $"{baseSlug}-{counter}";
            counter++;
        }

        return slug;
    }

    /// <summary>
    /// Alternative slug generation methods for different use cases
    /// </summary>
    public static class SlugGenerators
    {
        /// <summary>
        /// For URLs: Strict, SEO-friendly slugs
        /// </summary>
        public static string ForUrls(string name) => Category.GenerateSlug(name);

        /// <summary>
        /// For filesystem: Same as URLs but ensures filesystem compatibility
        /// </summary>
        public static string ForFilesystem(string name) => Category.GenerateSlug(name);

        /// <summary>
        /// For database keys: Can be more permissive
        /// </summary>
        public static string ForDatabase(string name)
        {
            // Similar to URL slugs but allows underscores
            return name.ToLowerInvariant()
                .Replace(' ', '_')
                .Replace('-', '_');
        }

        /// <summary>
        /// For display: Keep original formatting
        /// </summary>
        public static string ForDisplay(string name) => name;
    }

    /// <summary>
    /// Validation methods for slugs
    /// </summary>
    public static class Validators
    {
        public static bool IsValidSlug(string slug)
        {
            return !string.IsNullOrWhiteSpace(slug) &&
                   System.Text.RegularExpressions.Regex.IsMatch(slug, @"^[a-z0-9]+(?:-[a-z0-9]+)*$");
        }

        public static bool IsValidFilename(string filename)
        {
            return !string.IsNullOrWhiteSpace(filename) &&
                   !Path.GetInvalidFileNameChars().Any(filename.Contains);
        }
    }

    /// <summary>
    /// Test cases demonstrating slug generation
    /// </summary>
    public static class Examples
    {
        public static readonly (string Input, string ExpectedSlug, string Description)[] TestCases = new[]
        {
            ("Rock Nacional", "rock-nacional", "Espaços simples"),
            ("Músicas   Clássicas", "musicas-classicas", "Múltiplos espaços + acentos"),
            ("Pop & Rock 2024!", "pop-rock-2024", "Caracteres especiais + números"),
            ("Heavy Metal/Thrash", "heavy-metalthrash", "Barras e espaços"),
            ("   Espaços   no   Início   ", "espacos-no-inicio", "Espaços extras"),
            ("--Traços--Múltiplos--", "tracos-multiplos", "Traços extras"),
            ("", "categoria", "String vazia"),
            ("A", "categoria-97", "Nome muito curto (fallback com hash)"),
            ("Café & Música", "cafe-musica", "Caracteres acentuados"),
            ("Rock'n'Roll", "rocknroll", "Apóstrofos"),
            ("Hip Hop/R&B", "hip-hoprb", "Múltiplos separadores")
        };
    }
}

