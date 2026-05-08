using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;


namespace PrintDesktopClient.Services
{
    // ── Request / Response DTOs ───────────────────────────────────────────────

    public record AuthenticateRequest(string DeviceAccountId, string ApiSecret, string DeviceGuid);
    public record SyncPrintersRequest(string DeviceGuid, List<string> Printers);
    public record JobStatusRequest(string Status, string ErrorMessage);

    // ── Service ───────────────────────────────────────────────────────────────

    public class ApiService
    {
        private readonly IHttpClientFactory _httpFactory;
        private readonly ConfigurationService _config;
        private readonly ILogger<ApiService> _logger;

        public ApiService(IHttpClientFactory httpFactory, ConfigurationService config, ILogger<ApiService> logger)
        {
            _httpFactory = httpFactory;
            _config = config;
            _logger = logger;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private HttpClient CreateClient()
        {
            var client = _httpFactory.CreateClient("PrintAgent");
            client.BaseAddress = new Uri(_config.ApiBaseUrl.TrimEnd('/') + "/");
            // Inject auth headers on every request
            client.DefaultRequestHeaders.Remove("X-Device-Id");
            client.DefaultRequestHeaders.Remove("X-Api-Secret");
            client.DefaultRequestHeaders.Add("X-Device-Id",  _config.DeviceAccountId);
            client.DefaultRequestHeaders.Add("X-Api-Secret", _config.GetApiSecret());
            return client;
        }

        // ── POST /api/printagent/authenticate ─────────────────────────────────

        /// <returns>true on HTTP 200, false otherwise.</returns>
        public async Task<bool> AuthenticateAsync()
        {
            try
            {
                var client = _httpFactory.CreateClient("PrintAgent");
                client.BaseAddress = new Uri(_config.ApiBaseUrl.TrimEnd('/') + "/");

                var body = new AuthenticateRequest(
                    _config.DeviceAccountId,
                    _config.GetApiSecret(),
                    _config.DeviceGuid);

                var response = await client.PostAsJsonAsync("api/printagent/authenticate", body);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Authentication successful.");
                    return true;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    _logger.LogWarning("Authentication returned 401 – triggering revocation.");
                    OnUnauthorized?.Invoke();
                    return false;
                }

                _logger.LogWarning("Authentication failed: {StatusCode}", response.StatusCode);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Authentication HTTP call failed.");
                return false;
            }
        }

        // ── POST /api/printagent/sync-printers ────────────────────────────────

        public async Task<bool> SyncPrintersAsync(List<string> printers)
        {
            try
            {
                var response = await CreateClient().PostAsJsonAsync(
                    "api/printagent/sync-printers",
                    new SyncPrintersRequest(_config.DeviceGuid, printers));

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Printer sync successful.");
                    return true;
                }

                await HandleUnauthorized(response);
                _logger.LogWarning("Printer sync failed: {StatusCode}", response.StatusCode);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SyncPrinters HTTP call failed.");
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
                    _logger.LogInformation("Job {JobId} downloaded successfully.", jobId);
                    return await response.Content.ReadAsByteArrayAsync();
                }

                await HandleUnauthorized(response);
                _logger.LogWarning("Job download failed: {StatusCode}", response.StatusCode);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DownloadJob HTTP call failed for job {JobId}.", jobId);
                return null;
            }
        }

        // ── POST /api/printagent/jobs/{jobId}/status ──────────────────────────

        public async Task<bool> UpdateJobStatusAsync(string jobId, bool success, string errorMessage = "")
        {
            try
            {
                var status = success ? "Printed" : "Failed";
                var response = await CreateClient().PostAsJsonAsync(
                    $"api/printagent/jobs/{jobId}/status",
                    new JobStatusRequest(status, errorMessage));

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Job {JobId} status updated to {Status}.", jobId, status);
                    return true;
                }

                await HandleUnauthorized(response);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateJobStatus HTTP call failed for job {JobId}.", jobId);
                return false;
            }
        }

        // ── Lightweight connectivity test ─────────────────────────────────────

        public async Task<bool> TestApiConnectionAsync()
        {
            try
            {
                var client = _httpFactory.CreateClient("PrintAgent");
                client.BaseAddress = new Uri(_config.ApiBaseUrl.TrimEnd('/') + "/");
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
                _logger.LogWarning("Received 401 – triggering revocation.");
                OnUnauthorized?.Invoke();
            }
        }
    }
}
