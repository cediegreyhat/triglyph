# TriGlyph

**Three glyphs, driven properly.** A clean-room control panel for the KF420 / KF620
DGT CPU cooler's three-digit USB temperature display.

One self-contained `TriGlyph.exe`, ~146 MB. No installer, no admin rights, **no kernel
driver**, no vendor DLLs, no HWiNFO.

> **Status: `0.1.0-beta`** — feature-complete but **not yet verified on Windows or
> against real hardware.** The wire protocol is transcribed from an independently
> verified reference; the HID path has never been exercised on a physical panel.
> See [Known limitations](#known-limitations).

## Why this exists

The vendor "JL DGT" tool works, but it is worth replacing:

- It bundles a **renamed copy of HWiNFO32.DLL** (v8.25, Martin Malik / REALiX s.r.o.)
  as `api-ms-win-core-sysinfo-825-64.dll` and `-32.dll` — 2.4 MB UPX-packed files
  masquerading as Windows API-set DLLs, which are normally ~15 KB. Inside them are
  HWiNFO's ring-0 drivers (`HWiNFO_x64_205.sys`).
- It requires Administrator and installs that kernel driver as a service.
- It also ships leaked debug artifacts (`DeviceDriver.pdb`, `.iobj`, `.ipdb`).

Nothing there looks deliberately malicious — it is HWiNFO's SDK, renamed — but the
masquerade is a licensing violation and a technique AV heuristics rightly flag.

TriGlyph requires no elevation, installs nothing, and touches no other process's memory.

## Quick start

1. Copy `TriGlyph.exe` and the `themes/` folder to the target machine.
2. Run it. It lives in the tray; the tray icon is coloured by temperature.
3. Open **Advanced** and press **Re-read** to decode your panel's report descriptor —
   the vendor never published it, so this is how you find out what the device can do.

For accurate CPU package temperature without a driver, run
[LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)
alongside it. That is the trade the vendor tool made differently, by bundling a driver.

## Features

- **Pluggable temperature sources**, tried in priority order, with honest failure:
  LibreHardwareMonitor WMI → OpenHardwareMonitor WMI → HWiNFO shared memory → ACPI
  thermal zone → Windows temperature probe → manual value. Nothing is ever fabricated; unavailable is reported.
- **Report descriptor decoder** — reads the panel's undocumented 205-byte descriptor
  over `IOCTL_HID_GET_REPORT_DESCRIPTOR` and parses it, so unknown report ids can be
  discovered rather than guessed.
- **Raw feature-report console** with a safety gate (see [Safety](#safety)).
- **Auto-detection** that states *why* it chose a device, plus manual pinning.
- **Four themes** as editable JSON (`glass`, `softlight`, `industrial`, `editorial`),
  accent override, °C/°F, refresh interval, optional smoothing.
- **Tray icon** drawn at runtime and coloured by temperature.
- **Run at login** via `HKCU\...\Run` — per-user, never elevated.

## The hardware

| Property | Value |
|----------|-------|
| USB identity | `1a2c:4184` (China Resource Semico), shared by KF400/KF420/KF620 DGT |
| Interfaces | 2. Interface 0 is a fake keyboard; **interface 1 is the panel** |
| Endpoints | Interrupt IN only — **no OUT endpoint** |
| Host → device | Control transfers only, i.e. HID **feature reports** |
| Temperature report | Feature report **7**, 64 bytes: `[7, 0xFF, 0xFF, hundreds, tens, ones]` |
| Resolution | Whole degrees, three independent decimal digits |

Because there is no OUT endpoint, the panel is a dumb peripheral: its firmware renders
the digits and the host only pushes a number. Nothing here needs a driver.

Full detail, including what is *not* known: **[docs/PROTOCOL.md](docs/PROTOCOL.md)**.

## Safety

The raw console blocks writes to report ids other than 7 unless you arm **unsafe
writes**. This is not hypothetical — the reference project records that a bad USB state
left a panel unconfigured (`can't set config #1, error -32`) until reboot.

Writes to *input* reports are refused outright: those are read-only, and an attempt
means the report was misidentified.

## Configuration

Settings live at `%APPDATA%\TriGlyph\settings.json` — hand-editable, string enums,
unknown keys tolerated, out-of-range values clamped on load.

Themes are `themes/*.json`. Drop in your own; a user file overrides a built-in of the
same name. Invalid colours are repaired rather than rejected.

## Building

```bash
./build.sh
```

Runs the tests, publishes `win-x64` self-contained single-file, and patches the PE
subsystem. See **[docs/BUILDING.md](docs/BUILDING.md)** for the off-Windows details.

## Testing

```bash
dotnet test tests/TriGlyph.Tests/TriGlyph.Tests.csproj
```

**102 tests**, all runnable on Linux. Every piece with real behaviour lives in
`TriGlyph.Core` behind interfaces, so the interesting logic is testable without
Windows — including the whole read → smooth → encode → write pipeline, with failover,
range rejection, staleness and duplicate suppression.

Design rationale: **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)**.

## Known limitations

- **Never executed on Windows or against real hardware.** This is the big one. Run the
  Advanced page's descriptor reader first and report what it says.
- **No version resource** in the published exe — MSBuild cannot add apphost resources
  off Windows (`NETSDK1074`). The window icon *is* embedded as a WPF resource, and
  the PE subsystem *is* patched to GUI, so no console window appears. The tray icon
  is drawn at runtime and coloured by temperature.
- **Display blanking is not implemented.** No blank/off command is known.
- **RGB and fan control are not reachable** — the ARGB fans connect to the
  motherboard's ARGB header and the fan to a PWM header, neither through this device.
- **Only temperature semantics are enforced** (0–150 °C). The hardware accepts any
  three digits, so other values can be sent via the raw console.

## Documentation

| Document | Contents |
|----------|----------|
| [docs/PROTOCOL.md](docs/PROTOCOL.md) | The hardware protocol, what is measured vs guessed |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Module map and design decisions |
| [docs/BUILDING.md](docs/BUILDING.md) | Building, including off-Windows caveats |
| [CHANGELOG.md](CHANGELOG.md) | Release history |

## Repository layout

```
src/TriGlyph.Core     pure net6.0, no Windows dependencies — all behaviour, all tests
src/TriGlyph.App      net6.0-windows WPF — interop, sources, services, views
tests/TriGlyph.Tests  xunit, runs on Linux
docs/                 protocol, architecture, build notes
tools/                patch-subsystem.py, standalone probe
themes/               shipped beside the exe
build.sh              tests -> publish -> subsystem patch
```

## Credits

- [`njt1982/tzmrit-dgt-linux`](https://github.com/njt1982/tzmrit-dgt-linux) — the
  independent daemon that verified the temperature report this project encodes.
- [`liquidctl/liquidctl` issue #817](https://github.com/liquidctl/liquidctl/issues/817)
  — the `lsusb -v` dump the interface layout comes from.

Neither is affiliated with this project.

## License

MIT — see [LICENSE](LICENSE).
