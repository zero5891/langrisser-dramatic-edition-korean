#!/usr/bin/env python3
"""Apply the Korean v0.12.24 patch to the verified retail MDF."""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import struct
import sys
from pathlib import Path


PATCH_NAME = "langrisser_de_ko_v0.12.24.ldp"
SOURCE_SHA256 = "1a9d479d3238bd1932fe2faee0c2b146c6333127a5b39d83e7d3d81a067505c1"
TARGET_SHA256 = "3d3a8d0c8da43082f012f9af57b6f60a987c292fca8b568a52861321af54e714"
EXPECTED_SIZE = 682_656_624
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
    with patch_path.open("rb") as patch:
        if patch.read(4) != MAGIC:
            raise ValueError("Not an LDP1 patch file.")
        raw_length = patch.read(4)
        if len(raw_length) != 4:
            raise ValueError("Truncated LDP1 header.")
        manifest_length = struct.unpack("<I", raw_length)[0]
        manifest = json.loads(patch.read(manifest_length).decode("utf-8"))
        if manifest.get("source_sha256", "").lower() != SOURCE_SHA256:
            raise ValueError("Patch source metadata mismatch.")
        if manifest.get("target_sha256", "").lower() != TARGET_SHA256:
            raise ValueError("Patch target metadata mismatch.")

        output.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, output)
        try:
            with output.open("r+b") as result:
                while True:
                    header = patch.read(12)
                    if len(header) != 12:
                        raise ValueError("Truncated LDP1 record header.")
                    offset, length = struct.unpack("<QI", header)
                    if offset == END_OFFSET and length == 0:
                        break
                    replacement = patch.read(length)
                    if len(replacement) != length:
                        raise ValueError("Truncated LDP1 replacement data.")
                    result.seek(offset)
                    result.write(replacement)
            actual = sha256(output)
            if actual.lower() != TARGET_SHA256:
                raise ValueError(f"Patched MDF hash mismatch: {actual}")
        except Exception:
            output.unlink(missing_ok=True)
            raise

    print(f"Done: {output}")
    print(f"SHA-256: {TARGET_SHA256}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path, help="verified retail MDF")
    parser.add_argument("output", type=Path, nargs="?", help="output Korean v0.12.24 MDF")
    args = parser.parse_args()
    output = args.output or args.source.with_name(
        "langDramaticEdition_ko_v0.12.24.mdf"
    )
    try:
        apply(args.source, output)
    except Exception as error:
        print(f"Error: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
