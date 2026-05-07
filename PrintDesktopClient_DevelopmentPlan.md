# Development Plan: .NET 9.0 Auto-Print Desktop Application

## 1. High-Level Architecture Overview
The application will be built using the **Model-View-ViewModel (MVVM)** architectural pattern to ensure clean separation of concerns between the UI and business logic.
It will utilize a background worker service paradigm within a desktop app container to ensure non-blocking continuous loops and background MQTT listening.

**Key Components:**
*   **UI Layer (Views):** Displays printer selection, current status, document selection prompt, and incoming MQTT messages/logs.
*   **Presentation Logic (ViewModels):** Handles user interactions, manages the continuous prompt loop, and binds data to the UI.
*   **Services Layer:**
    *   `PrinterService`: Enumerates printers, manages selection, and handles the actual printing logic via Windows Spooler APIs / .NET PrintDocument.
    *   `DocumentProcessingService`: Validates files and routes them to the correct printing mechanism based on extension.
    *   `MqttListenerService`: Maintains a persistent connection to the MQTT broker, subscribes to topics, and triggers the print workflow upon message receipt.
    *   `NotificationService`: Dispatches success/failure messages to the UI and OS.
    *   `ConfigurationService`: Manages loading and saving of settings (selected printer, MQTT credentials).

## 2. Technology & Library Recommendations
*   **Target Framework:** .NET 9.0
*   **UI Framework:** **WPF (Windows Presentation Foundation)**.
    *   *Justification:* WPF is incredibly mature, stable, and has a vast ecosystem for enterprise/utility desktop apps. It provides robust data binding (crucial for live MQTT feeds and status updates) and allows for fine-grained control over background threading without UI lockups. While WinUI 3 is newer, WPF remains the gold standard for reliable, system-integrating utility applications.
*   **MQTT Client:** `MQTTnet`
    *   *Justification:* The de facto standard, high-performance MQTT library for .NET. Supports MQTT v3.1.1 and v5.0.
*   **Printing APIs:**
    *   *Text (MQTT):* `System.Drawing.Common` (`PrintDocument`). Allows precise control over text layout, fonts, and margins for received messages.
    *   *Documents:* Use `System.Diagnostics.Process` with the `Print` verb for general documents (relies on default OS handlers). For robust, headless PDF printing (recommended), integrate `PdfiumViewer` or `FreeSpire.PDF` to avoid popping up external applications like Adobe Reader.
*   **MVVM Framework:** `CommunityToolkit.Mvvm`
    *   *Justification:* Lightweight, fast, and modern source-generator-based MVVM support provided by Microsoft.
*   **Logging:** `Serilog` (with File and Debug sinks)
    *   *Justification:* Essential for tracking background print jobs and diagnosing MQTT connection issues.
*   **Secure Storage:** Windows Credential Manager API (via a wrapper) or standard .NET DPAPI for encrypting MQTT passwords rather than storing them in plain text in `appsettings.json`.

## 3. Detailed Task Breakdown

### Phase 1: Project Setup & Core Infrastructure
*   **Task 1.1:** Initialize a new WPF Application targeting .NET 9.0.
*   **Task 1.2:** Configure Dependency Injection (`Microsoft.Extensions.Hosting` and `Microsoft.Extensions.DependencyInjection`) to manage service lifetimes (Singleton for MQTT/Printer services).
*   **Task 1.3:** Setup Logging (`Serilog`) to write to a rolling file (e.g., `%LOCALAPPDATA%\PrintApp\logs\`).
*   **Task 1.4:** Implement secure `ConfigurationService` to load/save user settings (Printer Name, MQTT configs) securely using DPAPI.

### Phase 2: UI Design & MVVM Plumbing
*   **Task 2.1:** Install `CommunityToolkit.Mvvm` and set up the `MainViewModel`.
*   **Task 2.2:** Design the Main Window Layout:
    *   Top section: Printer selection dropdown (ComboBox) with a "Refresh" button.
    *   Middle section: Drag-and-drop zone / "Browse File" button for manual printing.
    *   Bottom section: A rolling log/status list box showing recent prints, MQTT messages, and system notifications.
    *   Status bar: MQTT connection status indicator (Red/Green).
*   **Task 2.3:** Implement a modern, clean theme (consider adding `MaterialDesignThemes` for polished out-of-the-box styling).

### Phase 3: Printer Discovery & Selection
*   **Task 3.1:** Implement `PrinterService.GetAvailablePrinters()` using `System.Drawing.Printing.PrinterSettings.InstalledPrinters`.
*   **Task 3.2:** Bind the printer list to the UI ComboBox.
*   **Task 3.3:** Implement logic to persist the user's selected printer to settings upon change, and load it on application startup.

### Phase 4: Document Printing Pipeline (Continuous Loop)
*   **Task 4.1:** Implement file browsing dialog and validation (ensure file exists, check supported extensions).
*   **Task 4.2:** Implement `PrintDocument(filePath, printerName)` in `PrinterService`.
    *   Handle generic files using shell execute (`ProcessStartInfo.Verb = "Print"`).
    *   *(Optional but recommended)* Integrate a dedicated PDF library for silent PDF printing without spawning Acrobat Reader UI.
*   **Task 4.3:** Implement the Continuous Loop Logic: After a document is queued/printed, immediately clear the UI state and keep the prompt active for the next document drop/selection.
*   **Task 4.4:** Integrate non-blocking asynchronous calls (`Task.Run`) to ensure the UI remains responsive during spooling.

### Phase 5: MQTT Integration
*   **Task 5.1:** Integrate `MQTTnet` library.
*   **Task 5.2:** Implement `MqttListenerService.StartAsync()`:
    *   Connect to `mqttserver.test` using `mqttuser` and the secured `mqttpass`.
    *   Implement automatic reconnect logic with exponential backoff on disconnect.
    *   Subscribe to topic `home/printer/print`.
*   **Task 5.3:** Handle incoming messages: Extract string payload (e.g. "Hi Fuad this is test").
*   **Task 5.4:** Implement `PrinterService.PrintText(string text, string printerName)`:
    *   Use `PrintDocument`. Handle `PrintPage` event to format text with reasonable margins (e.g. 1-inch), a readable font (e.g., Consolas or Arial, 12pt), and line wrapping (using `Graphics.MeasureString` or specifying a layout rectangle in `DrawString`).
*   **Task 5.5:** Hook the incoming MQTT message event to the text printing function automatically.

### Phase 6: Notification & Error Handling
*   **Task 6.1:** Implement centralized `NotificationService`.
*   **Task 6.2:** Wrap all printer interactions and MQTT connections in robust `try/catch` blocks.
*   **Task 6.3:** On success/failure, log the event via Serilog AND push a message to the `NotificationService`.
*   **Task 6.4:** Display notifications in the UI (e.g., Snackbar at the bottom of the window, or appending to the on-screen log).
*   **Task 6.5:** Generate Windows Toast Notifications (`Microsoft.Toolkit.Uwp.Notifications`) for background failures (e.g., "Printer Offline" or "MQTT Disconnected").

### Phase 7: Testing Strategy
*   **Task 7.1:** Unit Test `MqttListenerService` logic using a mocked MQTT broker or abstraction.
*   **Task 7.2:** Unit Test `PrinterService` layout logic (print to PDF/XPS virtual printer during tests to verify output without wasting paper).
*   **Task 7.3:** Integration Testing: Deploy Mosquitto locally to verify MQTT sub/pub flow.
*   **Task 7.4:** Edge Case Testing: Network disconnects, default printer uninstalled while running, malformed MQTT payloads.

### Phase 8: Packaging & Deployment
*   **Task 8.1:** Configure the .NET 9.0 project for Single File Publish (`PublishSingleFile=true`).
*   **Task 8.2:** Set up a WiX Toolset installer or MSIX package if a formal installation process is required, or deploy as a portable executable.
*   **Task 8.3:** Ensure required fonts or native dependencies (like Pdfium) are correctly copied to the output directory.

## 4. Risks, Edge Cases, and Mitigations

| Risk / Edge Case | Mitigation Strategy |
| :--- | :--- |
| **Spooler Hanging / UI Freeze** | Move all printer API calls to background threads (`Task.Run`). Never execute printing logic on the Dispatcher UI thread. |
| **MQTT Connection Drops** | Implement robust `WithAutomaticReconnect` settings in `MQTTnet`. Update UI status indicator to show offline status to the user. |
| **Unresponsive Default Printer** | Add timeout handling around print jobs if possible. Check printer status via WMI/System APIs before attempting to print to prevent deadlocks. |
| **"Print" Verb Issues (No Default App)** | Shell printing fails if no app is registered to print the file type. **Mitigation:** Strictly validate file types. Recommend dedicated libraries for common types (PDF) to bypass shell reliance. |
| **Long MQTT Text Payloads** | Implement pagination logic in `PrintPage` event handler. Calculate text height and add pages (`e.HasMorePages = true`) if text overflows the page bounds. |
| **Credential Exposure** | Do not hardcode passwords. Store `mqttpass` securely using the OS Credential Manager or DPAPI encryption. |

## 5. Optional Future Enhancements
*   **System Tray Integration:** Allow the app to be minimized to the system tray (Notification Area) so it runs unobtrusively in the background while still listening to MQTT.
*   **Printer Profiles:** Allow linking specific document types or MQTT topics to different printers (e.g., `home/printer/labels` goes to a thermal printer, `home/printer/docs` goes to a laser printer).
*   **Print History Database:** Store a local SQLite database of all printed jobs (timestamp, source, status) for auditing.
*   **Web API / Remote Trigger:** Add a small local Kestrel HTTP endpoint to trigger prints via REST in addition to MQTT.
