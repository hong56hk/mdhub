#!/usr/bin/env python3
"""Create a zip archive that keeps Unix permissions and symlinks.

macOS Archive Utility and Info-ZIP unzip honor the executable bit on an app
host when the entry records a Unix creator and the mode in external_attr.
Symlinks are stored as the link text with the symlink file type, which is
what those tools recreate.
"""

from __future__ import annotations

import os
import stat
import sys
import time
import zipfile


def _info(path: str, arcname: str) -> zipfile.ZipInfo:
    st = os.lstat(path)
    arcname = arcname.replace("\\", "/")
    if stat.S_ISDIR(st.st_mode) and not arcname.endswith("/"):
        arcname += "/"
    info = zipfile.ZipInfo(arcname)
    # Zip timestamps must be 1980 or later.
    mtime = time.localtime(max(st.st_mtime, 315532800))
    info.date_time = mtime[:6]
    info.create_system = 3
    info.external_attr = (st.st_mode & 0xFFFF) << 16
    if stat.S_ISDIR(st.st_mode) or stat.S_ISLNK(st.st_mode):
        info.compress_type = zipfile.ZIP_STORED
    else:
        info.compress_type = zipfile.ZIP_DEFLATED
    return info


def zip_tree(source: str, destination: str) -> None:
    source = os.path.abspath(source)
    parent = os.path.dirname(source)
    with zipfile.ZipFile(destination, "w") as archive:
        for dirpath, dirnames, filenames in os.walk(source, followlinks=False):
            dirnames.sort()
            rel_dir = os.path.relpath(dirpath, parent).replace(os.sep, "/")
            archive.writestr(_info(dirpath, rel_dir), b"")
            names = sorted(filenames)
            for name in names:
                path = os.path.join(dirpath, name)
                arcname = os.path.relpath(path, parent).replace(os.sep, "/")
                info = _info(path, arcname)
                if stat.S_ISLNK(os.lstat(path).st_mode):
                    archive.writestr(info, os.readlink(path))
                    continue
                with archive.open(info, "w") as entry, open(path, "rb") as src:
                    while True:
                        chunk = src.read(1024 * 1024)
                        if not chunk:
                            break
                        entry.write(chunk)


def main() -> None:
    if len(sys.argv) != 3:
        print("usage: zip_unix.py SOURCE DESTINATION.zip", file=sys.stderr)
        sys.exit(2)
    source, destination = sys.argv[1], sys.argv[2]
    if not os.path.isdir(source):
        print(f"not a directory: {source}", file=sys.stderr)
        sys.exit(2)
    parent = os.path.dirname(os.path.abspath(destination))
    if parent:
        os.makedirs(parent, exist_ok=True)
    zip_tree(source, destination)


if __name__ == "__main__":
    main()
