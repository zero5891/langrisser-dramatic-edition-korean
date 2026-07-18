#!/usr/bin/env python3
"""Convert the verified patched MDF to a physically aligned BIN/CUE image.

The Alcohol 2448-byte MDF omits two 150-sector physical pregaps from its
main-channel stream.  Merely stripping the 96-byte subchannel area therefore
moves Track 2 by two seconds and Track 3-6 by four seconds.  Some emulators then
lose the MODE2/XA voice stream even though CDDA music still plays.

This converter recreates both omitted gaps in the BIN itself:

* 150 valid empty MODE1 sectors before Track 2
* 150 empty MODE2 sectors before Track 3

No copyrighted payload is embedded in this script.  Both gaps are generated
from the CD-ROM sector format.
"""

from __future__ import annotations

import argparse
import hashlib
import sys
from pathlib import Path


MDF_SECTOR_SIZE = 2448
BIN_SECTOR_SIZE = 2352
SOURCE_SECTORS = 278_863
OUTPUT_SECTORS = 279_163
GAP_SECTORS = 150

# Source-sector positions in the 2448-byte MDF main-channel stream.
TRACK2_SOURCE_SECTOR = 167_075
TRACK3_SOURCE_SECTOR = 235_445

# Physical file-sector positions after restoring the omitted pregaps.
TRACK2_FILE_SECTOR = 167_225
TRACK3_FILE_SECTOR = 235_745

EXPECTED_MDF_SIZE = MDF_SECTOR_SIZE * SOURCE_SECTORS
EXPECTED_PATCHED_SHA256 = (
    "407b0a858527a34de80d96e6befdfd9e751c37ddb9d74d76008147b3665f1928"
)
EXPECTED_MODE1_GAP_SHA256 = (
    "ab2480bf935e1bd21f6217aa7f689d1017ff9bee87a85c709f5457185c6ed1d8"
)
EXPECTED_MODE2_GAP_SHA256 = (
    "d70194a7c37bd7044df7a83a42e6bde9e4e1bd89e5484112b74fe6262a43034c"
)
EXPECTED_OUTPUT_SHA256 = (
    "1e7b93a820d512db65928e9c827c9f892400de3aed88f435056de3675f721726"
)
CHUNK_SIZE = 4 * 1024 * 1024
SYNC = bytes.fromhex("00ffffffffffffffffffff00")


def _make_cd_tables() -> tuple[list[int], list[int], list[int]]:
    edc_lut: list[int] = []
    ecc_f_lut: list[int] = []
    ecc_b_lut: list[int] = [0] * 256
    for value in range(256):
        edc = value
        for _ in range(8):
            edc = (edc >> 1) ^ (0xD8018001 if edc & 1 else 0)
        edc_lut.append(edc)

        forward = (value << 1) ^ (0x11D if value & 0x80 else 0)
        ecc_f_lut.append(forward)
        ecc_b_lut[value ^ forward] = value
    return edc_lut, ecc_f_lut, ecc_b_lut


EDC_LUT, ECC_F_LUT, ECC_B_LUT = _make_cd_tables()


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        while block := stream.read(CHUNK_SIZE):
            digest.update(block)
    return digest.hexdigest()


def edc_compute(data: bytes | bytearray | memoryview) -> int:
    edc = 0
    for value in data:
        edc = (edc >> 8) ^ EDC_LUT[(edc ^ value) & 0xFF]
    return edc


def ecc_compute(
    source: bytes | bytearray | memoryview,
    major_count: int,
    minor_count: int,
    major_mult: int,
    minor_inc: int,
) -> bytes:
    size = major_count * minor_count
    first = bytearray(major_count)
    second = bytearray(major_count)
    for major in range(major_count):
        index = (major >> 1) * major_mult + (major & 1)
        ecc_a = 0
        ecc_b = 0
        for _ in range(minor_count):
            value = source[index]
            index += minor_inc
            if index >= size:
                index -= size
            ecc_a ^= value
            ecc_b ^= value
            ecc_a = ECC_F_LUT[ecc_a]
        ecc_a = ECC_B_LUT[ECC_F_LUT[ecc_a] ^ ecc_b]
        first[major] = ecc_a
        second[major] = ecc_a ^ ecc_b
    return bytes(first + second)


def rebuild_mode1_sector(sector: bytearray) -> None:
    sector[2064:2068] = edc_compute(memoryview(sector)[:2064]).to_bytes(4, "little")
    sector[2068:2076] = b"\0" * 8
    sector[2076:2248] = ecc_compute(memoryview(sector)[12:2076], 86, 24, 2, 86)
    sector[2248:2352] = ecc_compute(memoryview(sector)[12:2248], 52, 43, 86, 88)


def bcd(value: int) -> int:
    return ((value // 10) << 4) | (value % 10)


def empty_mode1_sector(file_sector: int) -> bytes:
    """Return the original Japanese empty MODE1 transition sector."""
    fad = file_sector + 150
    minute, remainder = divmod(fad, 75 * 60)
    second, frame = divmod(remainder, 75)
    sector = bytearray(BIN_SECTOR_SIZE)
    sector[:12] = SYNC
    sector[12:16] = bytes((bcd(minute), bcd(second), bcd(frame), 1))
    rebuild_mode1_sector(sector)
    return bytes(sector)


def empty_mode2_sector(file_sector: int) -> bytes:
    """Return the header-only MODE2 sector used by the working disc layout."""
    fad = file_sector + 150
    minute, remainder = divmod(fad, 75 * 60)
    second, frame = divmod(remainder, 75)
    sector = bytearray(BIN_SECTOR_SIZE)
    sector[:12] = SYNC
    sector[12:16] = bytes((bcd(minute), bcd(second), bcd(frame), 2))
    return bytes(sector)


def cue_text(bin_name: str, *, track2_mode: str) -> str:
    return (
        f'FILE "{bin_name}" BINARY\n'
        "  TRACK 01 MODE1/2352\n"
        "    INDEX 01 00:00:00\n"
        f"  TRACK 02 {track2_mode}\n"
        "    INDEX 01 37:09:50\n"
        "  TRACK 03 AUDIO\n"
        "    INDEX 01 52:23:20\n"
        "  TRACK 04 AUDIO\n"
        "    INDEX 01 53:20:00\n"
        "  TRACK 05 AUDIO\n"
        "    INDEX 01 54:56:58\n"
        "  TRACK 06 AUDIO\n"
        "    INDEX 01 61:54:13\n"
    )


def convert(
    source: Path,
    output_bin: Path,
    output_cue: Path,
    output_mode1_cue: Path,
) -> None:
    unresolved = [
        value
        for value in (EXPECTED_PATCHED_SHA256,)
        if value.startswith("__")
    ]
    if unresolved:
        raise RuntimeError(
            "The v0.12.23 package MDF SHA-256 value is unresolved: "
            + ", ".join(unresolved)
        )
    if not source.is_file():
        raise FileNotFoundError(f"Patched MDF not found: {source}")
    if source.stat().st_size != EXPECTED_MDF_SIZE:
        raise ValueError(
            f"Unexpected MDF size: expected {EXPECTED_MDF_SIZE}, "
            f"got {source.stat().st_size}"
        )
    if source.resolve() in {
        output_bin.resolve(),
        output_cue.resolve(),
        output_mode1_cue.resolve(),
    }:
        raise ValueError("Input and output paths must be different.")
    if output_bin.exists() or output_cue.exists() or output_mode1_cue.exists():
        raise FileExistsError("Output BIN or CUE already exists.")

    print("Verifying patched MDF SHA-256...")
    actual_hash = sha256(source)
    if actual_hash.lower() != EXPECTED_PATCHED_SHA256:
        raise ValueError(
            "This is not the verified v0.12.23 patched MDF.\n"
            f"Expected: {EXPECTED_PATCHED_SHA256}\nActual:   {actual_hash}"
        )

    output_bin.parent.mkdir(parents=True, exist_ok=True)
    mode1_gap_hash = hashlib.sha256()
    mode2_gap_hash = hashlib.sha256()
    output_sector = 0

    print("Building physically aligned BIN...")
    try:
        with source.open("rb") as src, output_bin.open("xb") as dst:
            for source_sector in range(SOURCE_SECTORS):
                if source_sector == TRACK2_SOURCE_SECTOR:
                    if output_sector != TRACK2_SOURCE_SECTOR:
                        raise AssertionError("Track 2 pregap insertion point is wrong.")
                    for _ in range(GAP_SECTORS):
                        generated = empty_mode1_sector(output_sector)
                        dst.write(generated)
                        mode1_gap_hash.update(generated)
                        output_sector += 1
                    if output_sector != TRACK2_FILE_SECTOR:
                        raise AssertionError("Track 2 physical index is wrong.")

                if source_sector == TRACK3_SOURCE_SECTOR:
                    if output_sector != TRACK3_FILE_SECTOR - GAP_SECTORS:
                        raise AssertionError("Track 3 pregap insertion point is wrong.")
                    for _ in range(GAP_SECTORS):
                        generated = empty_mode2_sector(output_sector)
                        dst.write(generated)
                        mode2_gap_hash.update(generated)
                        output_sector += 1
                    if output_sector != TRACK3_FILE_SECTOR:
                        raise AssertionError("Track 3 physical index is wrong.")

                raw = src.read(MDF_SECTOR_SIZE)
                if len(raw) != MDF_SECTOR_SIZE:
                    raise ValueError(f"MDF ended at source sector {source_sector}.")
                dst.write(raw[:BIN_SECTOR_SIZE])
                output_sector += 1

            if src.read(1):
                raise ValueError("Unexpected trailing data in MDF.")

        if output_sector != OUTPUT_SECTORS:
            raise AssertionError(
                f"Unexpected output sector count: {output_sector} != {OUTPUT_SECTORS}"
            )
        if mode1_gap_hash.hexdigest() != EXPECTED_MODE1_GAP_SHA256:
            raise AssertionError("Generated MODE1 pregap failed its reference hash.")
        if mode2_gap_hash.hexdigest() != EXPECTED_MODE2_GAP_SHA256:
            raise AssertionError("Generated MODE2 pregap failed its reference hash.")

        output_cue.write_text(
            cue_text(output_bin.name, track2_mode="MODE2/2352"),
            encoding="ascii",
            newline="\n",
        )
        output_mode1_cue.write_text(
            cue_text(output_bin.name, track2_mode="MODE1/2352"),
            encoding="ascii",
            newline="\n",
        )
    except Exception:
        output_bin.unlink(missing_ok=True)
        output_cue.unlink(missing_ok=True)
        output_mode1_cue.unlink(missing_ok=True)
        raise

    expected_size = OUTPUT_SECTORS * BIN_SECTOR_SIZE
    if output_bin.stat().st_size != expected_size:
        output_bin.unlink(missing_ok=True)
        output_cue.unlink(missing_ok=True)
        output_mode1_cue.unlink(missing_ok=True)
        raise ValueError("Converted BIN size verification failed.")

    output_hash = sha256(output_bin)
    if (
        not EXPECTED_OUTPUT_SHA256.startswith("__")
        and output_hash != EXPECTED_OUTPUT_SHA256
    ):
        output_bin.unlink(missing_ok=True)
        output_cue.unlink(missing_ok=True)
        output_mode1_cue.unlink(missing_ok=True)
        raise ValueError(
            "Converted BIN SHA-256 verification failed.\n"
            f"Expected: {EXPECTED_OUTPUT_SHA256}\nActual:   {output_hash}"
        )

    print(f"Done: {output_bin}")
    print(f"CUE (recommended MODE2/XA Track 2): {output_cue}")
    print(f"CUE (alternate MODE1 Track 2):      {output_mode1_cue}")
    print(f"BIN SHA-256: {output_hash}")


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Convert the patched MDF to a voice-compatible physical BIN/CUE."
    )
    parser.add_argument("source", type=Path, help="v0.12.22 patched MDF")
    parser.add_argument("output_bin", type=Path, nargs="?", help="output BIN path")
    args = parser.parse_args()
    output_bin = args.output_bin or args.source.with_suffix(".bin")
    output_cue = output_bin.with_suffix(".cue")
    output_mode1_cue = output_bin.with_name(f"{output_bin.stem}_mode1.cue")
    try:
        convert(args.source, output_bin, output_cue, output_mode1_cue)
    except Exception as error:
        print(f"Error: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
