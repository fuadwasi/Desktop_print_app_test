# 📦 Building the Installer — Step-by-Step Guide

This document explains how to publish the `PrintDesktopClient` application and compile it
into a single distributable Windows installer `.exe` using **Inno Setup**.

> **Current version:** `1.2.0`
> **Output file:** `SetupProject\Output\PrintDesktopClient_Setup_v1.2.0.exe`

---

## Build Machine Prerequisites

Ensure the following are installed on your **build machine** (not needed on the end-user's machine):

| Tool | How to Check | Install Command |
|:---|:---|:---|
| **.NET SDK** (with .NET 4.8 support) | `dotnet --version` | [Download](https://dotnet.microsoft.com/download) |
| **Inno Setup 6** | Check Start Menu | `winget install --id JRSoftware.InnoSetup --silent` |

---

## ⚠️ One-Time Setup — Download the .NET 4.8 Prerequisite

> [!IMPORTANT]
> The `SetupProject\Prerequisites\` folder and its contents are **gitignored** and will
> **not** be present after a fresh `git clone`. You must download `ndp48-web.exe` once
> before you can compile the installer.

The installer embeds the **.NET Framework 4.8 Web Bootstrapper** so it can automatically
install .NET 4.8 on machines that don't have it. Run this once after cloning:

```powershell
# Create the Prerequisites folder and download the bootstrapper from Microsoft
New-Item -ItemType Directory -Path "SetupProject\Prerequisites" -Force | Out-Null
Invoke-WebRequest `
  -Uri "https://go.microsoft.com/fwlink/?LinkId=2085155" `
  -OutFile "SetupProject\Prerequisites\ndp48-web.exe" `
  -UseBasicParsing
```

After downloading, verify it:

```powershell
Get-Item "SetupProject\Prerequisites\ndp48-web.exe" | Select-Object Name, @{N='SizeKB';E={[math]::Round($_.Length/1KB,0)}}
# Expected: ndp48-web.exe  ~1406 KB
```

> This file is intentionally excluded from git (see `.gitignore`). It is downloaded at
> build time and does **not** need to be committed to the repository.

---

## Step 1 — Publish the Application

Open a terminal in the project root and run:

```powershell
dotnet publish PrintDesktopClient.csproj `
  -c Release `
  -r win-x64 `
  --no-self-contained `
  -o bin\Release\net48\publish
```

| Flag | Purpose |
|:---|:---|
| `-c Release` | Optimized release build |
| `-r win-x64` | Targets 64-bit Windows |
| `--no-self-contained` | Does not bundle the runtime (.NET 4.8 is already on all Win10/11 machines) |
| `-o bin\Release\net48\publish` | Output folder for all DLLs and the `.exe` |

After completion the folder will contain:

```
bin\Release\net48\publish\
├── PrintDesktopClient.exe    ← main application
├── MQTTnet.dll
├── PdfiumViewer.dll
├── MaterialDesignThemes.Wpf.dll
└── ... (all other dependency DLLs)
```

---

## Step 2 — Compile the Installer

Run Inno Setup Compiler against the script:

```powershell
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" "SetupProject\PrintDesktopClient.iss"
```

After ~10 seconds the installer is created at:

```
SetupProject\Output\PrintDesktopClient_Setup_v*.exe
```

This single `.exe` is the **distributable installer** — copy it to a USB drive, upload it,
or share it however you like.

---

## Full Build Script (All Steps Together)

Run this after a fresh clone to go from zero to a ready installer:

```powershell
# 0. Download .NET 4.8 prerequisite (only needed once per clone)
New-Item -ItemType Directory -Path "SetupProject\Prerequisites" -Force | Out-Null
if (-not (Test-Path "SetupProject\Prerequisites\ndp48-web.exe")) {
    Write-Host "Downloading .NET 4.8 web bootstrapper..."
    Invoke-WebRequest -Uri "https://go.microsoft.com/fwlink/?LinkId=2085155" `
                      -OutFile "SetupProject\Prerequisites\ndp48-web.exe" -UseBasicParsing
}

# 1. Publish the application
dotnet publish PrintDesktopClient.csproj -c Release -r win-x64 --no-self-contained -o bin\Release\net48\publish

# 2. Compile the installer
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" "SetupProject\PrintDesktopClient.iss"

# 3. Show result (wildcard — works for any version)
Get-ChildItem "SetupProject\Output\PrintDesktopClient_Setup_*.exe" | Select-Object Name, @{N='SizeMB';E={[math]::Round($_.Length/1MB,2)}}
```

---

## Changing the Output Path & Version

All key settings live at the top of
[`SetupProject\PrintDesktopClient.iss`](file:///c:/001.Data/Personal/PrintDesktopClient/SetupProject/PrintDesktopClient.iss):

```ini
#define AppVersion    "1.2.0"
#define AppPublisher  "Fuad Hasan"
#define SourceDir     "..\bin\Release\net48\publish"
#define OutputDir     "Output"
```

### Change the installer output directory

```ini
#define OutputDir "Output"                           ; → SetupProject\Output\  (default)
#define OutputDir "..\"                              ; → project root folder
#define OutputDir "C:\Releases\PrintDesktopClient"  ; → absolute path anywhere
```

### Change the installer filename

```ini
OutputBaseFilename=PrintDesktopClient_Setup_v{#AppVersion}  ; default
OutputBaseFilename=PDC_Setup_{#AppVersion}                   ; custom example
```

### Change the app version number

Update `#define AppVersion` in the `.iss` file **and** `<Version>` in
[`PrintDesktopClient.csproj`](file:///c:/001.Data/Personal/PrintDesktopClient/PrintDesktopClient.csproj)
to keep them in sync.

### Change the publish source directory

If you change the `-o` flag in `dotnet publish`, update the matching line in the `.iss`:

```ini
#define SourceDir "..\bin\Release\net48\publish"
```

---

## What the Installer Does on the End-User Machine

When the user runs `PrintDesktopClient_Setup_v1.2.0.exe`:

1. 🔍 **Checks for .NET Framework 4.8** — if missing, automatically downloads and installs it from Microsoft (internet required for this step only). On all Windows 10/11 machines this check passes instantly.
2. 🔐 **UAC prompt** — installs to `C:\Program Files (x86)\Print Desktop Client` (standard system location, requires one-time admin elevation).
3. 📌 Creates a **Start Menu** shortcut.
4. 🖥️ Optionally creates a **Desktop** shortcut (opt-in checkbox, unchecked by default).
5. 🚀 Optionally registers **auto-startup** at Windows login via `HKCU\...\Run` with the `--background` flag — starts silently to the system tray (opt-out checkbox, **enabled by default**).
6. ✅ Offers to launch the app immediately after installation.
7. 🗑️ Registers a full **Uninstaller** in Windows Add/Remove Programs.

> **Note:** After installation, all runtime data (settings, logs, encrypted credentials) are
> stored in `%LOCALAPPDATA%\PrintDesktopClient\` — no admin rights required for day-to-day use.

---

## File Reference

| File | Git tracked? | Purpose |
|:---|:---|:---|
| [`SetupProject\PrintDesktopClient.iss`](file:///c:/001.Data/Personal/PrintDesktopClient/SetupProject/PrintDesktopClient.iss) | ✅ Yes | Inno Setup script — the installer source |
| `SetupProject\Prerequisites\ndp48-web.exe` | 🚫 Gitignored | .NET 4.8 bootstrapper — download at build time |
| `SetupProject\Output\PrintDesktopClient_Setup_v*.exe` | 🚫 Gitignored | Final distributable installer |
| `bin\Release\net48\publish\` | 🚫 Gitignored | Intermediate publish output |
