
<h1 align="center">
  CapsLang
</h1>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet" alt=".NET 10.0" />
  <img src="https://img.shields.io/badge/AOT-✓-512BD4?style=flat-square&logo=dotnet" alt="AOT" />
  <img src="https://img.shields.io/badge/Windows-✓-0078D6?style=flat-square&logo=windows" alt="Windows" />
  <img src="https://img.shields.io/badge/License-MIT-yellow?style=flat-square" alt="License: MIT" />
</p>

<p align="center">
  <b>CapsLock → Language Switch.</b><br/>
Remaps CapsLock to keyboard layout toggle via a low-level keyboard hook.<br/>
No config, no tray icon, no dependencies. Runs entirely in the background.
</p>

<p align="center">
  <b>Windows only.</b> Works on Windows 7 / 8 / 10 / 11 (and all Windows Server versions since 2008 R2).
</p>

---

## How It Works

Hooks `WH_KEYBOARD_LL` globally. Uses `unsafe` pointer access for zero-allocation, sub-millisecond key processing.

**Two modes:**

| Mode | Behavior |
|------|----------|
| **Long-press** (default, 300ms) | Tap CapsLock → language switch via `Win+Space` (`SendInput`). Hold CapsLock (300ms) → real CapsLock toggle. |
| **Legacy** (`0` as arg) | CapsLock always switches language instantly. Press **Shift + CapsLock** for normal CapsLock toggle (passthrough). |

A named mutex prevents multiple instances.

## Command Line

```
CapsLang-x64.exe [timeout_ms]
```

| Arg | Values | Default | Description |
|-----|--------|---------|-------------|
| `timeout_ms` | number ≥ 0 | `300` | Long-press threshold. `0` = legacy (no long-press) |

**Examples:**

```powershell
CapsLang-x64.exe                    # 300ms (default)
CapsLang-x64.exe 500                # 500ms
CapsLang-x64.exe 0                  # legacy mode (no long-press, Shift+CapsLock = real CapsLock)
```

## Download

Pre-built AOT-published executables are available in [Releases](https://github.com/fryt3ch/CapsLang-win/releases).

| File | Architecture |
|------|-------------|
| `CapsLang-x64.exe` | Intel / AMD 64-bit (most common) |
| `CapsLang-x86.exe` | 32-bit (legacy systems) |
| `CapsLang-arm64.exe` | Windows on ARM (Surface Pro X/9/11, etc.) |

- **Zero dependencies** — no .NET runtime, no VC++ redistributable, nothing
- Just download, pick your architecture, and run

## Building from Source

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Publish as AOT

Choose the target runtime for your architecture:

```powershell
# Intel / AMD 64-bit
dotnet publish -c Release -r win-x64 -o publish-x64 `
  -p:PublishAot=true -p:DebugType=none `
  CapsLang.cs

# 32-bit
dotnet publish -c Release -r win-x86 -o publish-x86 `
  -p:PublishAot=true -p:DebugType=none `
  CapsLang.cs

# ARM64
dotnet publish -c Release -r win-arm64 -o publish-arm64 `
  -p:PublishAot=true -p:DebugType=none `
  CapsLang.cs
```

Each outputs a standalone `CapsLang.exe` in its respective `publish-*` folder.

## Auto-start via Task Scheduler

Run the tool automatically when you log in.

### Manual (GUI)

1. Open **Task Scheduler** → **Create Task**
2. **General** tab: check *Run with highest privileges*
3. **Triggers** tab: **New** → *Begin the task:* `At logon` → *Specific user* (your account)
4. **Actions** tab: **New** → *Action:* `Start a program` → *Program:* full path to `CapsLang.exe` → *Add arguments (optional):* e.g. `500` or `0`
5. **Settings** tab: uncheck *Stop the task if it runs longer than* (otherwise the process will be killed after 3 days by default)
6. **Conditions** tab: uncheck everything
7. OK to save

### PowerShell (one-liner)

```powershell
$exePath = "C:\Path\To\CapsLang.exe"
$arguments = ""                    # 300ms (default)
# $arguments = "500"               # 500ms
# $arguments = "0"                 # legacy mode (no long-press, Shift+CapsLock = real CapsLock)

$action = New-ScheduledTaskAction -Execute $exePath -Argument $arguments
$trigger = New-ScheduledTaskTrigger -AtLogOn
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType InteractiveToken -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]0)

Register-ScheduledTask -TaskName CapsLang -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force
```

Replace `C:\Path\To\CapsLang.exe` with the actual absolute path to your executable.

## Stopping / Removing

```powershell
# Kill the process
Stop-Process -Name CapsLang -Force

# Remove from autostart
Unregister-ScheduledTask -TaskName CapsLang -Confirm:$false
```

## License

MIT
