using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PrintDesktopClient.Services
{
    // ── Command payload shapes ─────────────────────────────────────────────────

    public record MqttCommand(string Type, string JobId, string PrinterName);

    // ── Service ───────────────────────────────────────────────────────────────

    public class MqttListenerService : IHostedService
    {
        private readonly ConfigurationService _config;
        private readonly PrinterService _printerService;
        private readonly ILogger<MqttListenerService> _logger;
        private IManagedMqttClient? _mqttClient;

        // ── Events consumed by the ViewModel ──────────────────────────────────

        /// <summary>Raised for legacy plain-text messages (non-command payloads).</summary>
        public event Action<string>? OnMessageReceived;
        /// <summary>Raised when the MQTT connection state changes.</summary>
        public event Action<string>? StatusChanged;
        /// <summary>Raised when a print command is received. Args: jobId, printerName.</summary>
        public event Func<string, string, Task>? OnPrintCommand;
        /// <summary>Raised when a printer_sync command is received.</summary>
        public event Action? OnSyncCommand;
        /// <summary>Raised when a revoke command is received.</summary>
        public event Action? OnRevokeCommand;

        public MqttListenerService(ConfigurationService config, PrinterService printerService, ILogger<MqttListenerService> logger)
        {
            _config = config;
            _printerService = printerService;
            _logger = logger;
        }

        // ── IHostedService ────────────────────────────────────────────────────

        public async Task StartAsync(CancellationToken cancellationToken)
            => await InitializeClientAsync();

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_mqttClient != null)
                await _mqttClient.StopAsync();
        }

        // ── Core initialization ───────────────────────────────────────────────

        public async Task InitializeClientAsync()
        {
            _logger.LogInformation("Initializing/Restarting MQTT Client...");

            if (_mqttClient != null)
            {
                try { await _mqttClient.StopAsync(); }
                catch (Exception ex) { _logger.LogError(ex, "Error stopping MQTT client."); }
            }

            var mqttFactory = new MqttFactory();
            _mqttClient = mqttFactory.CreateManagedMqttClient();

            // ── LWT (Last Will and Testament) ─────────────────────────────────
            var deviceAccountId = _config.DeviceAccountId;
            var statusTopic = string.IsNullOrEmpty(deviceAccountId)
                ? "devices/unknown/status"
                : $"devices/{deviceAccountId}/status";

            var willPayload = Encoding.UTF8.GetBytes("{\"state\":\"offline\"}");

            var clientOptions = new MqttClientOptionsBuilder()
                .WithClientId("PrintDesktopClient_" + Guid.NewGuid())
                .WithTcpServer(_config.MqttBroker)
                .WithCredentials(_config.MqttUsername, _config.GetMqttPassword())
                .WithWillTopic(statusTopic)
                .WithWillPayload(willPayload)
                .WithWillQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                .WithWillRetain(true)
                .WithCleanSession()
                .Build();

            //var clientOptions = new MqttClientOptionsBuilder()
            //    .WithClientId("PrintDesktopClient_" + _config.DeviceAccountId)
            //    .WithTcpServer(_config.MqttBroker)
            //    .WithCredentials(_config.MqttUsername, _config.GetMqttPassword())
            //    .WithCleanSession()
            //    .Build();

            var options = new ManagedMqttClientOptionsBuilder()
                .WithAutoReconnectDelay(TimeSpan.FromMinutes(_config.ReconnectIntervalMinutes))
                .WithClientOptions(clientOptions)
                .Build();

            // ── Events ────────────────────────────────────────────────────────

            _mqttClient.ConnectedAsync += async e =>
            {
                StatusChanged?.Invoke("Connected");
                _logger.LogInformation("MQTT Connected");

                // Publish online status immediately on connect
                if (_mqttClient != null)
                {
                    var onlineMsg = new MqttApplicationMessageBuilder()
                        .WithTopic(statusTopic)
                        .WithPayload("{\"state\":\"online\"}")
                        .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                        .WithRetainFlag(true)
                        .Build();

                    await _mqttClient.EnqueueAsync(onlineMsg);
                }
            };

            _mqttClient.DisconnectedAsync += e =>
            {
                StatusChanged?.Invoke("Disconnected");
                _logger.LogWarning("MQTT Disconnected");
                return Task.CompletedTask;
            };

            _mqttClient.ApplicationMessageReceivedAsync += HandleMessageAsync;

            // ── Subscribe to the cloud command topic ──────────────────────────
            var commandTopic = string.IsNullOrEmpty(deviceAccountId)
                ? _config.MqttTopic                          // Fallback to legacy topic
                : $"devices/{deviceAccountId}/commands";

            await _mqttClient.SubscribeAsync(commandTopic);
            await _mqttClient.StartAsync(options);

            _logger.LogInformation("Subscribed to topic: {Topic}", commandTopic);
        }

        // ── Message routing ───────────────────────────────────────────────────

        private async Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs e)
        {
            var raw = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
            _logger.LogInformation("MQTT Message: {Payload}", raw);

            // Try to parse as a structured command first
            try
            {
                var cmd = JsonSerializer.Deserialize<MqttCommand>(raw,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (cmd != null)
                {
                    switch (cmd.Type.ToLowerInvariant())
                    {
                        case "print":
                            _logger.LogInformation("Print command received. JobId: {JobId}", cmd.JobId);
                            if (OnPrintCommand != null)
                                await OnPrintCommand.Invoke(cmd.JobId, cmd.PrinterName);
                            return;

                        case "printer_sync":
                            _logger.LogInformation("Printer sync command received.");
                            OnSyncCommand?.Invoke();
                            return;

                        case "revoke":
                            _logger.LogWarning("Revoke command received.");
                            OnRevokeCommand?.Invoke();
                            return;
                    }
                }
            }
            catch
            {
                // Not a JSON command — treat as plain-text message (legacy behaviour)
            }

            // Legacy plain-text fallback: print raw text
            OnMessageReceived?.Invoke(raw);

            if (!string.IsNullOrEmpty(_config.SelectedPrinter))
                _printerService.PrintText(raw, _config.SelectedPrinter);
            else
                _logger.LogWarning("MQTT text received but no printer selected.");
        }

        // ── Manual connect ───────────────────────────────────────────────────

        public async Task ManualConnectAsync() => await InitializeClientAsync();
    }
}
