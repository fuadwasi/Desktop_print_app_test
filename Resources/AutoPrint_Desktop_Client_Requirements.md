Auto-Print Desktop Client (Local Agent)
Last updated by | System Architect | May 8, 2026

Overview
--------
Design and implement a cross-platform, locally installed desktop application built on the .NET framework. The application acts as a secure, real-time bridge between the nopCommerce cloud orchestrator and the user’s local physical hardware, executing automated print commands silently in the background.

Business Goals
--------------
1. Zero-Friction Operations
   - Install-and-forget utility: installs, creates OS shortcuts, and runs silently on boot.
   - High-speed, silent printing with System Tray lifecycle management.

2. Secure Cloud-to-Ground Bridging
   - Use encrypted outbound connections only (no inbound ports required).
   - Use MQTT (encrypted) for real-time signaling and authenticated REST for payloads.

3. Resilient Self-Management
   - Self-healing, auto-reconnect, and accurate online/offline reporting via MQTT LWT.

Functional Requirements
-----------------------
1. Core Application Lifecycle & OS Integration
   - Installation & Shortcuts: Delivered via OS-specific installer (e.g., .msi/.exe on Windows). Installer must create Desktop and Start Menu shortcuts.
   - Auto-Start on Boot: Installer or app first-run must register auto-start (e.g., HKCU Run key on Windows) with a `--background` argument.
   - Silent Background Startup: When launched with `--background`, app must start without rendering main window and only show System Tray icon.
   - Minimize-to-Tray: Intercept window close/minimize events to hide window instead of terminating the process.
   - Single Instance Constraint: Enforce a mutex so only one instance runs; launching a second should bring existing settings window to front.

2. Security & State Management
   - Immutable Device Identity: Generate and persist a UUID/GUID (`deviceGuid`) at first run.
   - Credential Storage: Securely store `DeviceAccountId` and `ApiSecret` locally (use OS-provided secure storage where available).
   - Configurable Endpoints & Credentials: The MQTT broker URL (including TLS options), MQTT credentials (if used), the HTTP API Base URL, and the device credentials (`DeviceAccountId`, `ApiSecret`) must be configurable from the application's UI. These settings must be validated by the client (test/validate button) and saved securely when accepted.
   - Authentication Flow: POST to `/api/printagent/authenticate` with JSON {deviceAccountId, apiSecret, deviceGuid}. On 200 OK, save credentials and initialize MQTT.
   - LWT State Tracking: Configure MQTT Will Message `{"state":"offline"}` to topic `devices/{DeviceAccountId}/status`. Publish `{"state":"online"}` upon successful connect.

3. Hardware Discovery & Synchronization
   - OS Spooler Integration: Read local print queue (Windows Print Spooler, Linux CUPS) to get logical printer names.
   - Automated Sync: On MQTT `{"type":"printer_sync","data":""}`, perform hardware discovery and POST to `/api/printagent/sync-printers` with headers `X-Device-Id` and `X-Api-Secret`, body {deviceGuid, printers[]}.

4. Print Execution Engine
   - Event Consumption: Subscribe to `devices/{DeviceAccountId}/commands`.
   - Payload Execution Flow on `{ "type": "print", "data": "[JobId]" }`:
     1. GET `/api/printagent/jobs/{jobId}/download` with `X-Device-Id` and `X-Api-Secret` headers.
     2. Download document payload to memory or temp file.
     3. Send document to specified logical printer by exact name.
     4. POST `/api/printagent/jobs/{jobId}/status` with headers and body `{ "status": "Printed", "errorMessage": "" }`.

5. Remote Revocation & Exception Handling
   - Event-Driven Wipe: On MQTT `{"type":"revoke","data":""}`, immediately drop network connections and permanently delete local credentials and `deviceGuid`.
   - HTTP 401 Fallback: If any HTTP request returns 401 Unauthorized, trigger the same self-wipe procedure.
   - Post-Revocation UX: After wipe, display OS desktop notification "Device Revoked by Administrator" and open the main setup window to await new credentials.

6. User Interface (Settings Window & Tray)
   - System Tray Iconography: Use a printer-themed icon supporting dynamic overlays (green for online, red for offline).
   - Tray Context Menu: Provide `Open Settings`, `Sync Printers`, and `Exit App` (true termination).
   - Configuration Interface: Inputs for `Device Account ID` and `Api Secret`.
   - Configuration Page Access: The Settings window must include a distinct "Configuration" page/tab where administrators can set and validate the MQTT Broker URL, MQTT credentials (username/password or client cert config), API Base URL, and other connection options.
   - Real-time Indicators: Live color-coded MQTT connection status in the UI.
   - Manual Controls: `Sync Printers to Cloud` and `Disconnect Device` (wipe credentials) buttons.

7. Configuration & Settings Page (Functional)
   - Purpose: Provide a dedicated, discoverable UI page for configuring network and credential settings required to connect to the cloud and MQTT broker.
   - Fields to Expose:
     - `API Base URL` (e.g., https://api.example.com) with a `Test API` button that issues an authenticated request to `/api/printagent/authenticate` or a lightweight health check.
     - `MQTT Broker URL` (e.g., ssl://mqtt.example.com:8883) with TLS options.
     - `MQTT Username` and `MQTT Password` or client-certificate selection where applicable.
     - `Device Account ID` and `API Secret` (credential fields also exposed elsewhere for convenience).
     - `Connection Test` controls: `Test MQTT` and `Test API` that report success/failure and any error messages.
   - Behavior & Validation:
     - Validation: All network endpoints and credentials must be validated before being persisted.
     - Secure Persistence: Save secrets using OS secure storage (Windows Credential Manager, macOS Keychain, Linux keyring).
     - Scoped Defaults: Allow resetting to a safe default set of endpoints (non-production sandbox) for testing.
     - Audit: Optional local audit log entry for configuration changes (timestamp and user action) kept minimal and not containing raw secrets.

Recommended .NET Technical Specifications
----------------------------------------
- Target Framework: .NET 8 or .NET 9
- UI Framework: Avalonia UI
- Tray: Avalonia `<TrayIcon>` component in App.axaml for tray icon and menu
- MQTT Library: MQTTnet (use LWT and robust reconnects)
- HTTP Client: `HttpClientFactory` with typed clients and automatic header injection
- Print Handling: `PdfiumViewer` for PDFs and `System.Drawing.Printing.PrinterSettings` for queue discovery

Deployment & Packaging
----------------------
- Build as a Self-Contained Executable.
- Wrap in an installer (Inno Setup or Windows Installer) that:
  - Installs `.exe` to `Program Files`.
  - Creates Desktop and Start Menu shortcuts.
  - Adds registry auto-start entry `HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run` with `--background` argument.

Notes for Implementation
------------------------
- Follow SOLID principles: design code to be modular, testable and maintainable by adhering to the Single Responsibility, Open/Closed, Liskov Substitution, Interface Segregation, and Dependency Inversion principles. Prefer small classes, clear interfaces, and dependency injection to enable unit testing and extensibility.
- Ensure credentials and device GUID are stored using secure platform facilities (Windows Credential Manager, macOS Keychain, Linux keyring) where possible.
- Respect user privacy and minimize local storage of documents; delete temp files after print completion.
- Use exponential backoff for MQTT/HTTP reconnects and avoid busy-loop retries.

Appendix: MQTT Topics & HTTP Endpoints
-------------------------------------
- MQTT Topics:
  - `devices/{DeviceAccountId}/commands` (subscribe)
  - `devices/{DeviceAccountId}/status` (publish online/offline state, LWT)
- HTTP Endpoints (cloud orchestrator):
  - `POST /api/printagent/authenticate`
  - `POST /api/printagent/sync-printers`
  - `GET /api/printagent/jobs/{jobId}/download`
  - `POST /api/printagent/jobs/{jobId}/status`
