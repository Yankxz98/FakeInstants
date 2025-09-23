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

    public AudioService(IJSRuntime jsRuntime, ILogger<AudioService> logger, HttpClient httpClient)
    {
        _jsRuntime = jsRuntime;
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<bool> PlaySoundAsync(Sound sound)
    {
        try
        {
            // Get duration if not already calculated
            if (sound.Duration <= 0)
            {
                try
                {
                    var audioData = await _httpClient.GetByteArrayAsync(sound.FilePath);
                    sound.Duration = await GetAudioDurationAsync(audioData);
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
            var audioInstance = new AudioInstance(sound.Id, sound.FilePath);
            _activeAudios[sound.Id] = audioInstance;

            // Initialize audio element via JavaScript
            await _jsRuntime.InvokeVoidAsync("audioService.initializeAudio", sound.Id, sound.FilePath, _masterVolume);

            // Play the audio
            await _jsRuntime.InvokeVoidAsync("audioService.playAudio", sound.Id);

            // Update sound statistics (this would be handled by SoundManager)
            sound.LastPlayed = DateTime.UtcNow;
            sound.PlayCount++;

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
                _activeAudios.Remove(soundId);
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
    }

    public async Task<double> GetAudioDurationAsync(string filePath)
    {
        try
        {
            var duration = await _jsRuntime.InvokeAsync<double>("audioService.getAudioDuration", filePath);
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
}
