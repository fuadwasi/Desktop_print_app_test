# Windows Installer (.exe) for PrintDesktopClient

Create a single professional installer `.exe` that, when double-clicked on any Windows PC, installs the Print Desktop Client application, places a shortcut, and registers it to start automatically at Windows login (via the Registry).

---

## Key Facts Discovered

| Item | Value |
|:---|:---|
| **Target Framework** | `.NET Framework 4.8` (`net48`) — already present on most modern Windows machines |
| **Silent startup arg** | `--background` (already coded in `App.xaml.cs`) — installer will use this for autostart |
| **Icon** | `Resources\Images\PDC_Icons\favicon.ico` |
| **App Version** | `1.1.0` |

> [!NOTE]
> Because the app targets **.NET Framework 4.8** (not .NET Core/5+), it cannot be published as a `--self-contained` single `.exe`. It requires .NET Framework 4.8 to be present on the target machine. The installer will check for this and prompt the user to install it if missing — but in practice, .NET 4.8 ships with every Windows 10 and Windows 11 machine by default.

---

## Toolchain: Inno Setup

**Inno Setup** is the industry-standard, free, open-source Windows installer creator. It produces a single professional `.exe` installer with:
- GUI wizard with progress bar
- License agreement page
- Custom install directory
- Start Menu shortcut creation
- Desktop shortcut (optional)
- Auto-startup via Windows **Registry** (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`)
- Silent install support (`/VERYSILENT /SUPPRESSMSGBOXES`)
- Uninstaller generation (Add/Remove Programs entry)

---

## Open Questions

> [!IMPORTANT]
> Please review and answer these before I execute:

1. **Install Location**: Should the app install to the default `%ProgramFiles%\PrintDesktopClient` (requires admin), or to `%LOCALAPPDATA%\PrintDesktopClient` (no admin required, per-user install)? 
   - ✅ **Recommended**: `%LOCALAPPDATA%` — no UAC elevation needed, better for a background agent.

2. **Auto-startup**: The app already supports `--background` to start silently to the tray. The installer will register this in the Windows Registry autorun key. Should this be **enabled by default** during installation, or should the user be given a **checkbox** to opt in?
   - ✅ **Recommended**: Enabled by default, with a checkbox to opt out.

3. **Desktop Shortcut**: Create a desktop shortcut in addition to the Start Menu shortcut?

4. **License/EULA Page**: Should the installer show a license agreement page? If yes, do you have license text, or should I generate a placeholder?

---

## Proposed Changes

### Step 1 — Publish the Application (Release Build)

Build the application in `Release` mode to produce the distributable output files:

```powershell
dotnet publish c:\001.Data\Personal\PrintDesktopClient\PrintDesktopClient.csproj `
  -c Release -r win-x64 --no-self-contained
```

This outputs all necessary DLLs and the `PrintDesktopClient.exe` to:
`bin\Release\net48\win-x64\publish\`

---

### Step 2 — Create Inno Setup Script

#### [NEW] [PrintDesktopClient.iss](file:///c:/001.Data/Personal/PrintDesktopClient/SetupProject/PrintDesktopClient.iss)

An Inno Setup script (`.iss`) file that defines the entire installer behavior. Key sections:

- **`[Setup]`**: App name, version (`1.1.0`), publisher, install directory, icon, min OS version
- **`[Files]`**: Recursively include all files from the publish output folder
- **`[Icons]`**: Start Menu folder shortcut and optional Desktop shortcut
- **`[Registry]`**: Write autorun entry to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` with the `--background` argument
- **`[Run]`**: Launch the app at the end of installation
- **`[UninstallDelete]`**: Clean up user data on uninstall (optional, configurable)
- **`[Tasks]`**: User-selectable checkbox options (e.g., "Start automatically with Windows", "Create Desktop shortcut")

---

### Step 3 — Build the Installer

Run Inno Setup Compiler from the command line to compile the `.iss` script into a single `PrintDesktopClient_Setup_v1.1.0.exe`:

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" `
  "c:\001.Data\Personal\PrintDesktopClient\SetupProject\PrintDesktopClient.iss"
```

**Output**: `c:\001.Data\Personal\PrintDesktopClient\SetupProject\Output\PrintDesktopClient_Setup_v1.1.0.exe`

---

## What the End-User Experience Looks Like

1. User double-clicks `PrintDesktopClient_Setup_v1.1.0.exe`
2. A standard Windows installer wizard opens with a welcome screen
3. User clicks Next → selects install folder (default pre-filled) → sees options checkboxes (autostart, desktop shortcut) → clicks Install
4. Files are copied, Start Menu shortcut and Desktop shortcut are created, Registry autorun key is written
5. Installer offers to launch the app immediately on Finish
6. On next Windows login, the app starts automatically to the system tray in background mode

---

## Verification Plan

### Automated
- `dotnet build` succeeds with no errors
- `dotnet publish` produces output in the publish directory

### Manual
- Run the installer on a clean Windows machine / VM
- Verify `PrintDesktopClient.exe` is present in the install folder
- Verify Start Menu shortcut works
- Verify Desktop shortcut works (if enabled)
- Check `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` in `regedit` for the autostart entry
- Reboot the machine and confirm the app appears in the system tray automatically
- Run the uninstaller and confirm clean removal
