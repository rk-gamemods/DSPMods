"""Prepare a verified, private, independently identified source tree for Munch."""
from __future__ import annotations

import json
from pathlib import Path
import shutil
import subprocess

from game_snapshot import ROOT, filesystem_path, latest, output_path, sha256, write_json


def prepare(state: Path) -> dict:
    snapshot, manifest = latest(state)
    if manifest["identity"].get("decompile_scope") != "all-managed":
        raise RuntimeError("Run the all-managed capture before preparing a source index.")
    destination = output_path(state, "DSPManaged-" + manifest["id"][:12])
    marker = output_path(state, destination.name + ".json")
    if destination.is_symlink() or destination.is_junction():
        raise RuntimeError("Refusing a linked index tree.")
    expected = {name.removeprefix("source/"): digest for name, digest in manifest["files"].items()
                if name.startswith("source/")}
    if not destination.exists():
        shutil.copytree(filesystem_path(snapshot / "source"), filesystem_path(destination))
    walk_root = filesystem_path(destination)
    actual = {path.relative_to(walk_root).as_posix(): sha256(path) for path in walk_root.rglob("*")
              if path.is_file() and ".git" not in path.relative_to(walk_root).parts}
    if actual != expected:
        raise RuntimeError("Index input differs from the verified snapshot; refusing to replace it.")
    git_metadata = destination / ".git"
    if git_metadata.is_symlink() or git_metadata.is_junction():
        raise RuntimeError("Refusing linked private Git metadata.")
    if git_metadata.exists() and not git_metadata.is_dir():
        raise RuntimeError("Private Git metadata must be a directory, not a worktree redirect.")
    if not git_metadata.exists():
        subprocess.run(["git", "init", "--initial-branch=local", str(destination)], check=True,
                       stdout=subprocess.DEVNULL)
    remotes = subprocess.check_output(["git", "-C", str(destination), "remote"], text=True).strip()
    if remotes:
        raise RuntimeError("The private index-input repository must have no remote.")
    result = {"snapshot_id": manifest["id"], "source_root": str(destination),
              "managed_assemblies": len(manifest["identity"]["assemblies"]),
              "csharp_files": sum(name.endswith(".cs") for name in expected),
              "source_files": len(expected), "status": "verified"}
    write_json(marker, result)
    return result


if __name__ == "__main__":
    print(json.dumps(prepare(ROOT / ".local"), indent=2))
