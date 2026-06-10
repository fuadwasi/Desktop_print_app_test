# Task-by-Task Development Plan: Profile-Based Redesign

This document details the plan to redesign the configuration system of the `PrintDesktopClient` application into a profile-first architecture, allowing multiple active profiles to run in parallel while providing a clean list and form editor interface.

---

### Task 1: Refactor and Remove Legacy Unified Configuration
* **Description**: Remove old unified configuration sections ("MQTT Section" and "Cloud Section") and direct settings-delegation from the main configuration service.
* **Proposed Changes**:
  - Delete legacy properties in `ConfigurationService.cs` that delegator-mapped to the selected profile.
  - Remove the separate "MQTT" and "Cloud" tabs from `MainWindow.xaml`.
* **Test Plan**:
  - Compile the solution. Verify that the removed properties cause compile errors only in the UI/viewmodel layer (which will be resolved in subsequent tasks), confirming that no core service logic is still reading from the old unified properties.

---

### Task 2: Refactor Profile Model & Storage System
* **Description**: Enhance the profile storage model in `ConfigurationService.cs` to explicitly support active/inactive states (`IsEnabled` mapping to Active/Inactive) and ensure unique profile constraints.
* **Proposed Changes**:
  - Update `ProfileSettings` model in `ConfigurationService.cs` to ensure clean validation (e.g. check for duplicate names).
  - Standardize DPAPI secret file cleanup when profiles are deleted.
* **Test Plan**:
  - Write a test plan/validation checking that creating multiple profiles creates unique secret data files in local appdata (e.g., `mqtt_{profileId}.dat`).
  - Verify that deleting a profile completely wipes its associated DPAPI secret files.

---

### Task 3: Implement Navigation & View State in ViewModel
* **Description**: Implement UI state switching (List view vs. Create/Edit Form view) inside the `MainViewModel.cs`.
* **Proposed Changes**:
  - Introduce an enum `enum ViewState { ProfileList, ProfileForm }`.
  - Expose a `CurrentViewState` property and navigation commands (e.g., `NavigateToCreateCommand`, `NavigateToEditCommand`, `NavigateBackToListCommand`).
  - Create a temporary model or fields for the "currently editing profile" to support cancellation (discarding changes if the user goes back to the list without saving).
* **Test Plan**:
  - Manually trigger navigation commands and assert that `CurrentViewState` transitions correctly.
  - Verify that editing fields on the edit form and then clicking "Cancel" does not overwrite the original profile settings.

---

### Task 4: Design Profile List View
* **Description**: Redesign `MainWindow.xaml` to display the profiles list in a clean table/list format.
* **Proposed Changes**:
  - Replace the left navigation rail or the main content layout with a profile list panel when `CurrentViewState` is `ProfileList`.
  - Display columns/controls: Profile Name, MQTT Connection Status (with real-time colored indicators), Toggle switch (Active/Inactive), Edit button, and Delete button.
  - Bind the Toggle switch to a command that calls `ToggleProfileEnabled` in the ViewModel.
* **Test Plan**:
  - Verify the list renders correctly with all columns.
  - Toggle a profile and confirm the state is persisted in `settings.json`.

---

### Task 5: Design Profile Create/Edit Form
* **Description**: Implement the Create/Edit form UI in `MainWindow.xaml` to manage a selected profile's settings.
* **Proposed Changes**:
  - Display fields for API Configuration (API URL, Device ID, API Secret) and MQTT Configuration (Broker URL, Username, Password, Topic, Interval).
  - Add "Test API Connection" and "Test MQTT Connection" buttons.
  - Add "Save" and "Cancel" buttons.
* **Test Plan**:
  - Click "Create New Profile", fill out the form, click "Save", and verify that a new profile appears in the list.
  - Click "Edit", modify a field, click "Save", and verify the list reflects the modified details.

---

### Task 6: Implement Connection Testing (API and MQTT)
* **Description**: Implement direct connection validation methods in the services and expose them via the edit form.
* **Proposed Changes**:
  - Implement a direct, lightweight MQTT connection test in `MqttListenerService.cs` or `ProfileSessionManager.cs` that attempts a connection with the given form parameters and returns success/failure without joining the background connection loop.
  - Expose `TestMqttConnectionCommand` and `TestApiConnectionCommand` in the ViewModel to bind to these actions.
* **Test Plan**:
  - Enter invalid MQTT credentials and click "Test MQTT Connection". Verify a failure status/message is shown.
  - Enter valid MQTT credentials and click "Test MQTT Connection". Verify a success status/message is shown.

---

### Task 7: Integrate Parallel MQTT Session Operations
* **Description**: Ensure the `ProfileSessionManager` responds dynamically to toggles, startup requests, and network state changes.
* **Proposed Changes**:
  - Update startup logic in `App.xaml.cs` to only start sessions for profiles that are marked active (`IsEnabled == true`).
  - Ensure that toggling a profile active establishes connection, and toggling inactive stops the session.
* **Test Plan**:
  - Set Profile A active and Profile B inactive. Restart the application. Verify that only Profile A connects to MQTT.
  - Toggle Profile B active while the app is running. Verify that its connection status changes to "Connected".

---

### Task 8: Edge Cases and Robustness Validation
* **Description**: Handle edge cases such as network disconnects, rapid toggles, and duplicate profile names.
* **Proposed Changes**:
  - Add validation in the Save command to prevent duplicate profile names.
  - Add debounce/lock flags to prevent multiple simultaneous connection tasks if the user rapidly toggles the enable switch.
* **Test Plan**:
  - Attempt to rename a profile to match an existing profile name and confirm the application alerts the user and prevents saving.
  - Rapidly toggle the active switch on and off 5 times. Verify that only a single connection or disconnection event completes and the final state matches the visual toggle switch.
  - Disconnect the local internet connection, verify that all profiles transition to "Disconnected", and automatically reconnect once the network is restored.
