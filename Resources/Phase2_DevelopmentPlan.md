# Auto-Print Agent: Phase 2 Development Plan (Cloud & API Integration)

> **Context**: This plan outlines the steps to adapt the existing Auto-Print Desktop Client (currently built in WPF/.NET 9.0) to meet the new "Cloud-to-Ground Bridge" requirements specified in `AutoPrint_Desktop_Client_Requirements.md` and the `AutoPrintAgent-Postman-Collection.json`.

---

## Phase 1: Security, Identity, & Configuration Updates

### Task 1.1: Immutable Identity & Secure Storage
*   **Description**: The device needs a unique identity and secure credential storage.
*   **Actionable Items**:
    *   Update `ConfigurationService` to generate a `deviceGuid` (UUID) on first run and persist it.
    *   Add properties for `ApiBaseUrl`, `DeviceAccountId`, and `ApiSecret`.
    *   Ensure `ApiSecret` and `DeviceAccountId` are encrypted using DPAPI (already implemented for MQTT password).

### Task 1.2: Enhance Configuration UI
*   **Description**: Provide an interface for administrators to configure cloud settings.
*   **Actionable Items**:
    *   Add inputs to the Settings Tab in `MainWindow.xaml` for `API Base URL`, `Device Account ID`, and `API Secret`.
    *   Implement a "Test API" button that validates the API Base URL by attempting to reach it.
    *   Add a "Sync Printers to Cloud" manual trigger button.

---

## Phase 2: HTTP Client & Authentication Flow

### Task 2.1: Implement ApiService
*   **Description**: Build a typed HTTP client to communicate with the cloud orchestrator.
*   **Actionable Items**:
    *   Register `HttpClient` via `IHttpClientFactory` in `App.xaml.cs`.
    *   Create `ApiService` to handle the endpoints defined in the Postman collection.
    *   Ensure standard headers (`X-Device-Id`, `X-Api-Secret`) are injected into requests where required.

### Task 2.2: The Authentication Bridge
*   **Description**: Authenticate with the cloud before connecting to MQTT.
*   **Actionable Items**:
    *   Implement `POST /api/printagent/authenticate` with `{deviceAccountId, apiSecret, deviceGuid}`.
    *   Modify application startup: Only initialize `MqttListenerService` *after* a successful HTTP 200 OK from the authentication endpoint.

---

## Phase 3: Advanced MQTT Integration

### Task 3.1: Command Subscription
*   **Description**: Listen for targeted cloud commands rather than generic topics.
*   **Actionable Items**:
    *   Update `MqttListenerService` to subscribe dynamically to `devices/{DeviceAccountId}/commands`.
    *   Implement JSON payload parsing to route commands based on `"type"` (`print`, `printer_sync`, `revoke`).

### Task 3.2: Last Will and Testament (LWT) & Status
*   **Description**: Provide accurate online/offline status to the cloud.
*   **Actionable Items**:
    *   Configure MQTT client options to set a Will Message: `{"state":"offline"}` to topic `devices/{DeviceAccountId}/status`.
    *   On successful MQTT connect, actively publish `{"state":"online"}` to the same topic.

---

## Phase 4: Hardware Synchronization

### Task 4.1: Cloud Printer Sync
*   **Description**: Report local hardware capabilities to the cloud orchestrator.
*   **Actionable Items**:
    *   Create a method to gather the list of installed printers from `PrinterService`.
    *   Implement `POST /api/printagent/sync-printers` with the printer list.
    *   Trigger this sync automatically when receiving the MQTT `{"type":"printer_sync"}` command, and manually via the UI button.

---

## Phase 5: The Print Execution Engine

### Task 5.1: Job Download Pipeline
*   **Description**: Download the actual document when instructed.
*   **Actionable Items**:
    *   When MQTT command `{"type":"print", "data":"[JobId]"}` is received, trigger the download flow.
    *   Implement `GET /api/printagent/jobs/{jobId}/download` in `ApiService`.
    *   Save the downloaded payload (assumed PDF) to a secure, temporary local file.

### Task 5.2: PDF Printing Integration
*   **Description**: Silently print the downloaded PDF.
*   **Actionable Items**:
    *   Integrate a silent PDF printing library (e.g., `PdfiumViewer` or `Ghostscript.NET`).
    *   Send the downloaded temp file to the target printer without opening secondary applications.
    *   Delete the temporary file immediately after the print spooler accepts it.

### Task 5.3: Status Reporting
*   **Description**: Close the loop by reporting success or failure.
*   **Actionable Items**:
    *   Implement `POST /api/printagent/jobs/{jobId}/status`.
    *   Send `{ "status": "Printed", "errorMessage": "" }` on success, or the respective error on failure.

---

## Phase 6: Application Lifecycle & Security Resilience

### Task 6.1: Remote Revocation (Self-Wipe)
*   **Description**: Securely disconnect and wipe local data if compromised.
*   **Actionable Items**:
    *   Listen for MQTT `{"type":"revoke"}` or globally catch HTTP `401 Unauthorized` responses.
    *   When triggered: Drop MQTT/HTTP connections, delete `deviceGuid` and all credentials from `settings.json`/DPAPI.
    *   Show a Windows Toast notification: "Device Revoked by Administrator" and force the UI back to the setup state.

### Task 6.2: Single Instance & Silent Boot
*   **Description**: Enforce application constraints for background running.
*   **Actionable Items**:
    *   Implement a `Mutex` in `App.xaml.cs` to ensure only one instance runs. If a second starts, bring the first to the foreground.
    *   Implement CLI argument parsing. If started with `--background` (as done by the startup registry key), skip rendering `MainWindow.xaml` and only show the System Tray icon.
