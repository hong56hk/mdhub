#!/usr/bin/env python3
"""Check that a published Mdhub.exe is one self-contained .NET single-file app.

The bundle footer is the 32-byte ".net core bundle" signature. The eight bytes
before it are the manifest offset. Native runtime libraries and the managed
app have to be entries in that manifest, not files sitting beside the exe.
"""

from __future__ import annotations

import struct
import sys
import zlib

SIGNATURE = bytes([
    0x8B, 0x12, 0x02, 0xB9, 0x6A, 0x61, 0x20, 0x38,
    0x72, 0x7B, 0x93, 0x02, 0x14, 0xD7, 0xA0, 0x32,
    0x13, 0xF5, 0xB9, 0xE6, 0xEF, 0xAE, 0x33, 0x18,
    0xEE, 0x3B, 0x2D, 0xCE, 0x24, 0xB3, 0x6A, 0xAE,
])

FILE_TYPES = {
    0: "Unknown",
    1: "Assembly",
    2: "NativeBinary",
    3: "DepsJson",
    4: "RuntimeConfigJson",
    5: "Symbols",
}

# Keep these in sync with the LogicalName values in Mdhub.csproj.
RESOURCE_NEEDLES = (
    b"mdhub.bundle.dictionaries.en_US.dic",
    b"mdhub.bundle.dictionaries.fr.dic",
    b"mdhub.bundle.dictionaries.es_ES.dic",
    b"mdhub.bundle.THIRD-PARTY-NOTICES.md",
)


def fail(message: str) -> None:
    print(f"single-file check failed: {message}", file=sys.stderr)
    raise SystemExit(1)


def read_7bit(data: bytes, pos: int) -> tuple[int, int]:
    result = 0
    shift = 0
    while True:
        if pos >= len(data):
            fail("truncated 7-bit integer in the bundle manifest")
        byte = data[pos]
        pos += 1
        result |= (byte & 0x7F) << shift
        if byte < 0x80:
            return result, pos
        shift += 7
        if shift > 28:
            fail("invalid 7-bit integer in the bundle manifest")


def read_string(data: bytes, pos: int) -> tuple[str, int]:
    length, pos = read_7bit(data, pos)
    end = pos + length
    if end > len(data):
        fail("truncated string in the bundle manifest")
    return data[pos:end].decode("utf-8"), end


def header_offset(data: bytes) -> int:
    found: list[int] = []
    start = 0
    while True:
        at = data.find(SIGNATURE, start)
        if at < 0:
            break
        if at >= 8:
            found.append(struct.unpack_from("<q", data, at - 8)[0])
        start = at + 1
    offsets = [offset for offset in found if 0 < offset < len(data)]
    if len(offsets) != 1:
        fail(f"expected one bundle header offset, found {offsets or found}")
    return offsets[0]


def parse_manifest(data: bytes, offset: int) -> tuple[int, list[tuple[int, int, int, int, str]]]:
    if offset + 16 > len(data):
        fail("bundle header offset is outside the file")
    major, minor = struct.unpack_from("<II", data, offset)
    count = struct.unpack_from("<i", data, offset + 8)[0]
    if major < 6 or minor < 0 or count < 1:
        fail(f"unexpected bundle header version={major}.{minor} files={count}")
    pos = offset + 12
    _bundle_id, pos = read_string(data, pos)
    # deps.json offset/size, runtimeconfig offset/size, flags.
    if pos + 40 > len(data):
        fail("truncated bundle header")
    pos += 40
    entries: list[tuple[int, int, int, int, str]] = []
    for _ in range(count):
        if pos + 17 > len(data):
            fail("truncated bundle file entry")
        file_offset, size = struct.unpack_from("<qq", data, pos)
        pos += 16
        compressed = struct.unpack_from("<q", data, pos)[0]
        pos += 8
        file_type = data[pos]
        pos += 1
        path, pos = read_string(data, pos)
        entries.append((file_type, file_offset, size, compressed, path))
    return major, entries


def entry_bytes(data: bytes, offset: int, size: int, compressed: int) -> bytes:
    span = compressed if compressed else size
    if offset < 0 or span < 0 or offset + span > len(data):
        fail("a bundle entry points outside the exe")
    blob = data[offset:offset + span]
    if not compressed:
        return blob
    try:
        return zlib.decompress(blob, -15)
    except zlib.error as exc:
        fail(f"could not inflate a bundle entry: {exc}")


def require_pe(data: bytes) -> None:
    if len(data) < 20 * 1024 * 1024:
        fail(f"exe is only {len(data)} bytes; a self-contained single-file build is much larger")
    if data[:2] != b"MZ":
        fail("not a PE executable")
    pe = struct.unpack_from("<I", data, 0x3C)[0]
    if pe < 0 or pe + 70 > len(data) or data[pe:pe + 4] != b"PE\0\0":
        fail("missing PE signature")
    machine = struct.unpack_from("<H", data, pe + 4)[0]
    if machine != 0x8664:
        fail(f"expected an x64 executable (machine 0x8664), got 0x{machine:04x}")
    magic = struct.unpack_from("<H", data, pe + 24)[0]
    if magic != 0x20B:
        fail(f"expected a PE32+ optional header, got 0x{magic:04x}")
    subsystem = struct.unpack_from("<H", data, pe + 24 + 68)[0]
    if subsystem != 2:
        fail(f"expected the Windows GUI subsystem, got {subsystem}")


def main() -> None:
    if len(sys.argv) != 2:
        fail("usage: verify-single-file.py Mdhub.exe")
    path = sys.argv[1]
    with open(path, "rb") as handle:
        data = handle.read()
    require_pe(data)
    major, entries = parse_manifest(data, header_offset(data))
    if len(entries) < 50:
        fail(f"only {len(entries)} embedded files; the runtime does not look bundled")

    def named(suffix: str) -> list[tuple[int, int, int, int, str]]:
        needle = suffix.lower()
        return [entry for entry in entries if entry[4].replace("\\", "/").lower().endswith(needle)]

    # .NET 10 links CoreCLR into the single-file host, so coreclr.dll is not its own
    # manifest entry. Third-party native libraries still are, and only when
    # IncludeNativeLibrariesForSelfExtract is on. System.Private.CoreLib.dll is
    # present only for a self-contained build.
    skia = [entry for entry in entries if entry[0] == 2 and "skia" in entry[4].lower()]
    harfbuzz = [entry for entry in entries if entry[0] == 2 and "harfbuzz" in entry[4].lower()]
    if not skia or not harfbuzz:
        natives = ", ".join(entry[4] for entry in entries if entry[0] == 2)
        fail("native Skia and HarfBuzz libraries are not embedded (IncludeNativeLibrariesForSelfExtract): " + natives)
    app = [entry for entry in entries if entry[4].replace("\\", "/") == "Mdhub.dll"]
    if len(app) != 1 or app[0][0] != 1:
        fail("Mdhub.dll is not embedded as a managed assembly")
    runtime = named("System.Private.CoreLib.dll")
    if len(runtime) != 1 or runtime[0][0] != 1:
        fail("System.Private.CoreLib.dll is not embedded; the exe is not self-contained")
    host_end = min(entry[1] for entry in entries)
    if host_end < 1024 * 1024:
        fail(f"single-file host is only {host_end} bytes; the native runtime does not look linked in")

    for file_type, offset, size, compressed, rel in entries:
        span = compressed if compressed else size
        if offset < 0 or span < 0 or offset + span > len(data):
            fail(f"entry {rel} ({FILE_TYPES.get(file_type, file_type)}) is not inside the exe")

    dll = entry_bytes(data, app[0][1], app[0][2], app[0][3])
    missing = [needle.decode() for needle in RESOURCE_NEEDLES if needle not in dll]
    if missing:
        fail("Mdhub.dll is missing embedded resources: " + ", ".join(missing))

    native_count = sum(1 for entry in entries if entry[0] == 2)
    print(
        f"SINGLE_FILE_OK {path} bytes={len(data)} bundle_version={major} "
        f"entries={len(entries)} native={native_count} host_bytes={host_end} "
        f"skia={skia[0][4]} harfbuzz={harfbuzz[0][4]} dictionaries=embedded"
    )


if __name__ == "__main__":
    main()
