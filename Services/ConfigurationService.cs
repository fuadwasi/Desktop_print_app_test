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
        public string MqttBroker { get; set; } = "mqttserver.test";
        public string MqttUsername { get; set; } = "mqttuser";
        public string MqttTopic { get; set; } = "home/printer/print";
        public int ReconnectIntervalMinutes { get; set; } = 1;
        public string SelectedPrinter { get; set; } = string.Empty;
    }

    public class ConfigurationService
    {
        private readonly IConfiguration _configuration;
        private readonly string _settingsPath;
        private readonly string _entropyPath;
        private UserSettings _userSettings;

        public ConfigurationService(IConfiguration configuration)
        {
            _configuration = configuration;
            var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrintDesktopClient");
            if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);

            _settingsPath = Path.Combine(dataDir, "settings.json");
            _entropyPath = Path.Combine(dataDir, "entropy.bin");

            _userSettings = LoadSettings();
        }

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

        public int ReconnectIntervalMinutes 
        { 
            get => _userSettings.ReconnectIntervalMinutes; 
            set { _userSettings.ReconnectIntervalMinutes = value; SaveSettings(); } 
        }

        public string SelectedPrinter 
        { 
            get => _userSettings.SelectedPrinter; 
            set { _userSettings.SelectedPrinter = value; SaveSettings(); } 
        }

        private UserSettings LoadSettings()
        {
            if (File.Exists(_settingsPath))
            {
                try
                {
                    var json = File.ReadAllText(_settingsPath);
                    return JsonSerializer.Deserialize<UserSettings>(json) ?? CreateDefaultSettings();
                }
                catch
                {
                    return CreateDefaultSettings();
                }
            }
            return CreateDefaultSettings();
        }

        private UserSettings CreateDefaultSettings()
        {
            return new UserSettings
            {
                MqttBroker = _configuration["Mqtt:Broker"] ?? "mqttserver.test",
                MqttUsername = _configuration["Mqtt:Username"] ?? "mqttuser",
                MqttTopic = _configuration["Mqtt:Topic"] ?? "home/printer/print"
            };
        }

        private void SaveSettings()
        {
            var json = JsonSerializer.Serialize(_userSettings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }

        public string GetMqttPassword()
        {
            var path = GetPasswordPath();
            if (!File.Exists(path)) return "mqttpass";

            try
            {
                var encryptedData = File.ReadAllBytes(path);
                var entropy = File.Exists(_entropyPath) ? File.ReadAllBytes(_entropyPath) : null;
                var decryptedData = ProtectedData.Unprotect(encryptedData, entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decryptedData);
            }
            catch
            {
                return "mqttpass";
            }
        }

        public void SaveMqttPassword(string password)
        {
            var data = Encoding.UTF8.GetBytes(password);
            var entropy = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(entropy);
            }
            
            var encryptedData = ProtectedData.Protect(data, entropy, DataProtectionScope.CurrentUser);
            
            var dir = Path.GetDirectoryName(GetPasswordPath());
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
            
            File.WriteAllBytes(GetPasswordPath(), encryptedData);
            File.WriteAllBytes(_entropyPath, entropy);
        }

        private string GetPasswordPath() => 
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrintDesktopClient", "mqtt.dat");
    }
}
