# Hardware protocol

What is known about the panel, what is measured, and what remains guesswork.
Nothing in this document is inferred - each claim is marked with how it is known.

## Device

| Property | Value | Source |
|----------|-------|--------|
| VID:PID | `1a2c:4184` | Reported for KF400, KF420 and KF620 DGT coolers |
| Vendor | China Resource Semico Co., Ltd | USB descriptor |
| Product string | "USB Gaming Keyboard" | USB descriptor (spoofed) |
| Manufacturer string | "SEMICO" | USB descriptor |
| bcdUSB | 1.10 | `lsusb -v` |
| Power | 100 mA, bus powered, remote wakeup | `lsusb -v` |
| Interfaces | 2 | `lsusb -v` |
| Interface 0 | HID, boot subclass, protocol **Keyboard**, EP `0x81` IN | `lsusb -v` |
| Interface 1 | HID, boot subclass, protocol **Mouse**, EP `0x82` IN | `lsusb -v` |
| Endpoints | Interrupt IN only - **no OUT endpoint** | `lsusb -v` |

The panel is **interface 1**. It masquerades as a mouse; interface 0 masquerades as a
keyboard. Neither is used as such.

### Why there is no driver

With no OUT endpoint, the host cannot send output reports. All host-to-device traffic
goes over the control pipe as **HID feature reports**. That is a userspace operation:
`HidD_SetFeature` on Windows, `HIDIOCSFEATURE` on Linux. No ring-0 component is
possible or needed.

## Temperature report

**Verified against real hardware** by the independent `tzmrit-dgt-linux` project, and
reproduced in `TemperatureFrame`:

```
feature report id : 7
total length      : 64 bytes (report id + 63 data bytes)

byte 0     : 0x07            report id
byte 1..2  : 0xFF 0xFF
byte 3     : hundreds digit  (0-9)
byte 4     : tens digit      (0-9)
byte 5     : ones digit      (0-9)
byte 6..63 : zero padding
```

Example - 45 °C:

```
07 FF FF 00 04 05 00 00 00 ... 00
```

Notes:

- Each digit is sent **independently** as a decimal digit 0-9, not as a packed
  binary value. The panel's firmware drives three 7-segment digits.
- Whole degrees only; the reference implementation truncates via `int()`, and so does
  TriGlyph. There is no decimal point on the panel.
- Because the payload is three decimal digits, the hardware can render **any value
  0-999**. The 0-150 °C bound is a *semantic* guard in TriGlyph against displaying a
  fabricated CPU temperature, not a hardware limit.

## What is NOT known

The vendor never published the protocol. Interface 1 declares a **205-byte report
descriptor** which, as far as can be established, has never been dumped publicly.

Consequences:

- **Other report ids may exist.** TriGlyph reads the descriptor off the device at
  runtime (`IOCTL_HID_GET_REPORT_DESCRIPTOR` = `0x000B0007`) and decodes it, which is
  the only honest way to find out. Until that is run on real hardware, the report
  inventory beyond id 7 is unknown.
- **The `0xFF 0xFF` prefix is unexplained.** It may be padding, or a mode/command
  selector. Sending other values in those bytes has not been characterised.
- **No blank/off command is known.** The reference project notes an "unavailable/blank
  display command" it could not find. TriGlyph therefore does not offer blanking; the
  closest honest behaviour would be to stop sending, and the panel would hold its last
  value.
- **Whether digit bytes accept values > 9 is unknown.** If a byte is a digit index it
  is 0-9; if it is a raw 7-segment bitmap it could produce letters and hex. This has
  not been tested.
- **Brightness control is unknown.** Nothing suggests it exists.

## Safety

The reference project records that an unsuccessful unbind/recovery experiment left a
panel unconfigured (`can't set config #1, error -32`) until reboot. A bad USB state is
therefore a real, observed hazard, not a theoretical one.

TriGlyph's console therefore:

- always permits report 7 (the known, verified report);
- refuses writes to *input* reports outright - those are read-only, and an attempt
  means the report was misidentified;
- requires the user to arm "unsafe writes" before sending any other feature report id.

## Cross-references

- `njt1982/tzmrit-dgt-linux` - independent Python daemon for the same panel family;
  source of the verified temperature report.
- `liquidctl/liquidctl` issue #817 - a full `lsusb -v` dump for `1a2c:4184` on a
  Jungle Leopard KF400, which is where the interface and endpoint layout above
  comes from.
- `habibrehmansg/infopanel` PR #140 - a *different* product line (LCD panels using a
  2 Mbaud CDC-ACM transport with a JPEG envelope). Not applicable to the DGT
  three-digit display, recorded here so it is not mistaken for it.
