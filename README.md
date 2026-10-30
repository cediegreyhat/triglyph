# <p align="center">TriGlyph</p>

<p align="center">
  <b>Three glyphs, driven properly.</b><br>
  A clean-room control panel for the KF420 / KF620 DGT three-digit USB temperature display.
</p>

<p align="center">
  <a href="https://github.com/cediegreyhat/triglyph/releases/tag/v0.1.0-beta">
    <img src="https://img.shields.io/badge/release-v0.1.0--beta-22d3ee?style=for-the-badge&logo=github" alt="Release v0.1.0-beta">
  </a>
  <a href="https://github.com/cediegreyhat/triglyph/blob/main/LICENSE">
    <img src="https://img.shields.io/badge/license-MIT-22c55e?style=for-the-badge" alt="MIT License">
  </a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11-0078d4?style=for-the-badge&logo=windows" alt="Windows 10/11">
  <img src="https://img.shields.io/badge/.NET-6.0-512bd4?style=for-the-badge&logo=dotnet" alt=".NET 6">
  <img src="https://img.shields.io/badge/tests-102%20passing-brightgreen?style=for-the-badge&logo=xunit" alt="102 tests passing">
  <img src="https://img.shields.io/badge/driver-none%20required-10b981?style=for-the-badge" alt="No driver required">
</p>

<p align="center">
  <a href="https://github.com/cediegreyhat/triglyph/releases/latest">
    <img src="https://img.shields.io/github/downloads/cediegreyhat/triglyph/total?style=flat-square&color=22d3ee" alt="Total downloads">
  </a>
  <a href="https://github.com/cediegreyhat/triglyph/stargazers">
    <img src="https://img.shields.io/github/stars/cediegreyhat/triglyph?style=flat-square&color=f59e0b" alt="GitHub stars">
  </a>
  <a href="https://github.com/cediegreyhat/triglyph/issues">
    <img src="https://img.shields.io/github/issues/cediegreyhat/triglyph?style=flat-square&color=ef4444" alt="Open issues">
  </a>
  <a href="https://github.com/cediegreyhat/triglyph/commits/main">
    <img src="https://img.shields.io/github/last-commit/cediegreyhat/triglyph?style=flat-square&color=8b5cf6" alt="Last commit">
  </a>
</p>

---

## ✨ Highlights

- 🧩 **One self-contained `TriGlyph.exe`** (~146 MB) — no installer, no admin rights, **no kernel driver**, no vendor DLLs, no HWiNFO.
- 🔌 **Pluggable temperature sources** tried in priority order, with honest failure reporting. Nothing is ever fabricated.
- 🧪 **Report descriptor decoder** reads the panel's undocumented 205-byte descriptor over `IOCTL_HID_GET_REPORT_DESCRIPTOR`.
- 🎛️ **Raw feature-report console** with a safety gate, auto-detection with manual pinning, four editable themes.
- 🖥️ **Tray icon drawn at runtime** and coloured by temperature; run-at-login via `HKCU\...\Run`.

## 📥 Download

Grab the latest build from the **[Releases page](https://github.com/cediegreyhat/triglyph/releases/latest)**.

```text
TriGlyph.exe   (~146 MB, self-contained, win-x64)
themes/        (ship beside the exe)
```

1. Copy `TriGlyph.exe` and the `themes/` folder to the target machine.
2. Run it — it lives in the tray; the tray icon is coloured by temperature.
3. Open **Advanced** → **Re-read** to decode your panel's report descriptor.

> 💡 For accurate CPU package temperature without a driver, run
> [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) alongside it.
> That is the trade the vendor tool made differently — by bundling a driver.

## 🎯 Why this exists

The vendor **"JL DGT"** tool works, but it is worth replacing:

| Concern | Vendor tool | TriGlyph |
|---|---|---|
| Kernel driver | Bundles a **renamed copy of HWiNFO32.DLL** (`api-ms-win-core-sysinfo-825-64.dll`) hiding `HWiNFO_x64_205.sys` ring-0 driver | ❌ None |
| Elevation | Requires Administrator | ✅ User-level |
| Install footprint | Installs a kernel service | ✅ Portable, nothing installed |
| Debug artifacts | Ships `DeviceDriver.pdb`, `.iobj`, `.ipdb` | ✅ Clean |
| Licensing | Renamed HWiNFO SDK masquerading as Windows API-set DLLs | ✅ Clean-room |

Nothing there looks deliberately malicious — it is HWiNFO's SDK, renamed — but the masquerade is a licensing violation and a technique AV heuristics rightly flag. TriGlyph requires no elevation, installs nothing, and touches no other process's memory.

## 🔌 Temperature sources

Tried in priority order, with honest failure:

```
LibreHardwareMonitor WMI → OpenHardwareMonitor WMI → HWiNFO shared memory
→ AIDA64 shared memory → ACPI thermal zone → Windows temperature probe → Manual value
```

Unavailable sources are reported, never fabricated. Missing sources from a newer build are merged into your saved priority list automatically on load.

## 🧠 The hardware

| Property | Value |
|---|---|
| USB identity | `1a2c:4184` (China Resource Semico), shared by KF400/KF420/KF620 DGT |
| Interfaces | 2 — interface 0 is a fake keyboard, **interface 1 is the panel** |
| Endpoints | Interrupt IN only — **no OUT endpoint** |
| Host → device | Control transfers only, i.e. HID **feature reports** |
| Temperature report | Feature report **7**, 64 bytes: `[7, 0xFF, 0xFF, hundreds, tens, ones]` |
| Resolution | Whole degrees, three independent decimal digits |

Because there is no OUT endpoint, the panel is a dumb peripheral: firmware renders the digits and the host only pushes a number. Nothing here needs a driver.

Full detail, including what is *not* known: **[docs/PROTOCOL.md](docs/PROTOCOL.md)**.

## 🛡️ Safety

The raw console blocks writes to report ids other than **7** unless you arm **unsafe writes**. This is not hypothetical — the reference project records that a bad USB state left a panel unconfigured (`can't set config #1, error -32`) until reboot.

Writes to *input* reports are refused outright: those are read-only, and an attempt means the report was misidentified.

## ⚙️ Configuration

- Settings live at `%APPDATA%\TriGlyph\settings.json` — hand-editable, string enums, unknown keys tolerated, out-of-range values clamped on load.
- Themes are `themes/*.json` (`glass`, `softlight`, `industrial`, `editorial`). Drop in your own; a user file overrides a built-in of the same name. Invalid colours are repaired rather than rejected.

## 🏗️ Building

```bash
./build.sh
```

Runs the tests, publishes `win-x64` self-contained single-file, and patches the PE subsystem. See **[docs/BUILDING.md](docs/BUILDING.md)** for the off-Windows details.

## 🧪 Testing

```bash
dotnet test tests/TriGlyph.Tests/TriGlyph.Tests.csproj
```

**102 tests**, all runnable on Linux. Every piece with real behaviour lives in `TriGlyph.Core` behind interfaces, so the interesting logic is testable without Windows — including the whole read → smooth → encode → write pipeline, with failover, range rejection, staleness and duplicate suppression.

Design rationale: **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)**.

## 📋 Known limitations

- **No version resource** in the published exe — MSBuild cannot add apphost resources off Windows (`NETSDK1074`). The window icon *is* embedded as a WPF resource, and the PE subsystem *is* patched to GUI, so no console window appears.
- **Display blanking is not implemented.** No blank/off command is known.
- **RGB and fan control are not reachable** — the ARGB fans connect to the motherboard's ARGB header and the fan to a PWM header, neither through this device.
- **Only temperature semantics are enforced** (0–150 °C). The hardware accepts any three digits, so other values can be sent via the raw console.

## 📚 Documentation

| Document | Contents |
|---|---|
| [docs/PROTOCOL.md](docs/PROTOCOL.md) | The hardware protocol, what is measured vs guessed |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Module map and design decisions |
| [docs/BUILDING.md](docs/BUILDING.md) | Building, including off-Windows caveats |
| [CHANGELOG.md](CHANGELOG.md) | Release history |

## 🗂️ Repository layout

```
src/TriGlyph.Core     pure net6.0, no Windows dependencies — all behaviour, all tests
src/TriGlyph.App      net6.0-windows WPF — interop, sources, services, views
tests/TriGlyph.Tests  xunit, runs on Linux
docs/                 protocol, architecture, build notes
tools/                patch-subsystem.py, standalone probe
themes/               shipped beside the exe
build.sh              tests -> publish -> subsystem patch
```

## 🙏 Credits

- [`njt1982/tzmrit-dgt-linux`](https://github.com/njt1982/tzmrit-dgt-linux) — the independent daemon that verified the temperature report this project encodes.
- [`liquidctl/liquidctl` issue #817](https://github.com/liquidctl/liquidctl/issues/817) — the `lsusb -v` dump the interface layout comes from.

Neither is affiliated with this project.

## 📄 License

MIT — see [LICENSE](LICENSE). Made with ❤️ for cleaner USB HID tooling.