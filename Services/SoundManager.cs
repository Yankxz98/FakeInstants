using fakeinstants.Models;

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
            Category = categoryId,
            FileName = fileName,
            FilePath = $"audio/categories/{category.Name}/{fileName}",
            Format = Path.GetExtension(fileName).TrimStart('.').ToLower()
        };

        // Note: Duration will be calculated when first played to avoid blocking initialization
        // sound.Duration = await _audioService.GetAudioDurationAsync(sound.FilePath);

        _currentData.Sounds.Add(sound);
        category.SoundCount++;

        await SaveDataAsync();
        _logger.LogInformation("Added sound: {0}", sound.DisplayName);

        return sound;
    }

    public async Task<Sound?> AddProcessedSoundAsync(Sound sound)
    {
        if (_currentData == null) await InitializeAsync();

        var category = _currentData!.Categories.FirstOrDefault(c => c.Id == sound.Category);
        if (category == null)
        {
            _logger.LogWarning("Category {0} not found", sound.Category);
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
        if (existingSound.Category != sound.Category)
        {
            var oldCategory = _currentData.Categories.FirstOrDefault(c => c.Id == existingSound.Category);
            var newCategory = _currentData.Categories.FirstOrDefault(c => c.Id == sound.Category);

            if (oldCategory != null) oldCategory.SoundCount--;
            if (newCategory != null) newCategory.SoundCount++;

            existingSound.Category = sound.Category;
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
        var category = _currentData.Categories.FirstOrDefault(c => c.Id == sound.Category);
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

        if (_currentData!.Categories.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning("Category with name '{0}' already exists", name);
            return null;
        }

        var category = new Category(name, description);
        _currentData.Categories.Add(category);

        await SaveDataAsync();
        _logger.LogInformation("Added category: {0}", category.Name);

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
        return _currentData!.Sounds.Where(s => s.Category == categoryId).ToList();
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
}
