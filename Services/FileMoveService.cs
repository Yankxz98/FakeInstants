using System.Net.Http.Json;
using fakeinstants.Models;

namespace fakeinstants.Services;

public class FileMoveService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FileMoveService> _logger;

    public FileMoveService(HttpClient httpClient, ILogger<FileMoveService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public class FileMoveResult
    {
        public string SoundId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class MoveOperationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<FileMoveResult> FileResults { get; set; } = new();
    }

    /// <summary>
    /// Moves multiple files to their respective categories
    /// </summary>
    public async Task<MoveOperationResult> MoveFilesToCategoriesAsync(List<Sound> sounds)
    {
        var result = new MoveOperationResult();

        try
        {
            _logger.LogInformation("FileMoveService.MoveFilesToCategoriesAsync() - Starting to move {Count} files", sounds.Count);

            if (sounds == null || sounds.Count == 0)
            {
                result.Success = false;
                result.Message = "Nenhum som fornecido para mover";
                return result;
            }

            // Create move requests
            var moveRequests = sounds.Select(sound => new MoveFileRequest
            {
                SoundId = sound.Id,
                TargetCategory = sound.CategoryId
            }).ToList();

            // Call the server endpoint
            var response = await _httpClient.PostAsJsonAsync("/media/move-files", moveRequests);

            if (!response.IsSuccessStatusCode)
            {
                result.Success = false;
                result.Message = $"Falha na requisição: {response.StatusCode}";
                _logger.LogError("FileMoveService.MoveFilesToCategoriesAsync() - Request failed: {StatusCode}", response.StatusCode);
                return result;
            }

            // Parse response
            var serverResponse = await response.Content.ReadFromJsonAsync<ServerMoveResponse>();
            if (serverResponse == null)
            {
                result.Success = false;
                result.Message = "Resposta inválida do servidor";
                return result;
            }

            result.Success = true;
            result.Message = serverResponse.message;
            result.FileResults = serverResponse.results ?? new List<FileMoveResult>();

            var successCount = result.FileResults.Count(r => r.Success);
            _logger.LogInformation("FileMoveService.MoveFilesToCategoriesAsync() - Completed: {Success}/{Total} files moved successfully",
                successCount, result.FileResults.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FileMoveService.MoveFilesToCategoriesAsync() - Error moving files: {Message}", ex.Message);
            result.Success = false;
            result.Message = $"Erro: {ex.Message}";
            return result;
        }
    }

    /// <summary>
    /// Moves a single file to a specific category
    /// </summary>
    public async Task<FileMoveResult> MoveFileToCategoryAsync(string soundId, string targetCategoryId)
    {
        var result = new FileMoveResult { SoundId = soundId };

        try
        {
            _logger.LogInformation("FileMoveService.MoveFileToCategoryAsync() - Moving sound {SoundId} to category {CategoryId}",
                soundId, targetCategoryId);

            var moveRequests = new List<MoveFileRequest>
            {
                new MoveFileRequest
                {
                    SoundId = soundId,
                    TargetCategory = targetCategoryId
                }
            };

            var response = await _httpClient.PostAsJsonAsync("/media/move-files", moveRequests);

            if (!response.IsSuccessStatusCode)
            {
                result.Success = false;
                result.Message = $"Falha na requisição: {response.StatusCode}";
                return result;
            }

            var serverResponse = await response.Content.ReadFromJsonAsync<ServerMoveResponse>();
            if (serverResponse?.results == null || serverResponse.results.Count == 0)
            {
                result.Success = false;
                result.Message = "Resposta inválida do servidor";
                return result;
            }

            var fileResult = serverResponse.results[0];
            result.Success = fileResult.Success;
            result.Message = fileResult.Message;

            _logger.LogInformation("FileMoveService.MoveFileToCategoryAsync() - Result: {Success}, {Message}",
                result.Success, result.Message);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FileMoveService.MoveFileToCategoryAsync() - Error: {Message}", ex.Message);
            result.Success = false;
            result.Message = $"Erro: {ex.Message}";
            return result;
        }
    }

    // DTOs for server communication
    private class MoveFileRequest
    {
        public string SoundId { get; set; } = string.Empty;
        public string TargetCategory { get; set; } = string.Empty;
    }

    private class ServerMoveResponse
    {
        public string message { get; set; } = string.Empty;
        public List<FileMoveResult> results { get; set; } = new();
    }
}