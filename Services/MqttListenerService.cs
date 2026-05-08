using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PrintDesktopClient.Services
{
    public class MqttListenerService : IHostedService
    {
        private readonly ConfigurationService _config;
        private readonly PrinterService _printerService;
        private readonly ILogger<MqttListenerService> _logger;
        private IManagedMqttClient? _mqttClient;

        public event Action<string>? OnMessageReceived;
        public event Action<string>? StatusChanged;

        public MqttListenerService(ConfigurationService config, PrinterService printerService, ILogger<MqttListenerService> logger)
        {
            _config = config;
            _printerService = printerService;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await InitializeClientAsync();
        }

        public async Task InitializeClientAsync()
        {
            if (_mqttClient != null)
            {
                await _mqttClient.StopAsync();
            }

            var mqttFactory = new MqttFactory();
            _mqttClient = mqttFactory.CreateManagedMqttClient();

            var clientOptions = new MqttClientOptionsBuilder()
                .WithClientId("PrintDesktopClient_" + Guid.NewGuid())
                .WithTcpServer(_config.MqttBroker)
                .WithCredentials(_config.MqttUsername, _config.GetMqttPassword())
                .WithCleanSession()
                .Build();

            var options = new ManagedMqttClientOptionsBuilder()
                .WithAutoReconnectDelay(TimeSpan.FromMinutes(_config.ReconnectIntervalMinutes))
                .WithClientOptions(clientOptions)
                .Build();

            _mqttClient.ConnectedAsync += e => {
                StatusChanged?.Invoke("Connected");
                _logger.LogInformation("MQTT Connected");
                return Task.CompletedTask;
            };

            _mqttClient.DisconnectedAsync += e => {
                StatusChanged?.Invoke("Disconnected");
                _logger.LogWarning("MQTT Disconnected");
                return Task.CompletedTask;
            };

            _mqttClient.ApplicationMessageReceivedAsync += e =>
            {
                var payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
                _logger.LogInformation("MQTT Message Received: {Payload}", payload);
                
                OnMessageReceived?.Invoke(payload);
                
                if (!string.IsNullOrEmpty(_config.SelectedPrinter))
                {
                    _printerService.PrintText(payload, _config.SelectedPrinter);
                }
                else
                {
                    _logger.LogWarning("MQTT Message received but no printer selected.");
                }
                
                return Task.CompletedTask;
            };

            await _mqttClient.SubscribeAsync(_config.MqttTopic);
            await _mqttClient.StartAsync(options);
        }

        public async Task ManualConnectAsync()
        {
            _logger.LogInformation("Manual MQTT connection triggered.");
            if (_mqttClient != null)
            {
                // Managed client StartAsync ensures it's trying to connect
                await _mqttClient.StartAsync(_mqttClient.Options);
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_mqttClient != null)
            {
                await _mqttClient.StopAsync();
            }
        }
    }
}
