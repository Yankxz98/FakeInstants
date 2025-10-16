using System.Net.Http.Json;
using fakeinstants.Models;

namespace fakeinstants.Services;

public class FolderBasedCategoryService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FolderBasedCategoryService> _logger;

    public FolderBasedCategoryService(HttpClient httpClient, ILogger<FolderBasedCategoryService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Gets categories from the folder structure on the server
    /// </summary>
    public async Task<List<Category>> GetCategoriesFromFoldersAsync()
    {
        try
        {
            _logger.LogInformation("FolderBasedCategoryService.GetCategoriesFromFoldersAsync() - Scanning media folders for categories...");

            // Call /media/scan to get all sounds from filesystem
            var response = await _httpClient.GetAsync("/media/scan");
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("FolderBasedCategoryService.GetCategoriesFromFoldersAsync() - Failed to scan media directory: {StatusCode}", response.StatusCode);
                return new List<Category>();
            }

            var scannedSounds = await response.Content.ReadFromJsonAsync<List<Sound>>();
            if (scannedSounds == null || scannedSounds.Count == 0)
            {
                _logger.LogInformation("FolderBasedCategoryService.GetCategoriesFromFoldersAsync() - No sounds found in media directory");
                return new List<Category>();
            }

            // Extract unique categories from the folder structure
            var categoryIds = scannedSounds
                .Select(s => s.CategoryId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var categories = new List<Category>();
            foreach (var categoryId in categoryIds)
            {
                // Create category with readable ID
                var category = new Category
                {
                    Id = categoryId,
                    Name = FormatCategoryName(categoryId),
                    Description = $"Categoria {FormatCategoryName(categoryId)}",
                    SoundCount = scannedSounds.Count(s => s.CategoryId == categoryId)
                };

                // Try to apply default styling if it's a known category
                var defaultCategory = SoundData.DefaultCategories.FirstOrDefault(c =>
                    c.Id.Equals(categoryId, StringComparison.OrdinalIgnoreCase));

                if (defaultCategory != null)
                {
                    category.Color = defaultCategory.Color;
                    category.Icon = defaultCategory.Icon;
                    category.Description = defaultCategory.Description;
                }
                else
                {
                    // Default styling for custom categories
                    category.Color = "#6c757d"; // Gray
                    category.Icon = "fas fa-folder";
                }

                categories.Add(category);
                _logger.LogInformation("FolderBasedCategoryService.GetCategoriesFromFoldersAsync() - Found category: {Id} ({Name}) with {Count} sounds",
                    category.Id, category.Name, category.SoundCount);
            }

            // Sort categories: default categories first, then custom ones alphabetically
            var defaultCategoryIds = SoundData.DefaultCategories.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var sortedCategories = categories
                .OrderByDescending(c => defaultCategoryIds.Contains(c.Id)) // Default categories first
                .ThenBy(c => c.Name) // Then alphabetical
                .ToList();

            _logger.LogInformation("FolderBasedCategoryService.GetCategoriesFromFoldersAsync() - Found {Count} categories from folder structure",
                sortedCategories.Count);

            return sortedCategories;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FolderBasedCategoryService.GetCategoriesFromFoldersAsync() - Error scanning folders: {Message}", ex.Message);
            return new List<Category>();
        }
    }

    /// <summary>
    /// Gets sounds from the folder structure on the server
    /// </summary>
    public async Task<List<Sound>> GetSoundsFromFoldersAsync()
    {
        try
        {
            _logger.LogInformation("FolderBasedCategoryService.GetSoundsFromFoldersAsync() - Scanning media folders for sounds...");

            // Call /media/scan to get all sounds from filesystem
            var response = await _httpClient.GetAsync("/media/scan");
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("FolderBasedCategoryService.GetSoundsFromFoldersAsync() - Failed to scan media directory: {StatusCode}", response.StatusCode);
                return new List<Sound>();
            }

            var scannedSounds = await response.Content.ReadFromJsonAsync<List<Sound>>();
            if (scannedSounds == null)
            {
                _logger.LogInformation("FolderBasedCategoryService.GetSoundsFromFoldersAsync() - No sounds found in media directory");
                return new List<Sound>();
            }

            // Convert SoundDto to Sound model and ensure proper structure
            var sounds = scannedSounds.Select(dto => new Sound
            {
                Id = dto.Id,
                Name = dto.Name,
                Description = dto.Description,
                CategoryId = dto.CategoryId,
                FileName = dto.FileName,
                FilePath = dto.FilePath,
                FileSize = dto.FileSize,
                Duration = dto.Duration,
                Format = dto.Format,
                CreatedAt = dto.CreatedAt,
                Tags = new List<string>(),
                Favorite = false,
                PlayCount = 0,
                LastPlayed = null
            }).ToList();

            _logger.LogInformation("FolderBasedCategoryService.GetSoundsFromFoldersAsync() - Found {Count} sounds from folder structure", sounds.Count);

            return sounds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FolderBasedCategoryService.GetSoundsFromFoldersAsync() - Error scanning sounds: {Message}", ex.Message);
            return new List<Sound>();
        }
    }

    /// <summary>
    /// Formats a category ID into a readable name
    /// </summary>
    private static string FormatCategoryName(string categoryId)
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
}