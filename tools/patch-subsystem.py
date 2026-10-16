#!/usr/bin/env python3
"""Force the PE subsystem of a .NET apphost to GUI (2).

Why this exists: MSBuild normally rewrites the apphost's subsystem (and embeds the
icon, manifest and version resource) during a Windows build. Building a WinExe off
Windows only emits NETSDK1074 and skips that step, leaving the raw apphost from the
runtime pack, which is console-subsystem. Launched from Explorer such an app opens a
console window alongside its UI.

The subsystem field is a plain 2-byte field in the PE optional header and nothing in
the apphost reads it back, so flipping it is safe. The PE checksum is not recomputed
because Windows only enforces it for drivers.
"""
import struct
import sys

GUI_SUBSYSTEM = 2


def patch(path):
    with open(path, "r+b") as handle:
        data = handle.read()

        if data[:2] != b"MZ":
            raise SystemExit("{}: not a PE file".format(path))

        pe_offset = struct.unpack_from("<I", data, 0x3C)[0]
        if data[pe_offset:pe_offset + 4] != b"PE\0\0":
            raise SystemExit("{}: bad PE signature".format(path))

        optional = pe_offset + 24
        magic = struct.unpack_from("<H", data, optional)[0]
        if magic not in (0x10B, 0x20B):
            raise SystemExit("{}: unexpected optional header magic 0x{:x}".format(path, magic))

        at = optional + 68
        current = struct.unpack_from("<H", data, at)[0]

        if current == GUI_SUBSYSTEM:
            print("{}: already GUI subsystem".format(path))
            return

        handle.seek(at)
        handle.write(struct.pack("<H", GUI_SUBSYSTEM))
        print("{}: subsystem {} -> {} (console -> GUI)".format(path, current, GUI_SUBSYSTEM))


if __name__ == "__main__":
    if len(sys.argv) < 2:
        raise SystemExit("usage: patch-subsystem.py <exe> [exe...]")
    for argument in sys.argv[1:]:
        patch(argument)
