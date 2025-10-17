using System.Text.Json;
using fakeinstants.Models;

namespace fakeinstants.Services;

public class JsonStorageService
{
    private readonly ILogger<JsonStorageService> _logger;
    private readonly FolderBasedCategoryService _folderService;
    private const string SOUND_DATA_FILE = "data/sounds.json";
    private const string SETTINGS_FILE = "data/settings.json";

    public JsonStorageService(
        ILogger<JsonStorageService> logger,
        FolderBasedCategoryService folderService)
    {
        _logger = logger;
        _folderService = folderService;
    }

    // Sound Data Management
    public async Task<SoundData> LoadSoundDataAsync()
    {
        try
        {
            _logger.LogInformation("JsonStorageService.LoadSoundDataAsync() - Starting to load sound data from folder structure...");

            // Load exclusively from folder structure (Server/media/)
            var folderCategories = await _folderService.GetCategoriesFromFoldersAsync();
            var folderSounds = await _folderService.GetSoundsFromFoldersAsync();

            var folderData = new SoundData
            {
                Categories = folderCategories,
                Sounds = folderSounds
            };

            _logger.LogInformation("JsonStorageService.LoadSoundDataAsync() - Loaded {0} sounds and {1} categories from folder structure",
                folderSounds.Count, folderCategories.Count);

            return folderData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JsonStorageService.LoadSoundDataAsync() - Error loading sound data from folders: {0}", ex.Message);
            return CreateDefaultSoundData();
        }
    }


    public async Task SaveSoundDataAsync(SoundData data)
    {
        try
        {
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            // Save to physical file only
            var physicalFilePath = Path.Combine("wwwroot", SOUND_DATA_FILE);
            var directory = Path.GetDirectoryName(physicalFilePath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(physicalFilePath, json);

            _logger.LogInformation("Sound data saved to file ({0} sounds, {1} categories)",
                data.Sounds.Count, data.Categories.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving sound data: {0}", ex.Message);
            throw;
        }
    }

    // Settings Management
    public async Task<AppSettings> LoadSettingsAsync()
    {
        try
        {
            if (!File.Exists(SETTINGS_FILE))
            {
                _logger.LogInformation("Settings file not found, creating default settings");
                var defaultSettings = new AppSettings();
                await SaveSettingsAsync(defaultSettings);
                return defaultSettings;
            }

            var json = await File.ReadAllTextAsync(SETTINGS_FILE);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return settings ?? new AppSettings();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading settings");
            return new AppSettings();
        }
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(SETTINGS_FILE, json);
            _logger.LogInformation("Settings saved successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving settings");
            throw;
        }
    }

    private SoundData CreateDefaultSoundData()
    {
        return new SoundData
        {
            Categories = SoundData.DefaultCategories
        };
    }

}
