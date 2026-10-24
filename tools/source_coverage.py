"""Report verified managed-source coverage and DLLs outside the captured set."""
from __future__ import annotations

import argparse
from collections import Counter
import json
from pathlib import Path
import struct

from game_snapshot import ROOT, discover_game, filesystem_path, inventory, latest, write_json


def is_managed(path: Path) -> bool:
    """Read the PE CLR directory, without loading or executing the assembly."""
    with filesystem_path(path).open("rb") as stream:
        header = stream.read(64)
        if header[:2] != b"MZ" or len(header) < 64:
            raise ValueError(f"Not a PE file: {path}")
        stream.seek(struct.unpack_from("<I", header, 60)[0])
        pe = stream.read(24)
        if pe[:4] != b"PE\0\0" or len(pe) < 24:
            raise ValueError(f"Invalid PE header: {path}")
        optional = stream.read(struct.unpack_from("<H", pe, 20)[0])
    magic = struct.unpack_from("<H", optional)[0]
    directory_offset = {0x10B: 96, 0x20B: 112}.get(magic)
    if directory_offset is None or len(optional) < directory_offset:
        raise ValueError(f"Unsupported PE optional header: {path}")
    if struct.unpack_from("<I", optional, directory_offset - 4)[0] <= 14:
        return False
    if len(optional) < directory_offset + 15 * 8:
        raise ValueError(f"Truncated PE directories: {path}")
    rva, size = struct.unpack_from("<II", optional, directory_offset + 14 * 8)
    return rva != 0 and size != 0


def report(game: Path, state: Path) -> dict:
    snapshot, manifest = latest(state)
    if inventory(game / "DSPGAME_Data/Managed") != manifest["identity"]["assemblies"]:
        raise RuntimeError("Snapshot is stale relative to the installed game.")
    counts = Counter()
    for name in manifest["files"]:
        parts = Path(name).parts
        if len(parts) > 2 and parts[0] == "source" and name.endswith(".cs"):
            counts[parts[1]] += 1
    outside = []
    walk_game = filesystem_path(game)
    binaries = sorted([*walk_game.rglob("*.dll"), *walk_game.rglob("*.exe")])
    for dll in binaries:
        relative = dll.relative_to(walk_game).as_posix()
        if relative.startswith("DSPGAME_Data/Managed/"):
            continue
        outside.append({"path": relative, "managed": is_managed(dll)})
    return {"snapshot_id": manifest["id"], "steam_build": manifest["identity"]["steam_build"],
            "decompile_scope": manifest["identity"].get("decompile_scope", "Assembly-CSharp only"),
            "managed_assemblies": len(manifest["identity"]["assemblies"]),
            "csharp_files": sum(counts.values()), "csharp_files_by_assembly": dict(sorted(counts.items())),
            "outside_managed_directory": outside,
            "uncaptured_managed_binaries": [entry["path"] for entry in outside if entry["managed"]],
            "limits": ["Managed implementation and native-call declarations only; native C++ and GPU shader code are not recovered.",
                       "C# is reconstructed by ILSpy; original assemblies remain authoritative."]}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game")
    parser.add_argument("--report", type=Path, default=ROOT / ".local/source-coverage.json")
    args = parser.parse_args()
    result = report(discover_game(args.game), ROOT / ".local")
    if not args.report.resolve().is_relative_to((ROOT / ".local").resolve()):
        raise ValueError("Coverage reports must stay under .local.")
    write_json(args.report, result)
    print(json.dumps({key: value for key, value in result.items() if key != "csharp_files_by_assembly"}, indent=2))
