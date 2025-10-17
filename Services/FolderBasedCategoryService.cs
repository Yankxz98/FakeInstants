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
            _logger.LogInformation("FolderBasedCategoryService.GetCategoriesFromFoldersAsync() - Loading categories from server...");

            // Call /media/categories to get all categories directly from folders
            var response = await _httpClient.GetAsync("/media/categories");
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("FolderBasedCategoryService.GetCategoriesFromFoldersAsync() - Failed to load categories: {StatusCode}", response.StatusCode);
                return new List<Category>();
            }

            var categoryInfos = await response.Content.ReadFromJsonAsync<List<CategoryInfo>>();
            if (categoryInfos == null)
            {
                _logger.LogInformation("FolderBasedCategoryService.GetCategoriesFromFoldersAsync() - No categories found");
                return new List<Category>();
            }

            var categories = new List<Category>();
            foreach (var categoryInfo in categoryInfos)
            {
                // Create category with readable ID
                var category = new Category
                {
                    Id = categoryInfo.Id,
                    Name = categoryInfo.Name,
                    Description = $"Categoria {categoryInfo.Name}",
                    SoundCount = categoryInfo.SoundCount
                };

                // Try to apply default styling if it's a known category
                var defaultCategory = SoundData.DefaultCategories.FirstOrDefault(c =>
                    c.Id.Equals(categoryInfo.Id, StringComparison.OrdinalIgnoreCase));

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

            _logger.LogInformation("FolderBasedCategoryService.GetCategoriesFromFoldersAsync() - Loaded {Count} categories from server",
                sortedCategories.Count);

            return sortedCategories;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FolderBasedCategoryService.GetCategoriesFromFoldersAsync() - Error loading categories: {Message}", ex.Message);
            return new List<Category>();
        }
    }

    /// <summary>
    /// Creates a category folder on the server
    /// </summary>
    public async Task<bool> CreateCategoryFolderAsync(string categoryId)
    {
        try
        {
            _logger.LogInformation("FolderBasedCategoryService.CreateCategoryFolderAsync() - Creating category folder: {CategoryId}", categoryId);

            var request = new CreateCategoryRequest { CategoryId = categoryId };
            _logger.LogInformation("FolderBasedCategoryService.CreateCategoryFolderAsync() - Sending request to /media/create-category with CategoryId: {CategoryId}", categoryId);

            var response = await _httpClient.PostAsJsonAsync("/media/create-category", request);
            _logger.LogInformation("FolderBasedCategoryService.CreateCategoryFolderAsync() - Response status: {StatusCode}", response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("FolderBasedCategoryService.CreateCategoryFolderAsync() - Failed to create category folder. Status: {StatusCode}, Content: {Content}", response.StatusCode, errorContent);
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<CreateCategoryResponse>();
            if (result == null)
            {
                _logger.LogError("FolderBasedCategoryService.CreateCategoryFolderAsync() - Invalid response from server - result is null");
                return false;
            }

            _logger.LogInformation("FolderBasedCategoryService.CreateCategoryFolderAsync() - Category folder created successfully: {Message}", result.message);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FolderBasedCategoryService.CreateCategoryFolderAsync() - Exception: {Message}, StackTrace: {StackTrace}", ex.Message, ex.StackTrace);
            return false;
        }
    }

    /// <summary>
    /// Deletes a category folder from the server
    /// </summary>
    public async Task<bool> DeleteCategoryFolderAsync(string categoryId)
    {
        try
        {
            _logger.LogInformation("FolderBasedCategoryService.DeleteCategoryFolderAsync() - Deleting category folder: {CategoryId}", categoryId);

            var response = await _httpClient.DeleteAsync($"/media/categories/{Uri.EscapeDataString(categoryId)}");
            _logger.LogInformation("FolderBasedCategoryService.DeleteCategoryFolderAsync() - Response status: {StatusCode}", response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("FolderBasedCategoryService.DeleteCategoryFolderAsync() - Failed to delete category folder. Status: {StatusCode}, Content: {Content}", response.StatusCode, errorContent);
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<DeleteCategoryResponse>();
            if (result == null)
            {
                _logger.LogError("FolderBasedCategoryService.DeleteCategoryFolderAsync() - Invalid response from server - result is null");
                return false;
            }

            _logger.LogInformation("FolderBasedCategoryService.DeleteCategoryFolderAsync() - Category folder deleted successfully: {Message}", result.message);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FolderBasedCategoryService.DeleteCategoryFolderAsync() - Exception: {Message}, StackTrace: {StackTrace}", ex.Message, ex.StackTrace);
            return false;
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

    // DTOs for server communication
    private class CreateCategoryRequest
    {
        public string CategoryId { get; set; } = string.Empty;
    }

    private class CreateCategoryResponse
    {
        public string message { get; set; } = string.Empty;
        public string categoryId { get; set; } = string.Empty;
    }

    private class DeleteCategoryResponse
    {
        public string message { get; set; } = string.Empty;
    }

    private class CategoryInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int SoundCount { get; set; }
    }
}