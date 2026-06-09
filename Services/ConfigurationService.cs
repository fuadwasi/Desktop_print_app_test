using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using System.Text.Json;
using System;

namespace PrintDesktopClient.Services
{
    public class UserSettings
    {
        // MQTT
        public string MqttBroker { get; set; } = "mqttserver.test";
        public string MqttUsername { get; set; } = "mqttuser";
        public string MqttTopic { get; set; } = "home/printer/print";
        public int ReconnectIntervalSeconds { get; set; } = 10;
        public string SelectedPrinter { get; set; } = string.Empty;

        // Cloud API
        public string ApiBaseUrl { get; set; } = "https://localhost:5001";
        public string DeviceAccountId { get; set; } = string.Empty;

        // Device Identity — generated once, never overwritten
        public string DeviceGuid { get; set; } = string.Empty;
    }

    public class ConfigurationService
    {
        private readonly IConfiguration _configuration;
        private readonly string _settingsPath;
        private readonly string _dataDir;
        private UserSettings _userSettings;

        public ConfigurationService(IConfiguration configuration)
        {
            _configuration = configuration;
            _dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrintDesktopClient");
            if (!Directory.Exists(_dataDir)) Directory.CreateDirectory(_dataDir);

            _settingsPath = Path.Combine(_dataDir, "settings.json");

            _userSettings = LoadSettings();

            // Ensure DeviceGuid is set exactly once and persisted
            if (string.IsNullOrEmpty(_userSettings.DeviceGuid))
            {
                _userSettings.DeviceGuid = Guid.NewGuid().ToString();
                SaveSettings();
            }
        }

        // ── MQTT ──────────────────────────────────────────────────────────────

        public string MqttBroker
        {
            get => _userSettings.MqttBroker;
            set { _userSettings.MqttBroker = value; SaveSettings(); }
        }

        public string MqttUsername
        {
            get => _userSettings.MqttUsername;
            set { _userSettings.MqttUsername = value; SaveSettings(); }
        }

        public string MqttTopic
        {
            get => _userSettings.MqttTopic;
            set { _userSettings.MqttTopic = value; SaveSettings(); }
        }

        public int ReconnectIntervalSeconds
        {
            get => _userSettings.ReconnectIntervalSeconds;
            set { _userSettings.ReconnectIntervalSeconds = value; SaveSettings(); }
        }

        public string SelectedPrinter
        {
            get => _userSettings.SelectedPrinter;
            set { _userSettings.SelectedPrinter = value; SaveSettings(); }
        }

        // ── Cloud API ─────────────────────────────────────────────────────────

        public string ApiBaseUrl
        {
            get => _userSettings.ApiBaseUrl;
            set { _userSettings.ApiBaseUrl = value; SaveSettings(); }
        }

        public string DeviceAccountId
        {
            get => _userSettings.DeviceAccountId;
            set { _userSettings.DeviceAccountId = value; SaveSettings(); }
        }

        /// <summary>Immutable after first generation.</summary>
        public string DeviceGuid => _userSettings.DeviceGuid;

        // ── Secure Secret Storage (DPAPI) ─────────────────────────────────────

        public string GetMqttPassword()         => LoadSecret("mqtt.dat", "mqtt_entropy.bin", "mqttpass");
        public void   SaveMqttPassword(string p) => SaveSecret("mqtt.dat", "mqtt_entropy.bin", p);

        public string GetApiSecret()            => LoadSecret("api_secret.dat", "api_secret_entropy.bin", string.Empty);
        public void   SaveApiSecret(string s)   => SaveSecret("api_secret.dat", "api_secret_entropy.bin", s);

        // ── Wipe Credentials (Revocation) ─────────────────────────────────────

        public void WipeCredentials()
        {
            _userSettings.DeviceAccountId = string.Empty;
            _userSettings.DeviceGuid = string.Empty; // Force new GUID on next boot
            DeleteSecretFile("mqtt.dat");
            DeleteSecretFile("mqtt_entropy.bin");
            DeleteSecretFile("api_secret.dat");
            DeleteSecretFile("api_secret_entropy.bin");
            SaveSettings();
        }

        // ── Persist / Load ────────────────────────────────────────────────────

        private UserSettings LoadSettings()
        {
            if (File.Exists(_settingsPath))
            {
                try
                {
                    var json = File.ReadAllText(_settingsPath);
                    return JsonSerializer.Deserialize<UserSettings>(json) ?? CreateDefaultSettings();
                }
                catch { return CreateDefaultSettings(); }
            }
            return CreateDefaultSettings();
        }

        private UserSettings CreateDefaultSettings() => new()
        {
            MqttBroker = _configuration["Mqtt:Broker"] ?? "mqttserver.test",
            MqttUsername = _configuration["Mqtt:Username"] ?? "mqttuser",
            MqttTopic = _configuration["Mqtt:Topic"] ?? "home/printer/print",
            ApiBaseUrl = _configuration["Api:BaseUrl"] ?? "https://localhost:5001"
        };

        private void SaveSettings()
        {
            var json = JsonSerializer.Serialize(_userSettings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }

        // ── DPAPI helpers ─────────────────────────────────────────────────────

        private string LoadSecret(string dataFile, string entropyFile, string fallback)
        {
            var path = Path.Combine(_dataDir, dataFile);
            var entropyPath = Path.Combine(_dataDir, entropyFile);
            if (!File.Exists(path)) return fallback;

            try
            {
                var encrypted = File.ReadAllBytes(path);
                var entropy   = File.Exists(entropyPath) ? File.ReadAllBytes(entropyPath) : null;
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted, entropy, DataProtectionScope.CurrentUser));
            }
            catch { return fallback; }
        }

        private void SaveSecret(string dataFile, string entropyFile, string value)
        {
            var data    = Encoding.UTF8.GetBytes(value);
            var entropy = new byte[16];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(entropy);

            var encrypted = ProtectedData.Protect(data, entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(Path.Combine(_dataDir, dataFile),    encrypted);
            File.WriteAllBytes(Path.Combine(_dataDir, entropyFile), entropy);
        }

        private void DeleteSecretFile(string fileName)
        {
            var path = Path.Combine(_dataDir, fileName);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
