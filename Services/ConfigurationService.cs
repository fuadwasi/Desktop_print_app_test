using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;
using System.IO;

namespace PrintDesktopClient.Services
{
    public class ConfigurationService
    {
        private readonly IConfiguration _configuration;
        private readonly string _entropyPath;

        public ConfigurationService(IConfiguration configuration)
        {
            _configuration = configuration;
            _entropyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrintDesktopClient", "entropy.bin");
        }

        public string MqttBroker => _configuration["Mqtt:Broker"] ?? "mqttserver.test";
        public string MqttUsername => _configuration["Mqtt:Username"] ?? "mqttuser";
        public string MqttTopic => _configuration["Mqtt:Topic"] ?? "home/printer/print";
        public string SelectedPrinter { get; set; } = string.Empty;

        public string GetMqttPassword()
        {
            var path = GetPasswordPath();
            if (!File.Exists(path)) return "mqttpass"; // Default for testing as per plan

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
