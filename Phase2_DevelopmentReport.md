# Auto-Print Agent – Phase 2 Development Report

> Auto-generated development log tracking the completion of each task defined in `Resources/Phase2_DevelopmentPlan.md`.

---

## Progress Summary

| Task | Description | Status |
| :--- | :--- | :---: |
| 1.1 | Immutable Identity & Secure Storage | ✅ Done |
| 1.2 | Enhance Configuration UI | ✅ Done |
| 2.1 | Implement ApiService | ✅ Done |
| 2.2 | Authentication Bridge | ✅ Done |
| 3.1 | Command Subscription & Routing | ✅ Done |
| 3.2 | LWT & Online/Offline Status | ✅ Done |
| 4.1 | Cloud Printer Sync | ✅ Done |
| 5.1 | Job Download Pipeline | ✅ Done |
| 5.2 | PDF Printing Integration | ✅ Done |
| 5.3 | Job Status Reporting | ✅ Done |
| 6.1 | Remote Revocation (Self-Wipe) | ✅ Done |
| 6.2 | Single Instance & Silent Boot | ✅ Done |

---

## Task 1.1 – Immutable Identity & Secure Storage

**Date:** 2026-05-08
**Commit:** `feat(phase2): ...`

### Work Done
- [x] Extended `UserSettings` model with `DeviceGuid`, `ApiBaseUrl`, and `DeviceAccountId`.
- [x] On first run, `ConfigurationService` generates a UUID `DeviceGuid` and persists it — never overwritten.
- [x] Added `GetApiSecret()` / `SaveApiSecret()` using the same DPAPI pattern as the MQTT password.
- [x] Added `WipeCredentials()` method that deletes all DPAPI-encrypted files and clears `DeviceAccountId` / `DeviceGuid` for the revocation flow.
- [x] Refactored DPAPI helpers into generic `LoadSecret` / `SaveSecret` methods to eliminate duplication.

---

## Task 1.2 – Enhance Configuration UI

**Date:** 2026-05-08
**Commit:** `feat(phase2): ...`

### Work Done
- [x] Added a new **Cloud** tab to `MainWindow.xaml` with inputs for `API Base URL`, `Device Account ID`, `API Secret`, and a read-only `Device GUID` display.
- [x] Added **TEST API** button that pings the API and shows `✅ API Reachable` or `❌ Cannot reach API`.
- [x] Added **SAVE & AUTHENTICATE** button that saves settings and authenticates with the cloud.
- [x] Added **DISCONNECT DEVICE (WIPE)** button with a confirmation dialog before wiping credentials.
- [x] Added **SYNC TO CLOUD** button on the Dashboard printer row for manual printer sync.
- [x] Added **Sync Printers** entry in the System Tray context menu.

---

## Task 2.1 – Implement ApiService

**Date:** 2026-05-08
**Commit:** `feat(phase2): ...`

### Work Done
- [x] Created `ApiService.cs` with all four Postman-collection endpoints.
- [x] Registered `IHttpClientFactory` and `ApiService` (singleton) in `App.xaml.cs`.
- [x] Named client `"PrintAgent"` — headers `X-Device-Id` and `X-Api-Secret` are injected automatically on each authenticated request.
- [x] Added `TestApiConnectionAsync()` for the UI test button (5-second timeout, accepts any non-500 response).
- [x] `OnUnauthorized` event is raised whenever any endpoint returns HTTP 401.

---

## Task 2.2 – Authentication Bridge

**Date:** 2026-05-08
**Commit:** `feat(phase2): ...`

### Work Done
- [x] `AuthenticateAsync()` POSTs `{ deviceAccountId, apiSecret, deviceGuid }` to `/api/printagent/authenticate`.
- [x] On HTTP 200, `SaveApiSettingsCommand` in `MainViewModel` re-initializes `MqttListenerService` so the device topic is applied.
- [x] On HTTP 401, the `OnUnauthorized` event is raised, which cascades into `TriggerRevocation()`.

---

## Task 3.1 – Command Subscription & Routing

**Date:** 2026-05-08
**Commit:** `feat(phase2): ...`

### Work Done
- [x] `MqttListenerService` now subscribes to `devices/{DeviceAccountId}/commands` when `DeviceAccountId` is set, falling back to the legacy topic when not authenticated.
- [x] JSON payloads are parsed by `type` field and dispatched to dedicated events: `OnPrintCommand`, `OnSyncCommand`, `OnRevokeCommand`.
- [x] Unknown / plain-text payloads fall through to the legacy `OnMessageReceived` event for backward compatibility.

---

## Task 3.2 – LWT & Online/Offline Status

**Date:** 2026-05-08
**Commit:** `feat(phase2): ...`

### Work Done
- [x] MQTT client is configured with a Will Message `{"state":"offline"}` published to `devices/{DeviceAccountId}/status` with retain=true.
- [x] On successful connect, the client actively enqueues `{"state":"online"}` to the same retained topic, ensuring accurate state even if LWT fires.

---

## Task 4.1 – Cloud Printer Sync

**Date:** 2026-05-08
**Commit:** `feat(phase2): ...`

### Work Done
- [x] `ApiService.SyncPrintersAsync()` POSTs `{ deviceGuid, printers[] }` to `/api/printagent/sync-printers`.
- [x] `SyncPrintersCommand` in `MainViewModel` lets the user trigger a sync manually from the Dashboard.
- [x] MQTT `printer_sync` command also triggers the same sync flow automatically.

---

## Tasks 5.1, 5.2 & 5.3 – Print Job Execution Pipeline

**Date:** 2026-05-08
**Commit:** `feat(phase2): ...`

### Work Done
- [x] `ApiService.DownloadJobAsync(jobId)` performs a `GET /api/printagent/jobs/{jobId}/download` and returns the raw PDF bytes.
- [x] `PrinterService.PrintPdfBytes()` saves the bytes to a temp file, prints silently via **PdfiumViewer** (`CreatePrintDocument` + `StandardPrintController`), then **deletes the temp file** immediately — no data at rest.
- [x] `ApiService.UpdateJobStatusAsync()` POSTs `{ status, errorMessage }` to `/api/printagent/jobs/{jobId}/status` after print success or failure.
- [x] The full pipeline is orchestrated in `MainViewModel.ExecutePrintJobAsync()`, wired to `MqttListenerService.OnPrintCommand`.

---

## Task 6.1 – Remote Revocation (Self-Wipe)

**Date:** 2026-05-08
**Commit:** `feat(phase2): ...`

### Work Done
- [x] `MainViewModel.TriggerRevocation()` is the single entry point for all revocation flows.
- [x] It calls `ConfigurationService.WipeCredentials()` which deletes all DPAPI secret files and clears `DeviceGuid`.
- [x] Revocation is triggered by: MQTT `revoke` command, any HTTP 401, or the user clicking "Disconnect Device (Wipe)".
- [x] After wipe, `IsRevoked = true` is set on the ViewModel and a notification is displayed.

---

## Task 6.2 – Single Instance & Silent Boot

**Date:** 2026-05-08
**Commit:** `feat(phase2): ...`

### Work Done
- [x] Named `Mutex("PrintDesktopClient_SingleInstance")` in `App.xaml.cs` enforces single-instance. A second launch shows an info dialog and exits immediately.
- [x] `--background` argument support: if the app is launched with this flag (as it would be from the registry auto-start key), `MainWindow` is instantiated but never shown — the app runs silently with only the tray icon active.

---

*Report updated after every committed task. Build status: ✅ 0 Errors.*
