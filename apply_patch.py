#!/usr/bin/env python3
"""Apply the Korean v0.13.13 patch to the verified retail MDF."""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import struct
import sys
import tempfile
from pathlib import Path


PATCH_NAME = "langrisser_de_ko_v0.13.13.ldp"
PATCH_SHA256 = "db15e2790cf6fbe940f48dc0bc910e56e93067d771823900005eef3994ba6abc"
SOURCE_SHA256 = "1a9d479d3238bd1932fe2faee0c2b146c6333127a5b39d83e7d3d81a067505c1"
TARGET_SHA256 = "9b7c2919a1587ea755eab251ee22422cae648dd247bef5c55b1628353669db93"
EXPECTED_SIZE = 682_656_624
EXPECTED_PATCH_SIZE = 7_515_082
EXPECTED_RECORD_COUNT = 70_470
EXPECTED_REPLACEMENT_BYTES = 6_669_110
MDF_SECTOR_SIZE = 2_448
MAIN_CHANNEL_SIZE = 2_352
TRACK2_SOURCE_SECTOR = 167_075
MAX_MANIFEST_SIZE = 64 * 1024
MAGIC = b"LDP1"
END_OFFSET = 0xFFFFFFFFFFFFFFFF
CHUNK_SIZE = 4 * 1024 * 1024


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        while block := stream.read(CHUNK_SIZE):
            digest.update(block)
    return digest.hexdigest()


def apply(source: Path, output: Path) -> None:
    if not source.is_file():
        raise FileNotFoundError(f"Input MDF not found: {source}")
    if source.stat().st_size != EXPECTED_SIZE:
        raise ValueError(f"Unexpected MDF size: {source.stat().st_size}")
    if sha256(source).lower() != SOURCE_SHA256:
        raise ValueError("Input must be the verified retail MDF (SHA-256 mismatch).")
    if source.resolve() == output.resolve():
        raise ValueError("Output must be different from the input MDF.")
    if output.exists():
        raise FileExistsError(f"Output already exists: {output}")

    patch_path = Path(__file__).resolve().parent / PATCH_NAME
    if not patch_path.is_file():
        raise FileNotFoundError(f"Patch file not found: {patch_path}")
    if patch_path.stat().st_size != EXPECTED_PATCH_SIZE:
        raise ValueError(f"Unexpected patch size: {patch_path.stat().st_size}")
    patch_hash = sha256(patch_path)
    if patch_hash.lower() != PATCH_SHA256:
        raise ValueError(f"Patch SHA-256 mismatch: {patch_hash}")

    with patch_path.open("rb") as patch:
        if patch.read(4) != MAGIC:
            raise ValueError("Not an LDP1 patch file.")
        raw_length = patch.read(4)
        if len(raw_length) != 4:
            raise ValueError("Truncated LDP1 header.")
        manifest_length = struct.unpack("<I", raw_length)[0]
        if not 1 <= manifest_length <= MAX_MANIFEST_SIZE:
            raise ValueError(f"Invalid LDP1 manifest size: {manifest_length}")
        manifest_raw = patch.read(manifest_length)
        if len(manifest_raw) != manifest_length:
            raise ValueError("Truncated LDP1 manifest.")
        manifest = json.loads(manifest_raw.decode("utf-8"))
        if manifest.get("format") != "LDP1" or manifest.get("version") != "v0.13.13":
            raise ValueError("Patch format/version metadata mismatch.")
        if manifest.get("source_sha256", "").lower() != SOURCE_SHA256:
            raise ValueError("Patch source metadata mismatch.")
        if manifest.get("target_sha256", "").lower() != TARGET_SHA256:
            raise ValueError("Patch target metadata mismatch.")
        if manifest.get("expected_size") != EXPECTED_SIZE:
            raise ValueError("Patch size metadata mismatch.")
        if manifest.get("record_count") != EXPECTED_RECORD_COUNT:
            raise ValueError("Patch record-count metadata mismatch.")
        if manifest.get("replacement_bytes") != EXPECTED_REPLACEMENT_BYTES:
            raise ValueError("Patch replacement-byte metadata mismatch.")
        if manifest.get("contains_full_disc_image") is not False:
            raise ValueError("Patch payload policy metadata mismatch.")

        output.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.NamedTemporaryFile(
            prefix=f".{output.name}.", suffix=".partial", dir=output.parent, delete=False
        ) as temporary:
            partial = Path(temporary.name)
        try:
            shutil.copyfile(source, partial)
            record_count = 0
            replacement_bytes = 0
            previous_end = 0
            with partial.open("r+b") as result:
                while True:
                    header = patch.read(12)
                    if len(header) != 12:
                        raise ValueError("Truncated LDP1 record header.")
                    offset, length = struct.unpack("<QI", header)
                    if offset == END_OFFSET:
                        if length != 0:
                            raise ValueError("Invalid LDP1 end marker.")
                        break
                    if length <= 0 or offset < previous_end:
                        raise ValueError("Invalid or overlapping LDP1 record.")
                    end = offset + length
                    if end > EXPECTED_SIZE:
                        raise ValueError("LDP1 record exceeds the MDF size.")
                    in_sector = offset % MDF_SECTOR_SIZE
                    if in_sector + length > MAIN_CHANNEL_SIZE:
                        raise ValueError("LDP1 record reaches protected subchannel data.")
                    if end > TRACK2_SOURCE_SECTOR * MDF_SECTOR_SIZE:
                        raise ValueError("LDP1 record reaches Track 2 or later.")
                    replacement = patch.read(length)
                    if len(replacement) != length:
                        raise ValueError("Truncated LDP1 replacement data.")
                    result.seek(offset)
                    result.write(replacement)
                    record_count += 1
                    replacement_bytes += length
                    previous_end = end
            if patch.read(1):
                raise ValueError("Unexpected trailing data after the LDP1 end marker.")
            if record_count != EXPECTED_RECORD_COUNT:
                raise ValueError(f"Unexpected LDP1 record count: {record_count}")
            if replacement_bytes != EXPECTED_REPLACEMENT_BYTES:
                raise ValueError(
                    f"Unexpected LDP1 replacement byte count: {replacement_bytes}"
                )
            actual = sha256(partial)
            if actual.lower() != TARGET_SHA256:
                raise ValueError(f"Patched MDF hash mismatch: {actual}")
            partial.rename(output)
        except Exception:
            partial.unlink(missing_ok=True)
            raise

    print(f"Done: {output}")
    print(f"SHA-256: {TARGET_SHA256}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path, help="verified retail MDF")
    parser.add_argument("output", type=Path, nargs="?", help="output Korean v0.13.13 MDF")
    args = parser.parse_args()
    output = args.output or args.source.with_name(
        "langDramaticEdition_ko_v0.13.13.mdf"
    )
    try:
        apply(args.source, output)
    except Exception as error:
        print(f"Error: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
