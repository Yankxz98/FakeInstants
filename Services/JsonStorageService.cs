using System.IO.Compression;
using System.Text.Json;
using fakeinstants.Models;
using Microsoft.JSInterop;

namespace fakeinstants.Services;

public class JsonStorageService
{
    private readonly ILogger<JsonStorageService> _logger;
    private readonly IJSRuntime _jsRuntime;
    private const string SOUND_DATA_FILE = "data/sounds.json";
    private const string SETTINGS_FILE = "data/settings.json";

    public JsonStorageService(ILogger<JsonStorageService> logger, IJSRuntime jsRuntime)
    {
        _logger = logger;
        _jsRuntime = jsRuntime;
    }

    // Sound Data Management
    public async Task<SoundData> LoadSoundDataAsync()
    {
        try
        {
            _logger.LogInformation("JsonStorageService.LoadSoundDataAsync() - Starting to load sound data...");
            
            // First try to load from physical file
            var physicalFilePath = Path.Combine("wwwroot", SOUND_DATA_FILE);
            SoundData? fileData = null;
            
            if (File.Exists(physicalFilePath))
            {
                _logger.LogInformation("JsonStorageService.LoadSoundDataAsync() - Loading from physical file: {0}", physicalFilePath);
                var fileJson = await File.ReadAllTextAsync(physicalFilePath);
                if (!string.IsNullOrEmpty(fileJson))
                {
                    fileData = JsonSerializer.Deserialize<SoundData>(fileJson, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
            }

            // Then try localStorage as fallback/sync
            _logger.LogInformation("JsonStorageService.LoadSoundDataAsync() - Checking localStorage...");
            var storedData = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "fakeinstants-sound-data");
            SoundData? localStorageData = null;

            if (!string.IsNullOrEmpty(storedData))
            {
                // Decompress data from localStorage
                var decompressedData = await DecodeAndDecompressAsync(storedData);
                localStorageData = JsonSerializer.Deserialize<SoundData>(decompressedData, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }

            // Use the data source with more sounds (localStorage is more up-to-date)
            SoundData dataToUse;
            if (localStorageData != null && fileData != null)
            {
                dataToUse = localStorageData.Sounds.Count >= fileData.Sounds.Count ? localStorageData : fileData;
                _logger.LogInformation("JsonStorageService.LoadSoundDataAsync() - Using {0} (localStorage: {1} sounds, file: {2} sounds)", 
                    dataToUse == localStorageData ? "localStorage" : "file", 
                    localStorageData.Sounds.Count, 
                    fileData.Sounds.Count);
            }
            else if (localStorageData != null)
            {
                dataToUse = localStorageData;
                _logger.LogInformation("JsonStorageService.LoadSoundDataAsync() - Using localStorage data ({0} sounds)", localStorageData.Sounds.Count);
            }
            else if (fileData != null)
            {
                dataToUse = fileData;
                _logger.LogInformation("JsonStorageService.LoadSoundDataAsync() - Using file data ({0} sounds)", fileData.Sounds.Count);
            }
            else
            {
                _logger.LogInformation("JsonStorageService.LoadSoundDataAsync() - No data found, creating default");
                dataToUse = CreateDefaultSoundData();
            }

            // Always sync both storage methods
            await SaveSoundDataAsync(dataToUse);
            
            _logger.LogInformation("JsonStorageService.LoadSoundDataAsync() - Sound data loaded successfully with {0} sounds and {1} categories", dataToUse.Sounds.Count, dataToUse.Categories.Count);
            return dataToUse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JsonStorageService.LoadSoundDataAsync() - Error loading sound data: {0}", ex.Message);
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

            // Save to both localStorage and physical file for consistency
            // Compress data for localStorage to save space
            var compressedJson = await CompressAndEncodeAsync(json);
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "fakeinstants-sound-data", compressedJson);
            
            // Also save to physical file
            var physicalFilePath = Path.Combine("wwwroot", SOUND_DATA_FILE);
            var directory = Path.GetDirectoryName(physicalFilePath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            await File.WriteAllTextAsync(physicalFilePath, json);
            
            _logger.LogInformation("Sound data saved successfully to both localStorage and file ({0} sounds, {1} categories)", 
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

    // Compression helpers for localStorage
    private async Task<string> CompressAndEncodeAsync(string data)
    {
        try
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(data);
            using var outputStream = new MemoryStream();
            using (var gzipStream = new GZipStream(outputStream, CompressionMode.Compress))
            {
                await gzipStream.WriteAsync(bytes, 0, bytes.Length);
            }
            var compressedBytes = outputStream.ToArray();
            return Convert.ToBase64String(compressedBytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to compress data, using uncompressed data");
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(data));
        }
    }

    private async Task<string> DecodeAndDecompressAsync(string compressedData)
    {
        try
        {
            var compressedBytes = Convert.FromBase64String(compressedData);
            using var inputStream = new MemoryStream(compressedBytes);
            using var outputStream = new MemoryStream();
            using (var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress))
            {
                await gzipStream.CopyToAsync(outputStream);
            }
            var decompressedBytes = outputStream.ToArray();
            return System.Text.Encoding.UTF8.GetString(decompressedBytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decompress data, trying as uncompressed base64");
            try
            {
                // Fallback: try to decode as regular base64 (uncompressed data)
                var bytes = Convert.FromBase64String(compressedData);
                return System.Text.Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                _logger.LogError(ex, "Failed to decode data as both compressed and uncompressed");
                throw;
            }
        }
    }
}
