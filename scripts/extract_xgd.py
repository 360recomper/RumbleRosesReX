#!/usr/bin/env python3
import argparse
import mmap
import os
from pathlib import Path, PurePosixPath
import struct
import sys
import time


SECTOR_SIZE = 2048
MAGIC = b"MICROSOFT*XBOX*MEDIA"
LIKELY_GAME_OFFSETS = (
    0x00000000,
    0x0000FB20,
    0x00020600,
    0x02080000,
    0x0FD90000,
)
FILE_ATTRIBUTE_DIRECTORY = 0x10
CHUNK_SIZE = 8 * 1024 * 1024


class ExtractError(RuntimeError):
    pass


class Entry:
    def __init__(self, path, attributes, sector, size):
        self.path = path
        self.attributes = attributes
        self.sector = sector
        self.size = size

    @property
    def is_dir(self):
        return bool(self.attributes & FILE_ATTRIBUTE_DIRECTORY)


def le16(data, offset):
    return struct.unpack_from("<H", data, offset)[0]


def le32(data, offset):
    return struct.unpack_from("<I", data, offset)[0]


def find_game_offset(data):
    for game_offset in LIKELY_GAME_OFFSETS:
        magic_offset = game_offset + (32 * SECTOR_SIZE)
        if magic_offset + len(MAGIC) <= len(data) and data[magic_offset:magic_offset + len(MAGIC)] == MAGIC:
            return game_offset
    raise ExtractError("Xbox game partition magic was not found")


def safe_output_path(root, guest_path):
    normalized = PurePosixPath(*guest_path.parts)
    if normalized.is_absolute() or ".." in normalized.parts:
        raise ExtractError(f"Unsafe guest path: {guest_path}")
    host_path = (root / Path(*normalized.parts)).resolve()
    root_resolved = root.resolve()
    if root_resolved != host_path and root_resolved not in host_path.parents:
        raise ExtractError(f"Guest path escapes output root: {guest_path}")
    return host_path


def read_tree(data, game_offset, dir_offset, dir_size, ordinal, parent_path, entries, seen):
    key = (dir_offset, ordinal)
    if key in seen:
        raise ExtractError(f"Directory tree cycle at offset 0x{dir_offset:X}, ordinal {ordinal}")
    seen.add(key)

    entry_offset = dir_offset + (ordinal * 4)
    dir_end = dir_offset + dir_size
    if entry_offset + 14 > dir_end or entry_offset + 14 > len(data):
        raise ExtractError(f"Directory entry outside directory table at 0x{entry_offset:X}")

    node_l = le16(data, entry_offset + 0)
    node_r = le16(data, entry_offset + 2)
    sector = le32(data, entry_offset + 4)
    size = le32(data, entry_offset + 8)
    attributes = data[entry_offset + 12]
    name_length = data[entry_offset + 13]
    name_start = entry_offset + 14
    name_end = name_start + name_length
    if name_end > dir_end or name_end > len(data):
        raise ExtractError(f"Directory entry name outside directory table at 0x{entry_offset:X}")

    if node_l:
        read_tree(data, game_offset, dir_offset, dir_size, node_l, parent_path, entries, seen)

    raw_name = bytes(data[name_start:name_end])
    name = raw_name.decode("utf-8", errors="replace")
    guest_path = parent_path / name
    entry = Entry(guest_path, attributes, sector, size)
    entries.append(entry)

    if entry.is_dir and size:
        child_offset = game_offset + (sector * SECTOR_SIZE)
        if child_offset + size > len(data):
            raise ExtractError(f"Directory data outside image for {guest_path}")
        read_tree(data, game_offset, child_offset, size, 0, guest_path, entries, set())

    if node_r:
        read_tree(data, game_offset, dir_offset, dir_size, node_r, parent_path, entries, seen)


def parse_entries(data):
    game_offset = find_game_offset(data)
    fs_offset = game_offset + (32 * SECTOR_SIZE)
    root_sector = le32(data, fs_offset + 20)
    root_size = le32(data, fs_offset + 24)
    root_offset = game_offset + (root_sector * SECTOR_SIZE)

    if root_size < 13 or root_size > 32 * 1024 * 1024:
        raise ExtractError(f"Suspicious root directory size: {root_size}")
    if root_offset + root_size > len(data):
        raise ExtractError("Root directory is outside the image")

    entries = []
    read_tree(data, game_offset, root_offset, root_size, 0, PurePosixPath(), entries, set())
    return game_offset, root_sector, root_size, entries


def format_size(size):
    units = ("B", "KiB", "MiB", "GiB")
    value = float(size)
    for unit in units:
        if value < 1024 or unit == units[-1]:
            return f"{value:.1f} {unit}" if unit != "B" else f"{size} B"
        value /= 1024


def extract(data, output_root, entries, force=False):
    files = [entry for entry in entries if not entry.is_dir]
    total_size = sum(entry.size for entry in files)
    copied = 0
    started = time.monotonic()

    output_root.mkdir(parents=True, exist_ok=True)
    for entry_index, entry in enumerate(files, start=1):
        host_path = safe_output_path(output_root, entry.path)
        host_path.parent.mkdir(parents=True, exist_ok=True)
        data_offset = extract.game_offset + (entry.sector * SECTOR_SIZE)
        if data_offset + entry.size > len(data):
            raise ExtractError(f"File data outside image for {entry.path}")

        if host_path.exists() and not force and host_path.stat().st_size == entry.size:
            copied += entry.size
            continue

        with host_path.open("wb") as out:
            remaining = entry.size
            cursor = data_offset
            while remaining:
                amount = min(CHUNK_SIZE, remaining)
                out.write(data[cursor:cursor + amount])
                cursor += amount
                remaining -= amount
                copied += amount

        elapsed = max(time.monotonic() - started, 0.001)
        mib_per_second = (copied / (1024 * 1024)) / elapsed
        print(
            f"[{entry_index}/{len(files)}] {entry.path} "
            f"({format_size(entry.size)}) - {format_size(copied)} / {format_size(total_size)} "
            f"at {mib_per_second:.1f} MiB/s",
            flush=True,
        )


def main():
    parser = argparse.ArgumentParser(description="Extract an Xbox 360 XGD/XDVDFS ISO")
    parser.add_argument("iso", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--list", action="store_true", help="List entries without extracting")
    parser.add_argument("--force", action="store_true", help="Overwrite existing files")
    args = parser.parse_args()

    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(line_buffering=True)

    with args.iso.open("rb") as iso_file:
        with mmap.mmap(iso_file.fileno(), 0, access=mmap.ACCESS_READ) as data:
            game_offset, root_sector, root_size, entries = parse_entries(data)
            files = [entry for entry in entries if not entry.is_dir]
            directories = [entry for entry in entries if entry.is_dir]
            total_size = sum(entry.size for entry in files)

            print(f"Game partition offset: 0x{game_offset:X}")
            print(f"Root sector: {root_sector} ({root_size} bytes)")
            print(f"Entries: {len(files)} files, {len(directories)} directories, {format_size(total_size)}")

            if args.list:
                for entry in entries:
                    kind = "dir " if entry.is_dir else "file"
                    print(f"{kind} {entry.path} {entry.size}")
                return 0

            extract.game_offset = game_offset
            extract(data, args.output, entries, force=args.force)
            print(f"Extracted to: {args.output.resolve()}")
            return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except ExtractError as exc:
        print(f"error: {exc}", file=sys.stderr)
        raise SystemExit(1)
