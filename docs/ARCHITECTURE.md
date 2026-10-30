# Architecture

## The constraint that shapes everything

`net6.0-windows` code cannot execute on Linux, so anything inside the GUI project is
untestable on a Linux dev box. The design therefore pushes every piece with real
behaviour into a pure library and leaves the GUI as thin adapters.

```
src/TriGlyph.Core     net6.0, no Windows dependencies - all behaviour, all tests
src/TriGlyph.App      net6.0-windows, WPF - interop, sources, services, views
tests/TriGlyph.Tests  net6.0, xunit - runs on Linux
```

The App project's untested surface is P/Invoke signatures, XAML, and WMI calls. That
is the goal: no scheduling, failover, encoding or policy logic lives there.

## Core

| Module | Responsibility |
|--------|----------------|
| `TemperatureFrame` | Encodes a reading into the 64-byte feature report; validates range; °F conversion |
| `HidReportDescriptor` | Parses raw HID report descriptor bytes into a report inventory |
| `DeviceMatcher` | Scores enumerated HID interfaces and picks the panel, with a reason |
| `TemperaturePolicy` | Unit, smoothing (none / EMA / median-of-3), staleness |
| `TemperatureUnits` | °C/°F conversion and display formatting |
| `AppSettings` / `SettingsStore` | Configuration model, JSON round-trip, repair, clamping |
| `ThemeDefinition` / `ThemeCatalog` | Theme presets, JSON loading, discovery, repair |
| `ReportConsolePolicy` | Which raw writes are permitted |
| `TelemetryLoop` | One tick: source failover -> smooth -> encode -> write |

`TelemetryLoop` is the keystone. It depends only on two interfaces:

```csharp
public interface ITemperatureSource {
    string Name { get; }
    bool TryRead(out double celsius, out string error);
}

public interface IPanelWriter {
    bool TryWrite(TemperatureFrame frame, out string error);
}
```

Because of that, the whole read-smooth-encode-write pipeline - including failover,
range rejection, write-failure handling, staleness and duplicate suppression - is
tested on Linux with fakes. The App supplies a WMI-backed source and a
`HidD_SetFeature`-backed writer.

## App

| Layer | Contents |
|-------|----------|
| `Interop/` | `HidNative` (P/Invoke), `HidEnumerator` (SetupAPI walk), `HidFeatureChannel` (open/read/write/descriptor), `HidPanelWriter` (`IPanelWriter` adapter) |
| `Sources/` | `WmiTemperatureSource`, `HwInfoSharedMemorySource`, `Aida64SharedMemorySource`, `ManualTemperatureSource`, `SourceFactory` |
| `Services/` | `TelemetryHost` (timer + device choice), `TrayIcon`, `AutostartService` |
| `ViewModels/` | `AppState` - observable state the views bind to |
| `Views/` | `DashboardView`, `AdvancedView`, `SettingsView` |
| `Theming/` | `ThemeApplier` pushes a theme into `DynamicResource` brushes at runtime |

### Deliberate choices

- **The panel handle is opened per write, not held.** The vendor tool, HWiNFO and the
  OS keyboard driver may all want the same device; holding a handle across a 1 Hz loop
  invites conflicts. One open/write/close per tick is cheap at this rate.
- **Smoothing is off by default.** A temperature display that lags is worse than one
  that jitters, and a median hides real spikes. It is offered because some panels
  flicker visibly at 1 Hz, not because it is a good default.
- **`WriteOnlyOnChange` is off by default.** The verified reference writes every tick,
  and it is unknown whether this panel blanks itself if reports stop arriving. Cutting
  USB traffic is not worth an unknown.
- **The tray icon is drawn at runtime** from a bitmap, coloured by temperature. This
  also sidesteps the inability to embed an icon when building off Windows.
- **Failure is always reported, never papered over.** No source inventing a value, no
  clamping a bad reading into looking plausible, no silent write failures.

## Error handling

Every adapter returns `bool` plus an `out string error` rather than throwing across a
boundary. `TelemetryLoop` surfaces the last error into the UI status line, so the user
sees *why* nothing is on the panel (`no temperature source available (AcpiThermalZone:
MSAcpi_ThermalZoneTemperature returned nothing usable)`), not just that it is blank.
