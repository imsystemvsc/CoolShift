<p align="center">
  <img src="Resources/social_card.png" alt="CoolShift Banner" width="850">
</p>

**CoolShift** (formerly *Park Toggle*) is a modern, ultra-lightweight Windows desktop utility designed to dynamically manage CPU core parking, processor frequency scaling, power plan states, and live hardware sensor telemetry.

By running silently in your system tray, **CoolShift** shifts your PC between an energy-efficient **Cool Idle** mode and a high-performance **Always On** mode when configured apps or detected games are running.

<p align="center">
  <img src="Resources/screenshot.png" alt="CoolShift Dashboard Overview" width="850">
</p>

---

## ✨ Key Features

### ⚡ Smart Automation & Auto-Switching
- **Manual App List (Priority)**: Select running apps or browse for executables that should immediately switch to Always On. Manual selections always take priority over automatic detection.
- **Optional Game Detection**: Detects installed Steam, Epic Games, GOG, and Playnite games from their installation data. A game must have a visible top-level window and run continuously for five seconds before CoolShift switches modes.
- **Safe Passive Monitoring**: Uses Windows process start/stop events when available, performs an initial scan at launch, and falls back to low-frequency polling if WMI is unavailable. CoolShift never injects into games, hooks graphics APIs, reads process memory, changes game files, installs drivers, or uses PresentMon.
- **Ignored Applications**: Exclude individual executable names or paths. Launchers, crash reporters, uninstallers, redistributables, browsers, and CoolShift itself are excluded automatically.
- **Cool Idle Restoration**: Returns to the selected Cool Idle tier ten seconds after all detected games have closed.
- **Smart Battery Override**: Auto-detects AC vs. Battery power transitions to enforce power-saving states on battery.

### ❄️ Cool Idle Tier Presets
- **MaxCool (85% Max CPU)**: Maximum cooling & power efficiency. Configures minimum processor state to 5%, aggressive core parking, and delayed core unparking (`PERFINCTIME` = 5) to ignore brief background micro-spikes.
- **Balanced (99% Max CPU)**: Disables CPU boost clocks for quiet, cool daily computing without thermal throttling.
- **Responsive (100% Max CPU)**: Keeps full boost clock headroom while enabling dynamic idle frequency scaling.

### 📊 6-Tile Live Hardware Telemetry Panel
Real-time hardware monitoring dashboard powered by LibreHardwareMonitor and native MSI Afterburner shared memory integration (`MAHMSharedMemory`):
1. **CPU Power (W)**: Live package power draw.
2. **CPU Clock (MHz / GHz)**: Real-time average clock frequency across all cores.
3. **CPU VCore (V)**: Live CPU core voltage readouts.
4. **GPU Power (W)**: Live GPU board / package power.
5. **GPU Clock (MHz)**: Real-time GPU core clock speed.
6. **GPU VCore (V)**: Direct Ring-0 shared memory intercept from **MSI Afterburner**.

### 📈 Historical Thermal & Load Charting
- **Rolling Temperature History**: Live gradient-filled temperature chart powered by LiveCharts2 (SkiaSharp).
- **Interactive Load Gauges**: Symmetrical dual gauges for CPU and GPU usage.

### 🔔 System Tray & Silent Auto-Start
- **UAC-Free Windows Boot**: Dual-registration engine via Windows Startup Registry (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`) and Task Scheduler for silent, background startup with `--hidden` flag.
- **Live Tray Tooltip**: Hover over the tray icon for live temperature updates (`CoolShift | CPU: 35.0 °C | GPU: 37.0 °C`).
- **Quick Action Context Menu**: Right-click tray menu for instant access to Windows system tools (Restart Explorer, Open Task Manager, Flush DNS Cache, Empty Recycle Bin, Open Power Options).

---

## 🚀 Getting Started

### System Requirements
- **OS**: Windows 10 version 1809 or later, or Windows 11 (64-bit)
- **Prebuilt release**: No .NET runtime is required; the published executable is self-contained.
- **Build from source**: .NET 8 SDK

### Building from Source

```powershell
# Clone repository
git clone https://github.com/imsystemvsc/CoolShift.git
cd CoolShift

# Publish self-contained single-file Release binary
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false
```

The compiled self-contained single-file executable will be generated in `bin\Release\net8.0-windows\win-x64\publish\CoolShift.exe`.

### Testing

```powershell
dotnet test CoolShift.sln -c Release
```

---

## ⚙️ Usage

1. Launch **`CoolShift.exe`**.
2. Select your preferred **Cool Idle** preset tier (*MaxCool 85%*, *Balanced 99%*, or *Responsive 100%*).
3. Switch to the **Automation** tab to manage included apps, enable automatic game detection if wanted, and add any ignored applications.
4. Enable **Start with Windows** for background auto-management.
5. Hover over or right-click the system tray icon for live sensor telemetry and quick action tools.

---

## 🛠️ Built With

- **Framework**: WPF & .NET 8.0 (C#)
- **MVVM Architecture**: `CommunityToolkit.Mvvm`
- **Telemetry Engine**: `LibreHardwareMonitorLib` + Native MSI Afterburner Shared Memory (`MAHMSharedMemory`) Intercept
- **Visualization**: `LiveChartsCore.SkiaSharpView.WPF`
- **System Tray**: `H.NotifyIcon.Wpf`
