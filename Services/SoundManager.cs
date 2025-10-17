using fakeinstants.Models;
using System.Net.Http.Json;

namespace fakeinstants.Services;

public class SoundManager
{
    private readonly JsonStorageService _storage;
    private readonly AudioService _audioService;
    private readonly FolderBasedCategoryService _folderService;
    private readonly FileMoveService _fileMoveService;
    private readonly ILogger<SoundManager> _logger;
    private readonly HttpClient _httpClient;
    private SoundData? _currentData;

    public SoundManager(
        JsonStorageService storage,
        AudioService audioService,
        FolderBasedCategoryService folderService,
        FileMoveService fileMoveService,
        HttpClient httpClient,
        ILogger<SoundManager> logger)
    {
        _storage = storage;
        _audioService = audioService;
        _folderService = folderService;
        _fileMoveService = fileMoveService;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        try
        {
            _logger.LogInformation("SoundManager.InitializeAsync() - Starting initialization with filesystem as primary source...");

            // PRIMARY SOURCE: Load from folder structure (filesystem)
            _logger.LogInformation("SoundManager.InitializeAsync() - Loading from folder structure...");
            var folderCategories = await _folderService.GetCategoriesFromFoldersAsync();
            var folderSounds = await _folderService.GetSoundsFromFoldersAsync();

            var folderData = new SoundData
            {
                Categories = folderCategories,
                Sounds = folderSounds
            };

            _logger.LogInformation("SoundManager.InitializeAsync() - Loaded {0} sounds and {1} categories from filesystem",
                folderSounds.Count, folderCategories.Count);

            // SECONDARY: Load metadata from JSON/localStorage for enrichment
            _logger.LogInformation("SoundManager.InitializeAsync() - Loading metadata from localStorage for enrichment...");
            var jsonData = await _storage.LoadSoundDataAsync();

            // Enrich folder data with metadata from JSON (favorites, play counts, descriptions, etc.)
            await EnrichFolderDataWithJsonMetadata(folderData, jsonData);

            _currentData = folderData;

            // Sync metadata back to storage for consistency
            await _storage.SaveSoundDataAsync(_currentData);

            _logger.LogInformation("SoundManager.InitializeAsync() - Initialization completed with {0} sounds and {1} categories",
                _currentData.Sounds.Count, _currentData.Categories.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SoundManager.InitializeAsync() - Error during SoundManager initialization: {Message}", ex.Message);

            // Fallback to JSON data if filesystem fails
            try
            {
                _logger.LogWarning("SoundManager.InitializeAsync() - Falling back to JSON data");
                _currentData = await _storage.LoadSoundDataAsync();
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "SoundManager.InitializeAsync() - Fallback also failed, creating empty data");
                _currentData = new SoundData();
            }
        }
    }

    /// <summary>
    /// Enrich folder-based data with metadata from JSON (favorites, play counts, descriptions, etc.)
    /// </summary>
    private async Task EnrichFolderDataWithJsonMetadata(SoundData folderData, SoundData jsonData)
    {
        try
        {
            _logger.LogInformation("SoundManager.EnrichFolderDataWithJsonMetadata() - Enriching folder data with JSON metadata...");

            // Create lookup dictionary for JSON sounds by ID
            var jsonSoundsById = jsonData.Sounds.ToDictionary(s => s.Id, s => s);
            var jsonCategoriesById = jsonData.Categories.ToDictionary(c => c.Id, c => c);

            // Enrich sounds with metadata from JSON
            foreach (var folderSound in folderData.Sounds)
            {
                if (jsonSoundsById.TryGetValue(folderSound.Id, out var jsonSound))
                {
                    // Keep folder-based properties, enrich with metadata
                    folderSound.Name = jsonSound.Name; // Override with user-defined name
                    folderSound.Description = jsonSound.Description;
                    folderSound.Favorite = jsonSound.Favorite;
                    folderSound.Tags = jsonSound.Tags ?? new List<string>();
                    folderSound.PlayCount = jsonSound.PlayCount;
                    folderSound.LastPlayed = jsonSound.LastPlayed;
                    _logger.LogDebug("SoundManager.EnrichFolderDataWithJsonMetadata() - Enriched sound {Id}: {Name}", folderSound.Id, folderSound.Name);
                }
            }

            // Add categories from JSON that don't exist in folders
            foreach (var jsonCategory in jsonData.Categories)
            {
                if (!folderData.Categories.Any(c => c.Id == jsonCategory.Id))
                {
                    // Only add if it has sounds in JSON but not in folders
                    var categorySounds = jsonData.Sounds.Where(s => s.CategoryId == jsonCategory.Id).ToList();
                    if (categorySounds.Any())
                    {
                        folderData.Categories.Add(jsonCategory);
                        _logger.LogInformation("SoundManager.EnrichFolderDataWithJsonMetadata() - Added category from JSON: {Name}", jsonCategory.Name);
                    }
                }
            }

            _logger.LogInformation("SoundManager.EnrichFolderDataWithJsonMetadata() - Folder data enriched with JSON metadata");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SoundManager.EnrichFolderDataWithJsonMetadata() - Failed to enrich folder data with JSON metadata");
        }
    }

    // Sound Management
    public async Task<Sound?> AddSoundAsync(string fileName, string categoryId, string description = "")
    {
        if (_currentData == null) await InitializeAsync();

        var category = _currentData!.Categories.FirstOrDefault(c => c.Id == categoryId);
        if (category == null)
        {
            _logger.LogWarning("Category {0} not found", categoryId);
            return null;
        }

        var sound = new Sound
        {
            Name = Path.GetFileNameWithoutExtension(fileName),
            Description = description,
            CategoryId = categoryId,
            FileName = fileName,
            FilePath = string.Empty, // Will be set by backend on upload
            Format = Path.GetExtension(fileName).TrimStart('.').ToLower()
        };

        // Note: This method is deprecated. Use AddProcessedSoundAsync with backend-provided Sound instead
        _logger.LogWarning("AddSoundAsync is deprecated. Use AddProcessedSoundAsync with backend upload.");

        _currentData.Sounds.Add(sound);
        category.SoundCount++;

        await SaveDataAsync();
        _logger.LogInformation("Added sound: {0}", sound.DisplayName);

        return sound;
    }

    public async Task<Sound?> AddProcessedSoundAsync(Sound sound)
    {
        if (_currentData == null) await InitializeAsync();

        var category = _currentData!.Categories.FirstOrDefault(c => c.Id == sound.CategoryId);
        if (category == null)
        {
            _logger.LogWarning("Category {0} not found", sound.CategoryId);
            return null;
        }

        // Ensure the sound has a unique ID
        if (string.IsNullOrEmpty(sound.Id))
        {
            sound.Id = new Sound().Id;
        }

        // Set creation time if not set
        if (sound.CreatedAt == default)
        {
            sound.CreatedAt = DateTime.UtcNow;
        }

        _currentData.Sounds.Add(sound);
        category.SoundCount++;

        await SaveDataAsync();
        _logger.LogInformation("Added processed sound: {0} with path: {1}", sound.DisplayName, sound.FilePath);

        return sound;
    }

    public async Task<bool> UpdateSoundAsync(Sound sound)
    {
        if (_currentData == null) await InitializeAsync();

        var existingSound = _currentData!.Sounds.FirstOrDefault(s => s.Id == sound.Id);
        if (existingSound == null)
        {
            _logger.LogWarning("Sound {0} not found", sound.Id);
            return false;
        }

        var categoryChanged = existingSound.CategoryId != sound.CategoryId;

        // Update properties
        existingSound.Name = sound.Name;
        existingSound.Description = sound.Description;
        existingSound.Tags = sound.Tags;
        existingSound.Favorite = sound.Favorite;

        // If category changed, move the file and update counts
        if (categoryChanged)
        {
            _logger.LogInformation("SoundManager.UpdateSoundAsync() - Category changed for sound {Id}: {OldCategory} -> {NewCategory}",
                sound.Id, existingSound.CategoryId, sound.CategoryId);

            // Move file to new category
            var moveResult = await _fileMoveService.MoveFileToCategoryAsync(sound.Id, sound.CategoryId);
            if (!moveResult.Success)
            {
                _logger.LogError("SoundManager.UpdateSoundAsync() - Failed to move file: {Message}", moveResult.Message);
                return false;
            }

            // Update category counts
            var oldCategory = _currentData.Categories.FirstOrDefault(c => c.Id == existingSound.CategoryId);
            var newCategory = _currentData.Categories.FirstOrDefault(c => c.Id == sound.CategoryId);

            if (oldCategory != null) oldCategory.SoundCount--;
            if (newCategory != null) newCategory.SoundCount++;

            existingSound.CategoryId = sound.CategoryId;

            // Sync with filesystem to get updated paths
            await SyncWithFilesystemAsync();
        }

        await SaveDataAsync();
        _logger.LogInformation("Updated sound: {0}", sound.DisplayName);

        return true;
    }

    public async Task<bool> DeleteSoundAsync(string soundId)
    {
        if (_currentData == null) await InitializeAsync();

        var sound = _currentData!.Sounds.FirstOrDefault(s => s.Id == soundId);
        if (sound == null)
        {
            _logger.LogWarning("Sound {0} not found", soundId);
            return false;
        }

        // Stop playing if active
        await _audioService.StopSoundAsync(soundId);

        // Remove file via API
        try
        {
            var response = await _httpClient.DeleteAsync($"/media/sounds/{soundId}");
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to delete sound via API: {StatusCode} - {Content}", response.StatusCode, errorContent);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting audio file via API: {Message}", ex.Message);
            return false;
        }

        // Update category count
        var category = _currentData.Categories.FirstOrDefault(c => c.Id == sound.CategoryId);
        if (category != null)
        {
            category.SoundCount--;
        }

        _currentData.Sounds.Remove(sound);
        await SaveDataAsync();

        _logger.LogInformation("Deleted sound: {0}", sound.DisplayName);
        return true;
    }

    // Category Management
    public async Task<Category?> AddCategoryAsync(string name, string description = "")
    {
        _logger.LogInformation("SoundManager.AddCategoryAsync() - Starting to add category: {Name}", name);

        if (_currentData == null)
        {
            _logger.LogInformation("SoundManager.AddCategoryAsync() - _currentData is null, calling InitializeAsync()");
            await InitializeAsync();
        }

        // Check if category with same name already exists
        if (_currentData!.Categories.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning("SoundManager.AddCategoryAsync() - Category with name '{0}' already exists", name);
            return null;
        }

        // Generate ID from name
        var proposedId = Category.GenerateIdFromName(name);

        // Ensure ID is unique (add suffix if necessary)
        var finalId = proposedId;
        var counter = 1;
        while (_currentData.Categories.Any(c => c.Id == finalId))
        {
            finalId = $"{proposedId}-{counter}";
            counter++;
        }

        var category = new Category(name, description)
        {
            Id = finalId
        };

        // Apply default styling if it's a known category
        var defaultCategory = SoundData.DefaultCategories.FirstOrDefault(c =>
            c.Id.Equals(finalId, StringComparison.OrdinalIgnoreCase));
        if (defaultCategory != null)
        {
            category.Color = defaultCategory.Color;
            category.Icon = defaultCategory.Icon;
            category.Description = defaultCategory.Description;
        }

        _currentData.Categories.Add(category);
        _logger.LogInformation("SoundManager.AddCategoryAsync() - Category added to local data: {Name} (ID: {Id})", category.Name, category.Id);

        // Create the category folder on the server
        _logger.LogInformation("SoundManager.AddCategoryAsync() - Calling CreateCategoryFolderAsync for ID: {CategoryId}", finalId);
        try
        {
            var folderCreated = await _folderService.CreateCategoryFolderAsync(finalId);
            if (!folderCreated)
            {
                _logger.LogWarning("SoundManager.AddCategoryAsync() - Failed to create category folder for {CategoryId}, but category was added to local data", finalId);
                // Don't return null here - category was added to local data, just folder creation failed
            }
            else
            {
                _logger.LogInformation("SoundManager.AddCategoryAsync() - Category folder created successfully for {CategoryId}", finalId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SoundManager.AddCategoryAsync() - Exception while creating folder: {Message}", ex.Message);
            // Don't return null here - category was added to local data, just folder creation failed
        }

        await SaveDataAsync();
        _logger.LogInformation("Added category: {0} with ID: {1} and created folder", category.Name, category.Id);

        return category;
    }

    public async Task<bool> UpdateCategoryAsync(Category category)
    {
        if (_currentData == null) await InitializeAsync();

        var existingCategory = _currentData!.Categories.FirstOrDefault(c => c.Id == category.Id);
        if (existingCategory == null)
        {
            _logger.LogWarning("Category {0} not found", category.Id);
            return false;
        }

        existingCategory.Name = category.Name;
        existingCategory.Description = category.Description;
        existingCategory.Color = category.Color;
        existingCategory.Icon = category.Icon;

        await SaveDataAsync();
        _logger.LogInformation("Updated category: {0}", category.Name);

        return true;
    }

    public async Task<bool> DeleteCategoryAsync(string categoryId)
    {
        _logger.LogInformation("SoundManager.DeleteCategoryAsync() - Starting to delete category: {CategoryId}", categoryId);

        if (_currentData == null) await InitializeAsync();

        var category = _currentData!.Categories.FirstOrDefault(c => c.Id == categoryId);
        if (category == null)
        {
            _logger.LogWarning("SoundManager.DeleteCategoryAsync() - Category {CategoryId} not found in local data", categoryId);
            return false;
        }

        // Check if category has sounds
        if (category.SoundCount > 0)
        {
            _logger.LogWarning("SoundManager.DeleteCategoryAsync() - Cannot delete category {CategoryName} - it contains {SoundCount} sounds", category.Name, category.SoundCount);
            return false;
        }

        _logger.LogInformation("SoundManager.DeleteCategoryAsync() - Deleting category folder on server: {CategoryId}", categoryId);

        // Delete the folder on the server first
        var folderDeleted = await _folderService.DeleteCategoryFolderAsync(categoryId);
        if (!folderDeleted)
        {
            _logger.LogError("SoundManager.DeleteCategoryAsync() - Failed to delete category folder: {CategoryId}", categoryId);
            return false;
        }

        _logger.LogInformation("SoundManager.DeleteCategoryAsync() - Category folder deleted successfully: {CategoryId}", categoryId);

        // Remove from local data
        _currentData.Categories.Remove(category);
        _logger.LogInformation("SoundManager.DeleteCategoryAsync() - Category removed from local data: {CategoryName}", category.Name);

        await SaveDataAsync();

        _logger.LogInformation("SoundManager.DeleteCategoryAsync() - Category deleted successfully: {CategoryName}", category.Name);
        return true;
    }


    /// <summary>
    /// Syncs the current data with the filesystem after operations
    /// </summary>
    public async Task SyncWithFilesystemAsync()
    {
        try
        {
            _logger.LogInformation("SoundManager.SyncWithFilesystemAsync() - Syncing with filesystem...");

            // Reload data from filesystem
            var folderCategories = await _folderService.GetCategoriesFromFoldersAsync();
            var folderSounds = await _folderService.GetSoundsFromFoldersAsync();

            var folderData = new SoundData
            {
                Categories = folderCategories,
                Sounds = folderSounds
            };

            // Enrich with current metadata
            if (_currentData != null)
            {
                await EnrichFolderDataWithJsonMetadata(folderData, _currentData);
            }

            _currentData = folderData;

            // Save updated data
            await SaveDataAsync();

            _logger.LogInformation("SoundManager.SyncWithFilesystemAsync() - Synced {0} sounds and {1} categories",
                folderSounds.Count, folderCategories.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SoundManager.SyncWithFilesystemAsync() - Error syncing with filesystem: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Migrates existing localStorage data to filesystem-based structure
    /// </summary>
    public async Task<MigrationResult> MigrateLocalStorageToFilesystemAsync()
    {
        var result = new MigrationResult();

        try
        {
            _logger.LogInformation("SoundManager.MigrateLocalStorageToFilesystemAsync() - Starting migration from localStorage to filesystem...");

            // Load current localStorage data
            var localStorageData = await _storage.LoadSoundDataAsync();

            if (localStorageData.Sounds.Count == 0 && localStorageData.Categories.Count == 0)
            {
                result.Success = true;
                result.Message = "Nenhum dado encontrado no localStorage para migrar";
                _logger.LogInformation("SoundManager.MigrateLocalStorageToFilesystemAsync() - No data to migrate");
                return result;
            }

            _logger.LogInformation("SoundManager.MigrateLocalStorageToFilesystemAsync() - Found {0} sounds and {1} categories in localStorage",
                localStorageData.Sounds.Count, localStorageData.Categories.Count);

            // Step 1: Create categories that don't exist in filesystem
            var filesystemCategories = await _folderService.GetCategoriesFromFoldersAsync();
            var filesystemCategoryIds = filesystemCategories.Select(c => c.Id).ToHashSet();

            var categoriesToCreate = localStorageData.Categories
                .Where(c => !filesystemCategoryIds.Contains(c.Id))
                .ToList();

            foreach (var category in categoriesToCreate)
            {
                var createdCategory = await AddCategoryAsync(category.Name, category.Description);
                if (createdCategory != null)
                {
                    result.Log.Add($"Criada categoria: {createdCategory.Name} (ID: {createdCategory.Id})");
                    _logger.LogInformation("SoundManager.MigrateLocalStorageToFilesystemAsync() - Created category: {Name}", createdCategory.Name);
                }
                else
                {
                    result.Log.Add($"ERRO: Falha ao criar categoria: {category.Name}");
                    _logger.LogWarning("SoundManager.MigrateLocalStorageToFilesystemAsync() - Failed to create category: {Name}", category.Name);
                }
            }

            // Step 2: Move sounds to their correct categories
            var filesystemSounds = await _folderService.GetSoundsFromFoldersAsync();
            var filesystemSoundIds = filesystemSounds.Select(s => s.Id).ToHashSet();

            var soundsToMove = localStorageData.Sounds
                .Where(s => filesystemSoundIds.Contains(s.Id))
                .ToList();

            if (soundsToMove.Count > 0)
            {
                _logger.LogInformation("SoundManager.MigrateLocalStorageToFilesystemAsync() - Moving {Count} sounds to correct categories", soundsToMove.Count);

                var moveResult = await _fileMoveService.MoveFilesToCategoriesAsync(soundsToMove);

                result.Log.Add($"Movidos {moveResult.FileResults.Count(r => r.Success)}/{moveResult.FileResults.Count} arquivos");

                foreach (var fileResult in moveResult.FileResults)
                {
                    if (fileResult.Success)
                    {
                        result.Log.Add($"OK: {fileResult.Message}");
                    }
                    else
                    {
                        result.Log.Add($"ERRO: {fileResult.Message}");
                    }
                }

                if (moveResult.Success)
                {
                    // Sync with filesystem after moves
                    await SyncWithFilesystemAsync();
                }
            }

            // Step 3: Clean up localStorage (optional - keep as backup)
            // Note: We'll keep localStorage data as backup for now

            result.Success = true;
            result.Message = $"Migração concluída. Categorias criadas: {categoriesToCreate.Count}, Arquivos movidos: {soundsToMove.Count}";

            _logger.LogInformation("SoundManager.MigrateLocalStorageToFilesystemAsync() - Migration completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SoundManager.MigrateLocalStorageToFilesystemAsync() - Error during migration: {Message}", ex.Message);
            result.Success = false;
            result.Message = $"Erro durante migração: {ex.Message}";
            result.Log.Add($"ERRO FATAL: {ex.Message}");
        }

        return result;
    }

    public class MigrationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<string> Log { get; set; } = new();
    }

    // Data Access
    public async Task<List<Sound>> GetAllSoundsAsync()
    {
        if (_currentData == null) await InitializeAsync();
        return _currentData!.Sounds;
    }

    public async Task<List<Category>> GetAllCategoriesAsync()
    {
        if (_currentData == null) await InitializeAsync();
        return _currentData!.Categories;
    }

    /// <summary>
    /// Forces reload of categories from filesystem (ignores cache)
    /// </summary>
    public async Task<List<Category>> GetCategoriesFromFilesystemAsync()
    {
        // Reload data from filesystem
        var folderCategories = await _folderService.GetCategoriesFromFoldersAsync();
        var folderSounds = await _folderService.GetSoundsFromFoldersAsync();

        var folderData = new SoundData
        {
            Categories = folderCategories,
            Sounds = folderSounds
        };

        // Enrich with current metadata if available
        if (_currentData != null)
        {
            await EnrichFolderDataWithJsonMetadata(folderData, _currentData);
        }

        // Update current data
        _currentData = folderData;

        // Save updated data
        await SaveDataAsync();

        _logger.LogInformation("SoundManager.GetCategoriesFromFilesystemAsync() - Synced {0} sounds and {1} categories from filesystem",
            folderSounds.Count, folderCategories.Count);

        return _currentData.Categories;
    }

    public async Task<List<Sound>> GetSoundsByCategoryAsync(string categoryId)
    {
        if (_currentData == null) await InitializeAsync();
        return _currentData!.Sounds.Where(s => s.CategoryId == categoryId).ToList();
    }

    public async Task<List<Sound>> SearchSoundsAsync(string query)
    {
        if (_currentData == null) await InitializeAsync();

        if (string.IsNullOrWhiteSpace(query))
            return _currentData!.Sounds;

        var lowerQuery = query.ToLower();
        return _currentData!.Sounds.Where(s =>
            s.Name.ToLower().Contains(lowerQuery) ||
            s.Description.ToLower().Contains(lowerQuery) ||
            s.Tags.Any(t => t.ToLower().Contains(lowerQuery))
        ).ToList();
    }

    public async Task<List<Sound>> GetFavoriteSoundsAsync()
    {
        if (_currentData == null) await InitializeAsync();
        return _currentData!.Sounds.Where(s => s.Favorite).ToList();
    }

    public async Task<List<Sound>> GetMostPlayedSoundsAsync(int count = 10)
    {
        if (_currentData == null) await InitializeAsync();
        return _currentData!.Sounds
            .OrderByDescending(s => s.PlayCount)
            .Take(count)
            .ToList();
    }

    private async Task SaveDataAsync()
    {
        if (_currentData != null)
        {
            await _storage.SaveSoundDataAsync(_currentData);
        }
    }

    public async Task FixCategoryIdsAsync()
    {
        if (_currentData == null) await InitializeAsync();

        var categoriesFixed = 0;

        foreach (var category in _currentData!.Categories.ToList())
        {
            // Check if ID is a GUID
            if (Guid.TryParse(category.Id, out _))
            {
                var oldId = category.Id;
                var newId = Category.GenerateIdFromName(category.Name);
                
                // Ensure uniqueness
                var finalId = newId;
                var counter = 1;
                while (_currentData.Categories.Any(c => c.Id == finalId && c.Id != oldId))
                {
                    finalId = $"{newId}-{counter}";
                    counter++;
                }

                category.Id = finalId;

                // Update all sounds that reference this category
                foreach (var sound in _currentData.Sounds.Where(s => s.CategoryId == oldId))
                {
                    sound.CategoryId = finalId;
                }

                categoriesFixed++;
                _logger.LogInformation("Fixed category ID: {0} -> {1}", oldId, finalId);
            }
        }

        if (categoriesFixed > 0)
        {
            await SaveDataAsync();
            _logger.LogInformation("Fixed {0} category IDs", categoriesFixed);
        }
    }

    public async Task SyncWithMediaDirectoryAsync(HttpClient httpClient)
    {
        if (_currentData == null) await InitializeAsync();
        if (_currentData == null) return; // Safety check

        try
        {
            // Call /media/scan endpoint
            var response = await httpClient.GetAsync("/media/scan");
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to scan media directory: {0}", response.StatusCode);
                return;
            }

            var scannedSounds = await response.Content.ReadFromJsonAsync<List<Sound>>();
            if (scannedSounds == null || scannedSounds.Count == 0)
            {
                _logger.LogInformation("No sounds found in media directory");
                return;
            }

            var syncedCount = 0;
            var updatedCount = 0;

            foreach (var scannedSound in scannedSounds)
            {
                var existingSound = _currentData.Sounds.FirstOrDefault(s => s.Id == scannedSound.Id);
                if (existingSound == null)
                {
                    // New sound found on disk
                    _currentData.Sounds.Add(scannedSound);

                    // Update category count
                    var category = _currentData.Categories.FirstOrDefault(c => c.Id == scannedSound.CategoryId);
                    if (category != null)
                    {
                        category.SoundCount++;
                    }

                    syncedCount++;
                    _logger.LogInformation("Synced new sound from disk: {0}", scannedSound.DisplayName);
                }
                else
                {
                    // Update FilePath if changed
                    if (existingSound.FilePath != scannedSound.FilePath)
                    {
                        existingSound.FilePath = scannedSound.FilePath;
                        existingSound.FileName = scannedSound.FileName;
                        existingSound.FileSize = scannedSound.FileSize;
                        updatedCount++;
                    }
                }
            }

            // Remove sounds that no longer exist on disk
            var soundsToRemove = new List<Sound>();
            foreach (var sound in _currentData.Sounds)
            {
                if (!scannedSounds.Any(s => s.Id == sound.Id))
                {
                    soundsToRemove.Add(sound);
                }
            }

            foreach (var sound in soundsToRemove)
            {
                _currentData.Sounds.Remove(sound);
                var category = _currentData.Categories.FirstOrDefault(c => c.Id == sound.CategoryId);
                if (category != null)
                {
                    category.SoundCount--;
                }
                _logger.LogInformation("Removed sound no longer on disk: {0}", sound.DisplayName);
            }

            await SaveDataAsync();
            _logger.LogInformation("Media sync completed: {0} new, {1} updated, {2} removed",
                syncedCount, updatedCount, soundsToRemove.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing with media directory");
        }
    }
}
