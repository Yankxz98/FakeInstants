using Microsoft.JSInterop;
using fakeinstants.Models;
using System.Net.Http;

namespace fakeinstants.Services;

public class AudioService
{
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<AudioService> _logger;
    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, AudioInstance> _activeAudios = new();
    private double _masterVolume = 1.0;

    public event EventHandler<string>? AudioStarted;
    public event EventHandler<string>? AudioEnded;
    
    public string? CurrentPlayingSoundId { get; private set; }

    private DotNetObjectReference<AudioService>? _objRef;

    public AudioService(IJSRuntime jsRuntime, ILogger<AudioService> logger, HttpClient httpClient)
    {
        _jsRuntime = jsRuntime;
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task InitializeAsync()
    {
        try
        {
            _objRef = DotNetObjectReference.Create(this);
            await _jsRuntime.InvokeVoidAsync("audioService.setDotNetReference", _objRef);
            _logger.LogInformation("Audio service initialized with .NET reference");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing audio service");
        }
    }

    public async Task<bool> PlaySoundAsync(Sound sound)
    {
        try
        {
            // Stop all currently playing sounds before playing the new one
            await StopAllSoundsAsync();

            // Get duration if not already calculated (use audio metadata from URL)
            if (sound.Duration <= 0)
            {
                try
                {
                    sound.Duration = await GetAudioDurationAsync(sound.FilePath);
                    _logger.LogInformation($"Calculated duration for {sound.DisplayName}: {sound.Duration}s");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Could not get duration for {sound.DisplayName}, using default");
                    sound.Duration = 0;
                }
            }

            // Stop any currently playing instance of the same sound
            if (_activeAudios.ContainsKey(sound.Id))
            {
                await StopSoundAsync(sound.Id);
            }

            // Create new audio instance
            var resolvedPath = ResolveMediaUrl(sound.FilePath);
            var audioInstance = new AudioInstance(sound.Id, resolvedPath);
            _activeAudios[sound.Id] = audioInstance;

            // Initialize audio element via JavaScript
            await _jsRuntime.InvokeVoidAsync("audioService.initializeAudio", sound.Id, resolvedPath, _masterVolume);

            // Play the audio
            await _jsRuntime.InvokeVoidAsync("audioService.playAudio", sound.Id);

            // Update sound statistics (this would be handled by SoundManager)
            sound.LastPlayed = DateTime.UtcNow;
            sound.PlayCount++;

            CurrentPlayingSoundId = sound.Id;
            AudioStarted?.Invoke(this, sound.Id);

            _logger.LogInformation($"Playing sound: {sound.DisplayName}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error playing sound {sound.Id}");
            return false;
        }
    }

    public async Task<bool> StopSoundAsync(string soundId)
    {
        try
        {
            if (_activeAudios.ContainsKey(soundId))
            {
                await _jsRuntime.InvokeVoidAsync("audioService.stopAudio", soundId);
                
                if (CurrentPlayingSoundId == soundId)
                {
                    CurrentPlayingSoundId = null;
                }
                
                _activeAudios.Remove(soundId);
                AudioEnded?.Invoke(this, soundId);
                
                _logger.LogInformation($"Stopped sound: {soundId}");
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error stopping sound {soundId}");
            return false;
        }
    }

    public async Task<bool> PauseSoundAsync(string soundId)
    {
        try
        {
            if (_activeAudios.ContainsKey(soundId))
            {
                await _jsRuntime.InvokeVoidAsync("audioService.pauseAudio", soundId);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error pausing sound {soundId}");
            return false;
        }
    }

    public async Task SetVolumeAsync(double volume)
    {
        _masterVolume = Math.Clamp(volume, 0.0, 1.0);
        try
        {
            await _jsRuntime.InvokeVoidAsync("audioService.setMasterVolume", _masterVolume);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting master volume");
        }
    }

    public async Task StopAllSoundsAsync()
    {
        var soundIds = _activeAudios.Keys.ToList();
        foreach (var soundId in soundIds)
        {
            await StopSoundAsync(soundId);
        }
        
        CurrentPlayingSoundId = null;
    }

    public async Task<double> GetAudioDurationAsync(string filePath)
    {
        try
        {
            var resolvedPath = ResolveMediaUrl(filePath);
            var duration = await _jsRuntime.InvokeAsync<double>("audioService.getAudioDuration", resolvedPath);
            return duration;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting duration for {filePath}");
            return 0;
        }
    }

    public async Task<double> GetAudioDurationAsync(byte[] audioData)
    {
        try
        {
            var duration = await _jsRuntime.InvokeAsync<double>("audioService.getAudioDurationFromBytes", audioData);
            return duration;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting duration from byte array");
            return 0;
        }
    }

    public List<string> GetSupportedFormats()
    {
        return new List<string> { "mp3", "wav", "ogg", "aac", "m4a" };
    }

    public bool IsFormatSupported(string format)
    {
        return GetSupportedFormats().Contains(format.ToLower());
    }

    private class AudioInstance
    {
        public string SoundId { get; }
        public string FilePath { get; }
        public DateTime StartedAt { get; }

        public AudioInstance(string soundId, string filePath)
        {
            SoundId = soundId;
            FilePath = filePath;
            StartedAt = DateTime.UtcNow;
        }
    }

    private string ResolveMediaUrl(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        // Normalize slashes
        path = path.Replace('\\', '/');

        // If already absolute http(s), return as is
        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute) && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return path;
        }
        // If looks like a direct file path under media, try to extract id from the filename suffix: name-<32hex>.<ext>
        // Examples to normalize to /media/<id>:
        //   media/<categoryId>/name-<id>.mp3
        //   /media/<categoryId>/name-<id>.mp3
        //   <anything>/name-<id>.mp3
        var toInspect = path;
        if (toInspect.StartsWith("/media/", StringComparison.OrdinalIgnoreCase))
        {
            // Handle two-segment media path
            var parts = toInspect.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3) // media, categoryId, file
            {
                toInspect = parts[^1];
            }
        }

        var lastSlash = toInspect.LastIndexOf('/') + 1;
        var fileNameOnly = lastSlash > 0 && lastSlash < toInspect.Length ? toInspect.Substring(lastSlash) : toInspect;
        var dotIndex2 = fileNameOnly.LastIndexOf('.');
        if (dotIndex2 > 0)
        {
            var nameWithoutExt = fileNameOnly.Substring(0, dotIndex2);
            var dashIndex = nameWithoutExt.LastIndexOf('-');
            if (dashIndex > 0 && dashIndex + 1 < nameWithoutExt.Length)
            {
                var candidateId = nameWithoutExt.Substring(dashIndex + 1);
                if (candidateId.Length == 32 && IsHex(candidateId))
                {
                    path = "/media/" + candidateId;
                }
            }
        }

        // Ensure leading slash for known media route
        if (path.StartsWith("media/", StringComparison.OrdinalIgnoreCase))
        {
            path = "/" + path;
        }

        // If relative (e.g. "/media/{id}"), combine with HttpClient BaseAddress if available
        if (_httpClient.BaseAddress != null)
        {
            var combined = new Uri(_httpClient.BaseAddress, path);
            return combined.ToString();
        }
        return path;
    }

    private static bool IsHex(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            var c = value[i];
            var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!isHex) return false;
        }
        return true;
    }

    [JSInvokable]
    public void NotifyAudioEnded(string soundId)
    {
        _logger.LogInformation($"Audio ended notification received for: {soundId}");
        
        if (CurrentPlayingSoundId == soundId)
        {
            CurrentPlayingSoundId = null;
            _logger.LogInformation($"Cleared current playing sound: {soundId}");
        }
        
        if (_activeAudios.ContainsKey(soundId))
        {
            _activeAudios.Remove(soundId);
        }
        
        AudioEnded?.Invoke(this, soundId);
        _logger.LogInformation($"AudioEnded event fired for: {soundId}");
    }

    public bool IsPlaying(string soundId)
    {
        return CurrentPlayingSoundId == soundId;
    }
}
