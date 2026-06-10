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

    public class MqttCommand
    {
        public string Type        { get; set; } = string.Empty;
        public string JobId       { get; set; } = string.Empty;
        public string PrinterName { get; set; } = string.Empty;
    }

    // ── Service ───────────────────────────────────────────────────────────────

    public class MqttListenerService : IHostedService
    {
        private readonly ConfigurationService _config;
        private readonly ProfileSettings _profile;
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

        public MqttListenerService(ConfigurationService config, ProfileSettings profile, PrinterService printerService, ILogger<MqttListenerService> logger)
        {
            _config = config;
            _profile = profile;
            _printerService = printerService;
            _logger = logger;
        }

        public ProfileSettings Profile => _profile;

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
            _logger.LogInformation("[Profile: {ProfileName}] Initializing/Restarting MQTT Client...", _profile.Name);

            if (_mqttClient != null)
            {
                try { await _mqttClient.StopAsync(); }
                catch (Exception ex) { _logger.LogError(ex, "[Profile: {ProfileName}] Error stopping MQTT client.", _profile.Name); }
            }

            var mqttFactory = new MqttFactory();
            _mqttClient = mqttFactory.CreateManagedMqttClient();

            // ── LWT (Last Will and Testament) ─────────────────────────────────
            var deviceAccountId = _profile.DeviceAccountId;
            var statusTopic = string.IsNullOrEmpty(deviceAccountId)
                ? $"devices/unknown/{_profile.Id}/status"
                : $"devices/{deviceAccountId}/status";

            var willPayload = Encoding.UTF8.GetBytes("{\"state\":\"offline\"}");

            var clientOptions = new MqttClientOptionsBuilder()
                .WithClientId("PrintDesktopClient_" + _profile.Id + "_" + Guid.NewGuid().ToString().Substring(0, 8))
                .WithTcpServer(_profile.MqttBroker)
                .WithCredentials(_profile.MqttUsername, _config.GetMqttPassword(_profile.Id))
                .WithWillTopic(statusTopic)
                .WithWillPayload(willPayload)
                .WithWillQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                .WithWillRetain(true)
                .WithCleanSession()
                .Build();

            var options = new ManagedMqttClientOptionsBuilder()
                .WithAutoReconnectDelay(TimeSpan.FromSeconds(_profile.ReconnectIntervalSeconds))
                .WithClientOptions(clientOptions)
                .Build();

            // ── Events ────────────────────────────────────────────────────────

            _mqttClient.ConnectedAsync += async e =>
            {
                StatusChanged?.Invoke("Connected");
                _logger.LogInformation("[Profile: {ProfileName}] MQTT Connected", _profile.Name);

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
                _logger.LogWarning("[Profile: {ProfileName}] MQTT Disconnected", _profile.Name);
                return Task.CompletedTask;
            };

            _mqttClient.ApplicationMessageReceivedAsync += HandleMessageAsync;

            // ── Subscribe to the cloud command topic ──────────────────────────
            var commandTopic = string.IsNullOrEmpty(deviceAccountId)
                ? _profile.MqttTopic                          // Fallback to legacy topic
                : $"devices/{deviceAccountId}/commands";

            await _mqttClient.SubscribeAsync(commandTopic);
            await _mqttClient.StartAsync(options);

            _logger.LogInformation("[Profile: {ProfileName}] Subscribed to topic: {Topic}", _profile.Name, commandTopic);
        }

        // ── Message routing ───────────────────────────────────────────────────

        private async Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs e)
        {
            var seg = e.ApplicationMessage.PayloadSegment;
            var raw = Encoding.UTF8.GetString(seg.Array ?? new byte[0], seg.Offset, seg.Count);
            _logger.LogInformation("[Profile: {ProfileName}] MQTT Message: {Payload}", _profile.Name, raw);

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
                            _logger.LogInformation("[Profile: {ProfileName}] Print command received. JobId: {JobId}", _profile.Name, cmd.JobId);
                            if (OnPrintCommand != null)
                                await OnPrintCommand.Invoke(cmd.JobId, cmd.PrinterName);
                            return;

                        case "printer_sync":
                            _logger.LogInformation("[Profile: {ProfileName}] Printer sync command received.", _profile.Name);
                            OnSyncCommand?.Invoke();
                            return;

                        case "revoke":
                            _logger.LogWarning("[Profile: {ProfileName}] Revoke command received.", _profile.Name);
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

            if (!string.IsNullOrEmpty(_profile.SelectedPrinter))
                _printerService.PrintText(raw, _profile.SelectedPrinter);
            else
                _logger.LogWarning("[Profile: {ProfileName}] MQTT text received but no printer selected.", _profile.Name);
        }

        // ── Manual connect ───────────────────────────────────────────────────

        public async Task ManualConnectAsync() => await InitializeClientAsync();

        // ── Test Connection ──────────────────────────────────────────────────

        public static async Task<bool> TestConnectionAsync(string broker, string username, string password)
        {
            try
            {
                var factory = new MqttFactory();
                using (var client = factory.CreateMqttClient())
                {
                    var options = new MqttClientOptionsBuilder()
                        .WithTcpServer(broker)
                        .WithCredentials(username, password)
                        .Build();

                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                    {
                        await client.ConnectAsync(options, cts.Token);
                        if (client.IsConnected)
                        {
                            await client.DisconnectAsync();
                            return true;
                        }
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
