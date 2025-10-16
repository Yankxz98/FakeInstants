using fakeinstants.Models;
using System.Net.Http.Json;

namespace fakeinstants.Services;

public class SoundManager
{
    private readonly JsonStorageService _storage;
    private readonly AudioService _audioService;
    private readonly ILogger<SoundManager> _logger;
    private SoundData? _currentData;

    public SoundManager(JsonStorageService storage, AudioService audioService, ILogger<SoundManager> logger)
    {
        _storage = storage;
        _audioService = audioService;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        try
        {
            _logger.LogInformation("SoundManager.InitializeAsync() - Starting initialization...");
            _logger.LogInformation("SoundManager.InitializeAsync() - Calling _storage.LoadSoundDataAsync()...");
            _currentData = await _storage.LoadSoundDataAsync();
            _logger.LogInformation("SoundManager.InitializeAsync() - _storage.LoadSoundDataAsync() completed");
            _logger.LogInformation("SoundManager initialized with {0} sounds and {1} categories",
                _currentData.Sounds.Count, _currentData.Categories.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SoundManager.InitializeAsync() - Error during SoundManager initialization");
            throw;
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
            sound.Id = Guid.NewGuid().ToString();
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

        // Update properties
        existingSound.Name = sound.Name;
        existingSound.Description = sound.Description;
        existingSound.Tags = sound.Tags;
        existingSound.Favorite = sound.Favorite;

        // If category changed, update counts
        if (existingSound.CategoryId != sound.CategoryId)
        {
            var oldCategory = _currentData.Categories.FirstOrDefault(c => c.Id == existingSound.CategoryId);
            var newCategory = _currentData.Categories.FirstOrDefault(c => c.Id == sound.CategoryId);

            if (oldCategory != null) oldCategory.SoundCount--;
            if (newCategory != null) newCategory.SoundCount++;

            existingSound.CategoryId = sound.CategoryId;
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

        // Remove file
        try
        {
            var physicalPath = Path.Combine("wwwroot", sound.FilePath);
            if (File.Exists(physicalPath))
            {
                File.Delete(physicalPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting audio file: {0}", sound.FilePath);
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
        if (_currentData == null) await InitializeAsync();

        // Check if category with same name already exists
        if (_currentData!.Categories.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning("Category with name '{0}' already exists", name);
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
        
        _currentData.Categories.Add(category);

        await SaveDataAsync();
        _logger.LogInformation("Added category: {0} with ID: {1}", category.Name, category.Id);

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
        if (_currentData == null) await InitializeAsync();

        var category = _currentData!.Categories.FirstOrDefault(c => c.Id == categoryId);
        if (category == null)
        {
            _logger.LogWarning("Category {0} not found", categoryId);
            return false;
        }

        // Check if category has sounds
        if (category.SoundCount > 0)
        {
            _logger.LogWarning("Cannot delete category {0} - it contains {1} sounds", category.Name, category.SoundCount);
            return false;
        }

        _currentData.Categories.Remove(category);
        await SaveDataAsync();

        _logger.LogInformation("Deleted category: {0}", category.Name);
        return true;
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
                var existingSound = _currentData!.Sounds.FirstOrDefault(s => s.Id == scannedSound.Id);
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
