using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using System.Text.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PrintDesktopClient.Services
{
    public class ProfileSettings
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;

        // MQTT
        public string MqttBroker { get; set; } = "mqttserver.test";
        public string MqttUsername { get; set; } = "mqttuser";
        public string MqttTopic { get; set; } = "home/printer/print";
        public int ReconnectIntervalSeconds { get; set; } = 10;
        public string SelectedPrinter { get; set; } = string.Empty;
        public bool ShowPrintPreview { get; set; } = false;

        // Cloud API
        public string ApiBaseUrl { get; set; } = "https://localhost:5001";
        public string DeviceAccountId { get; set; } = string.Empty;

        // Device Identity — generated once per profile, never overwritten
        public string DeviceGuid { get; set; } = string.Empty;
    }

    public class UserSettings
    {
        public List<ProfileSettings> Profiles { get; set; } = new List<ProfileSettings>();
        public string SelectedProfileId { get; set; } = string.Empty;

        // Legacy properties for migration:
        public string MqttBroker { get; set; }
        public string MqttUsername { get; set; }
        public string MqttTopic { get; set; }
        public int? ReconnectIntervalSeconds { get; set; }
        public string SelectedPrinter { get; set; }
        public bool? ShowPrintPreview { get; set; }
        public string ApiBaseUrl { get; set; }
        public string DeviceAccountId { get; set; }
        public string DeviceGuid { get; set; }
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
        }

        // ── Profiles Management ────────────────────────────────────────────────

        public List<ProfileSettings> Profiles => _userSettings.Profiles;

        public string SelectedProfileId
        {
            get => _userSettings.SelectedProfileId;
            set { _userSettings.SelectedProfileId = value; SaveSettings(); }
        }

        public ProfileSettings SelectedProfile
        {
            get
            {
                var profile = _userSettings.Profiles.FirstOrDefault(p => p.Id == _userSettings.SelectedProfileId);
                if (profile == null)
                {
                    profile = _userSettings.Profiles.FirstOrDefault();
                    if (profile == null)
                    {
                        profile = CreateDefaultProfile();
                        _userSettings.Profiles.Add(profile);
                    }
                    _userSettings.SelectedProfileId = profile.Id;
                    SaveSettings();
                }
                return profile;
            }
        }

        public void SaveSettings()
        {
            var json = JsonSerializer.Serialize(_userSettings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }

        // ── Secure Secret Storage (DPAPI) ─────────────────────────────────────

        public string GetMqttPassword(string profileId)         => LoadSecret($"mqtt_{profileId}.dat", $"mqtt_entropy_{profileId}.bin", "mqttpass");
        public void   SaveMqttPassword(string profileId, string p) => SaveSecret($"mqtt_{profileId}.dat", $"mqtt_entropy_{profileId}.bin", p);

        public string GetApiSecret(string profileId)            => LoadSecret($"api_secret_{profileId}.dat", $"api_secret_entropy_{profileId}.bin", string.Empty);
        public void   SaveApiSecret(string profileId, string s)   => SaveSecret($"api_secret_{profileId}.dat", $"api_secret_entropy_{profileId}.bin", s);

        // ── Wipe Credentials (Revocation) ─────────────────────────────────────

        public void WipeCredentials(string profileId)
        {
            var p = _userSettings.Profiles.FirstOrDefault(x => x.Id == profileId);
            if (p != null)
            {
                p.DeviceAccountId = string.Empty;
                p.DeviceGuid = Guid.NewGuid().ToString(); // Reset GUID
            }
            DeleteSecretFile($"mqtt_{profileId}.dat");
            DeleteSecretFile($"mqtt_entropy_{profileId}.bin");
            DeleteSecretFile($"api_secret_{profileId}.dat");
            DeleteSecretFile($"api_secret_entropy_{profileId}.bin");
            SaveSettings();
        }

        // ── Persist / Load & Migration ────────────────────────────────────────

        private UserSettings LoadSettings()
        {
            UserSettings settings = null;
            if (File.Exists(_settingsPath))
            {
                try
                {
                    var json = File.ReadAllText(_settingsPath);
                    settings = JsonSerializer.Deserialize<UserSettings>(json);
                }
                catch { }
            }

            if (settings == null)
            {
                settings = CreateDefaultSettings();
            }

            MigrateLegacySettings(settings);
            return settings;
        }

        private ProfileSettings CreateDefaultProfile() => new()
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Default",
            IsEnabled = true,
            MqttBroker = _configuration["Mqtt:Broker"] ?? "mqttserver.test",
            MqttUsername = _configuration["Mqtt:Username"] ?? "mqttuser",
            MqttTopic = _configuration["Mqtt:Topic"] ?? "home/printer/print",
            ReconnectIntervalSeconds = 10,
            ApiBaseUrl = _configuration["Api:BaseUrl"] ?? "https://localhost:5001",
            DeviceGuid = Guid.NewGuid().ToString()
        };

        private UserSettings CreateDefaultSettings()
        {
            var settings = new UserSettings();
            var defaultProfile = CreateDefaultProfile();
            settings.Profiles.Add(defaultProfile);
            settings.SelectedProfileId = defaultProfile.Id;
            return settings;
        }

        private void MigrateLegacySettings(UserSettings settings)
        {
            if (settings.Profiles == null)
            {
                settings.Profiles = new List<ProfileSettings>();
            }

            if (settings.Profiles.Count == 0 && (!string.IsNullOrEmpty(settings.ApiBaseUrl) || !string.IsNullOrEmpty(settings.MqttBroker)))
            {
                // Migrate legacy settings to a single profile
                var legacyProfile = new ProfileSettings
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "Default",
                    IsEnabled = true,
                    MqttBroker = settings.MqttBroker ?? "mqttserver.test",
                    MqttUsername = settings.MqttUsername ?? "mqttuser",
                    MqttTopic = settings.MqttTopic ?? "home/printer/print",
                    ReconnectIntervalSeconds = settings.ReconnectIntervalSeconds ?? 10,
                    SelectedPrinter = settings.SelectedPrinter ?? string.Empty,
                    ShowPrintPreview = settings.ShowPrintPreview ?? false,
                    ApiBaseUrl = settings.ApiBaseUrl ?? "https://localhost:5001",
                    DeviceAccountId = settings.DeviceAccountId ?? string.Empty,
                    DeviceGuid = settings.DeviceGuid ?? Guid.NewGuid().ToString()
                };

                settings.Profiles.Add(legacyProfile);
                settings.SelectedProfileId = legacyProfile.Id;

                // Move secret files
                MigrateSecretFile("mqtt.dat", $"mqtt_{legacyProfile.Id}.dat");
                MigrateSecretFile("mqtt_entropy.bin", $"mqtt_entropy_{legacyProfile.Id}.bin");
                MigrateSecretFile("api_secret.dat", $"api_secret_{legacyProfile.Id}.dat");
                MigrateSecretFile("api_secret_entropy.bin", $"api_secret_entropy_{legacyProfile.Id}.bin");

                _userSettings = settings;
                SaveSettings();
            }
            else if (settings.Profiles.Count == 0)
            {
                var defaultProfile = CreateDefaultProfile();
                settings.Profiles.Add(defaultProfile);
                settings.SelectedProfileId = defaultProfile.Id;
                _userSettings = settings;
                SaveSettings();
            }

            // Ensure all profiles have IDs and device GUIDs
            bool changed = false;
            foreach (var p in settings.Profiles)
            {
                if (string.IsNullOrEmpty(p.Id))
                {
                    p.Id = Guid.NewGuid().ToString();
                    changed = true;
                }
                if (string.IsNullOrEmpty(p.DeviceGuid))
                {
                    p.DeviceGuid = Guid.NewGuid().ToString();
                    changed = true;
                }
            }
            if (changed)
            {
                _userSettings = settings;
                SaveSettings();
            }
        }

        private void MigrateSecretFile(string oldName, string newName)
        {
            var oldPath = Path.Combine(_dataDir, oldName);
            var newPath = Path.Combine(_dataDir, newName);
            if (File.Exists(oldPath) && !File.Exists(newPath))
            {
                try { File.Move(oldPath, newPath); } catch { }
            }
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
