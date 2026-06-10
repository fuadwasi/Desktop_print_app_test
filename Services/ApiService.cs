using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PrintDesktopClient.Services
{
    // ── Request / Response DTOs ───────────────────────────────────────────────
    // Note: C# 'record' types require System.Runtime.CompilerServices.IsExternalInit
    // which is not available on net48. Using regular classes instead.

    public class AuthenticateRequest
    {
        public string DeviceAccountId { get; set; } = string.Empty;
        public string ApiSecret       { get; set; } = string.Empty;
        public string DeviceGuid      { get; set; } = string.Empty;

        public AuthenticateRequest(string deviceAccountId, string apiSecret, string deviceGuid)
        {
            DeviceAccountId = deviceAccountId;
            ApiSecret       = apiSecret;
            DeviceGuid      = deviceGuid;
        }
    }

    public class SyncPrintersRequest
    {
        public string       DeviceGuid { get; set; } = string.Empty;
        public List<string> Printers   { get; set; } = new List<string>();

        public SyncPrintersRequest(string deviceGuid, List<string> printers)
        {
            DeviceGuid = deviceGuid;
            Printers   = printers;
        }
    }

    public class JobStatusRequest
    {
        public int    Status       { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;

        public JobStatusRequest(int status, string errorMessage)
        {
            Status       = status;
            ErrorMessage = errorMessage;
        }
    }

    // Make sure these integer values match the PrintJobStatus enum in your API project.
    public enum PrintJobStatus
    {
        Queued      = 1,
        Signaled    = 2,
        Downloading = 3,
        Printed     = 4,
        Failed      = 5,
        Canceled      = 5
    }

    // ── Service ───────────────────────────────────────────────────────────────

    public class ApiService
    {
        private readonly IHttpClientFactory    _httpFactory;
        private readonly ConfigurationService  _config;
        private readonly ProfileSettings       _profile;
        private readonly ILogger<ApiService>   _logger;

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public ApiService(IHttpClientFactory httpFactory, ConfigurationService config, ProfileSettings profile, ILogger<ApiService> logger)
        {
            _httpFactory = httpFactory;
            _config      = config;
            _profile     = profile;
            _logger      = logger;
        }

        // Expose profile settings for status checking
        public ProfileSettings Profile => _profile;

        // ── Helpers ───────────────────────────────────────────────────────────

        private HttpClient CreateClient()
        {
            var client = _httpFactory.CreateClient("PrintAgent");
            client.BaseAddress = new Uri(_profile.ApiBaseUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Remove("X-Device-Id");
            client.DefaultRequestHeaders.Remove("X-Api-Secret");
            client.DefaultRequestHeaders.Add("X-Device-Id",  _profile.DeviceAccountId);
            client.DefaultRequestHeaders.Add("X-Api-Secret", _config.GetApiSecret(_profile.Id));
            return client;
        }

        /// <summary>
        /// Serializes <paramref name="payload"/> to JSON and POSTs it.
        /// Replaces System.Net.Http.Json.PostAsJsonAsync which is not available on net48.
        /// </summary>
        private static HttpContent ToJsonContent(object payload)
        {
            var json = JsonSerializer.Serialize(payload, _jsonOptions);
            return new StringContent(json, Encoding.UTF8, "application/json");
        }

        // ── POST /api/printagent/authenticate ─────────────────────────────────

        /// <returns>true on HTTP 200, false otherwise.</returns>
        public async Task<bool> AuthenticateAsync()
        {
            try
            {
                var client = _httpFactory.CreateClient("PrintAgent");
                client.BaseAddress = new Uri(_profile.ApiBaseUrl.TrimEnd('/') + "/");

                var body = new AuthenticateRequest(
                    _profile.DeviceAccountId,
                    _config.GetApiSecret(_profile.Id),
                    _profile.DeviceGuid);

                var response = await client.PostAsync("api/printagent/authenticate", ToJsonContent(body));

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("[Profile: {ProfileName}] Authentication successful.", _profile.Name);
                    return true;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    _logger.LogWarning("[Profile: {ProfileName}] Authentication returned 401 – triggering revocation.", _profile.Name);
                    OnUnauthorized?.Invoke();
                    return false;
                }

                _logger.LogWarning("[Profile: {ProfileName}] Authentication failed: {StatusCode}", _profile.Name, response.StatusCode);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Profile: {ProfileName}] Authentication HTTP call failed.", _profile.Name);
                return false;
            }
        }

        // ── POST /api/printagent/sync-printers ────────────────────────────────

        public async Task<bool> SyncPrintersAsync(List<string> printers)
        {
            try
            {
                var response = await CreateClient().PostAsync(
                    "api/printagent/sync-printers",
                    ToJsonContent(new SyncPrintersRequest(_profile.DeviceGuid, printers)));

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("[Profile: {ProfileName}] Printer sync successful.", _profile.Name);
                    return true;
                }

                await HandleUnauthorized(response);
                _logger.LogWarning("[Profile: {ProfileName}] Printer sync failed: {StatusCode}", _profile.Name, response.StatusCode);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Profile: {ProfileName}] SyncPrinters HTTP call failed.", _profile.Name);
                return false;
            }
        }

        // ── GET /api/printagent/jobs/{jobId}/download ─────────────────────────

        /// <returns>Raw PDF bytes, or null on failure.</returns>
        public async Task<byte[]?> DownloadJobAsync(string jobId)
        {
            try
            {
                var response = await CreateClient().GetAsync($"api/printagent/jobs/{jobId}/download");

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("[Profile: {ProfileName}] Job {JobId} downloaded successfully.", _profile.Name, jobId);
                    return await response.Content.ReadAsByteArrayAsync();
                }

                await HandleUnauthorized(response);
                _logger.LogWarning("[Profile: {ProfileName}] Job download failed: {StatusCode}", _profile.Name, response.StatusCode);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Profile: {ProfileName}] DownloadJob HTTP call failed for job {JobId}.", _profile.Name, jobId);
                return null;
            }
        }

        // ── POST /api/printagent/jobs/{jobId}/status ──────────────────────────

        public async Task<bool> UpdateJobStatusAsync(string jobId, bool success, string errorMessage = "")
        {
            try
            {
                var status   = success ? (int)PrintJobStatus.Printed : (int)PrintJobStatus.Failed;
                var response = await CreateClient().PostAsync(
                    $"api/printagent/jobs/{jobId}/status",
                    ToJsonContent(new JobStatusRequest(status, errorMessage)));

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("[Profile: {ProfileName}] Job {JobId} status updated to {Status}.", _profile.Name, jobId, status);
                    return true;
                }

                await HandleUnauthorized(response);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Profile: {ProfileName}] UpdateJobStatus HTTP call failed for job {JobId}.", _profile.Name, jobId);
                return false;
            }
        }

        // ── Lightweight connectivity test ─────────────────────────────────────

        public async Task<bool> TestApiConnectionAsync()
        {
            try
            {
                var client = _httpFactory.CreateClient("PrintAgent");
                client.BaseAddress = new Uri(_profile.ApiBaseUrl.TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromSeconds(5);
                var response = await client.GetAsync("health");
                return response.IsSuccessStatusCode || (int)response.StatusCode < 500;
            }
            catch
            {
                return false;
            }
        }

        // ── 401 Revocation Hook ───────────────────────────────────────────────

        /// <summary>Raised when any HTTP response returns 401.</summary>
        public event Action? OnUnauthorized;

        private async Task HandleUnauthorized(HttpResponseMessage response)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("[Profile: {ProfileName}] Received 401 – triggering revocation.", _profile.Name);
                OnUnauthorized?.Invoke();
            }
            await Task.CompletedTask;
        }
    }
}
