# Changelog

All notable changes to TriGlyph. Format loosely follows [Keep a Changelog](https://keepachangelog.com/).

## [0.1.0-beta] - 2026-10-16

First beta. Everything below is new.

### Added

- Self-contained WPF control panel for the KF420/KF620 DGT three-digit display.
  Single `TriGlyph.exe`, no installer, no admin, no kernel driver.
- Pluggable temperature sources, tried in priority order: LibreHardwareMonitor WMI,
  OpenHardwareMonitor WMI, HWiNFO shared memory, ACPI thermal zone, manual value.
  No source ever fabricates a reading - it reports unavailable instead.
- **Report descriptor decoder.** Reads the panel's undocumented 205-byte descriptor
  over `IOCTL_HID_GET_REPORT_DESCRIPTOR` and parses it, so report ids the vendor
  never published can be discovered rather than guessed.
- **Raw feature-report console** with a safety gate: report 7 is always permitted,
  any other report id requires explicitly arming unsafe writes.
- Auto-detection with a stated reason, plus manual pinning of a device path.
- Four theme presets (`glass`, `softlight`, `industrial`, `editorial`) as editable
  JSON in `themes/`; user files override built-ins. Accent override, °C/°F,
  refresh interval, optional smoothing.
- Tray icon drawn at runtime and coloured by temperature.
- Run-at-login via `HKCU\...\Run` (per-user, never elevated).
- 102 unit tests, all runnable on Linux.

### Known limitations

- **Never executed on Windows or against real hardware.** The wire protocol is
  transcribed from the independently verified `tzmrit-dgt-linux` project; the HID
  path here has not been exercised on a physical device.
- No embedded icon or version resource in the published exe (`NETSDK1074` - MSBuild
  cannot add apphost resources off Windows). The PE subsystem is patched to GUI so
  no console window appears.
- Display blanking is not implemented: no blank/off command is known.
- RGB and fan control are not reachable - those connect to motherboard headers, not
  to this USB device.
- Only temperature semantics are enforced (0-150 °C). The hardware itself accepts
  any three digits, so other values can be sent via the raw console.

### Security

- Replaces the vendor "JL DGT" tool, which ships HWiNFO32.DLL renamed as
  `api-ms-win-core-sysinfo-825-{32,64}.dll` and installs HWiNFO's ring-0 driver.
  TriGlyph requires no elevation and installs nothing.
