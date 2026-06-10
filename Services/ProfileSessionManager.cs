using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PrintDesktopClient.Services
{
    public class ProfileSession
    {
        public ProfileSettings Profile { get; }
        public ApiService ApiService { get; }
        public MqttListenerService MqttService { get; }
        public string MqttStatus { get; set; } = "Disconnected";

        public ProfileSession(ProfileSettings profile, ApiService apiService, MqttListenerService mqttService)
        {
            Profile = profile;
            ApiService = apiService;
            MqttService = mqttService;
        }
    }

    public class ProfileSessionManager
    {
        private readonly ConfigurationService _config;
        private readonly IHttpClientFactory _httpFactory;
        private readonly PrinterService _printerService;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<ProfileSessionManager> _logger;
        private readonly ConcurrentDictionary<string, ProfileSession> _sessions = new();

        public event Action<ProfileSession>? SessionAdded;
        public event Action<ProfileSession>? SessionRemoved;
        public event Action<ProfileSession, string>? SessionMqttStatusChanged;

        public ProfileSessionManager(
            ConfigurationService config,
            IHttpClientFactory httpFactory,
            PrinterService printerService,
            ILoggerFactory loggerFactory)
        {
            _config = config;
            _httpFactory = httpFactory;
            _printerService = printerService;
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<ProfileSessionManager>();
        }

        public IReadOnlyCollection<ProfileSession> Sessions => _sessions.Values.ToList();

        public async Task StartAllAsync()
        {
            _logger.LogInformation("Starting all enabled profile sessions...");
            foreach (var profile in _config.Profiles)
            {
                if (profile.IsEnabled)
                {
                    await StartSessionAsync(profile);
                }
            }
        }

        public async Task StopAllAsync()
        {
            _logger.LogInformation("Stopping all active profile sessions...");
            foreach (var session in _sessions.Values)
            {
                await StopSessionInternalAsync(session);
            }
            _sessions.Clear();
        }

        public async Task StartSessionAsync(ProfileSettings profile)
        {
            if (_sessions.ContainsKey(profile.Id))
            {
                await RestartSessionAsync(profile);
                return;
            }

            _logger.LogInformation("Creating session for profile: {ProfileName} (Id: {ProfileId})", profile.Name, profile.Id);

            var apiLogger = _loggerFactory.CreateLogger<ApiService>();
            var mqttLogger = _loggerFactory.CreateLogger<MqttListenerService>();

            var apiService = new ApiService(_httpFactory, _config, profile, apiLogger);
            var mqttService = new MqttListenerService(_config, profile, _printerService, mqttLogger);

            var session = new ProfileSession(profile, apiService, mqttService);

            mqttService.StatusChanged += status =>
            {
                session.MqttStatus = status;
                SessionMqttStatusChanged?.Invoke(session, status);
            };

            _sessions[profile.Id] = session;
            SessionAdded?.Invoke(session);

            try
            {
                await mqttService.InitializeClientAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize MQTT for profile: {ProfileName}", profile.Name);
            }
        }

        public async Task StopSessionAsync(string profileId)
        {
            if (_sessions.TryRemove(profileId, out var session))
            {
                await StopSessionInternalAsync(session);
                SessionRemoved?.Invoke(session);
            }
        }

        private async Task StopSessionInternalAsync(ProfileSession session)
        {
            _logger.LogInformation("Stopping session for profile: {ProfileName}", session.Profile.Name);
            try
            {
                await session.MqttService.StopAsync(default);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while stopping MQTT client for profile: {ProfileName}", session.Profile.Name);
            }
        }

        public async Task RestartSessionAsync(ProfileSettings profile)
        {
            _logger.LogInformation("Restarting session for profile: {ProfileName}", profile.Name);
            await StopSessionAsync(profile.Id);
            if (profile.IsEnabled)
            {
                await StartSessionAsync(profile);
            }
        }

        public ProfileSession? GetSession(string profileId)
        {
            _sessions.TryGetValue(profileId, out var session);
            return session;
        }
    }
}
