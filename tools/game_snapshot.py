"""Capture read-only DSP inputs and run metadata checks against verified snapshots."""
from __future__ import annotations

import argparse
from contextlib import contextmanager
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import uuid
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[1]
SCHEMA = 2
ILSPY = ["dotnet", "tool", "run", "ilspycmd", "--"]


def filesystem_path(path: Path) -> Path:
    """Keep Windows' legacy path limit out of recursive source verification."""
    path = path.resolve()
    if os.name == "nt" and not str(path).startswith("\\\\?\\"):
        value = str(path)
        return Path("\\\\?\\UNC\\" + value[2:] if value.startswith("\\\\") else "\\\\?\\" + value)
    return path


def sha256(path: Path) -> str:
    with filesystem_path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def atomic_write(path: Path, content: bytes) -> None:
    temporary = path.with_name(path.name + ".tmp-" + uuid.uuid4().hex)
    try:
        temporary.write_bytes(content)
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def write_json(path: Path, value: object) -> None:
    atomic_write(path, (json.dumps(value, indent=2, sort_keys=True) + "\n").encode("utf-8"))


def output_path(state: Path, relative: str) -> Path:
    path = state
    for part in Path(relative).parts:
        path /= part
        if path.is_symlink() or path.is_junction():
            raise RuntimeError(f"Refusing linked capture output: {path}")
    if not path.resolve().is_relative_to(state.resolve()):
        raise RuntimeError("Capture output escapes the state root.")
    return path


@contextmanager
def capture_lock(state: Path):
    state.mkdir(parents=True, exist_ok=True)
    with output_path(state, "capture.lock").open("a+b") as stream:
        stream.seek(0, os.SEEK_END)
        if stream.tell() == 0:
            stream.write(b"0")
            stream.flush()
        stream.seek(0)
        try:
            if os.name == "nt":
                import msvcrt
                msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl
                fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError as exc:
            raise RuntimeError("Another capture holds the snapshot lock.") from exc
        try:
            yield
        finally:
            stream.seek(0)
            if os.name == "nt":
                msvcrt.locking(stream.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                fcntl.flock(stream, fcntl.LOCK_UN)


def discover_game(override: str | None = None) -> Path:
    if override:
        candidates = [Path(override)]
    else:
        libraries = {Path("C:/Steam")}
        if os.name == "nt":
            import winreg
            try:
                with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"Software\Valve\Steam") as key:
                    libraries.add(Path(winreg.QueryValueEx(key, "SteamPath")[0]))
            except OSError:
                pass
        for library in list(libraries):
            vdf = library / "steamapps/libraryfolders.vdf"
            if vdf.is_file():
                for value in re.findall(r'"path"\s+"([^"]+)"', vdf.read_text(encoding="utf-8")):
                    path = Path(value.replace("\\\\", "\\"))
                    if path.is_absolute():
                        libraries.add(path)
        candidates = [p / "steamapps/common/Dyson Sphere Program" for p in sorted(libraries)]
    found = [p.resolve() for p in candidates if (p / "DSPGAME_Data/Managed/Assembly-CSharp.dll").is_file()]
    if len(found) != 1:
        raise RuntimeError(f"Expected one installed DSP game; found {len(found)}. Supply --game explicitly.")
    return found[0]


def inventory(managed: Path) -> list[dict]:
    result = []
    for path in sorted(managed.glob("*.dll")):
        if path.is_symlink() or path.resolve().parent != managed.resolve():
            raise RuntimeError(f"Refusing linked assembly: {path}")
        result.append({"name": path.name, "size": path.stat().st_size, "sha256": sha256(path)})
    if not any(x["name"] == "Assembly-CSharp.dll" for x in result):
        raise RuntimeError("Assembly-CSharp.dll is missing.")
    return result


def steam_build(game: Path) -> str:
    manifest = game.parent.parent / "appmanifest_1366540.acf"
    if manifest.is_file():
        match = re.search(r'"buildid"\s+"(\d+)"', manifest.read_text(encoding="utf-8"))
        if match:
            return match.group(1)
    return "unknown"


def decompile(managed: Path, output: Path) -> None:
    output = filesystem_path(output)
    output.mkdir()
    assemblies = sorted(managed.glob("*.dll"))
    with (output.parent / "decompile.log").open("w", encoding="utf-8") as log:
        for number, assembly in enumerate(assemblies, 1):
            destination = output / assembly.stem
            print(f"Decompiling {number}/{len(assemblies)}: {assembly.name}", flush=True)
            log.write(f"\n=== {assembly.name} ===\n")
            log.flush()
            subprocess.run(ILSPY + ["--disable-updatecheck", "-p", "--nested-directories", "-r", str(managed),
                                   "-o", str(destination), str(assembly)],
                           cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True)
            if not any(destination.glob("*.csproj")):
                raise RuntimeError(f"Decompiler produced no project for {assembly.name}.")


def verify_snapshot(snapshot: Path, expected_id: str | None = None) -> dict:
    snapshot = filesystem_path(snapshot)
    manifest = json.loads((snapshot / "manifest.json").read_text(encoding="utf-8"))
    encoded = json.dumps(manifest["identity"], sort_keys=True).encode()
    identity = hashlib.sha256(encoded).hexdigest()
    if (manifest.get("schema") not in {1, SCHEMA}
            or manifest["identity"].get("schema") != manifest["schema"]
            or identity != manifest.get("id") or (expected_id and identity != expected_id)):
        raise RuntimeError("Snapshot identity or schema mismatch.")
    if manifest["schema"] == 2 and manifest["identity"].get("decompile_scope") != "all-managed":
        raise RuntimeError("Schema 2 requires all-managed decompilation scope.")
    expected_files = manifest["files"]
    if not expected_files:
        raise RuntimeError("Snapshot file manifest is empty.")
    actual_files = {p.relative_to(snapshot).as_posix() for p in snapshot.rglob("*") if p.is_file()
                    and p.name not in {"manifest.json", "decompile.log"}}
    if actual_files != set(expected_files):
        raise RuntimeError("Snapshot file set changed.")
    for relative, digest in expected_files.items():
        path = snapshot / relative
        if path.is_symlink() or not path.resolve().is_relative_to(snapshot) or sha256(path) != digest:
            raise RuntimeError(f"Snapshot file failed verification: {relative}")
    for item in manifest["identity"]["assemblies"]:
        if expected_files.get("Managed/" + item["name"]) != item["sha256"]:
            raise RuntimeError("Captured assembly does not match input identity.")
        if manifest["schema"] == 2:
            prefix = "source/" + Path(item["name"]).stem + "/"
            if not any(name.startswith(prefix) and name.endswith(".csproj") for name in expected_files):
                raise RuntimeError(f"Missing decompiled project for {item['name']}.")
    return manifest


def publish_latest(state: Path, snapshot: Path, manifest: dict) -> None:
    pointer = output_path(state, "latest.json")
    properties = output_path(state, "GameSnapshot.props")
    managed = escape(str(snapshot / "Managed"))
    props = f'<Project><PropertyGroup><DSPManagedDir>{managed}</DSPManagedDir></PropertyGroup></Project>\n'
    previous_props = properties.read_bytes() if properties.exists() else None
    atomic_write(properties, props.encode("utf-8"))
    try:
        write_json(pointer, {"id": manifest["id"], "snapshot": snapshot.relative_to(state).as_posix()})
    except BaseException:
        if previous_props is None:
            properties.unlink(missing_ok=True)
        else:
            atomic_write(properties, previous_props)
        raise


def capture(game: Path, state: Path, *, version: str, runner=decompile) -> dict:
    game, state = game.resolve(), state.resolve()
    if state.is_relative_to(game) or game.is_relative_to(state):
        raise RuntimeError("Capture state and game installation must not overlap.")
    for directory in ("staging", "snapshots"):
        output_path(state, directory)
    with capture_lock(state):
        managed = game / "DSPGAME_Data/Managed"
        original = inventory(managed)
        identity = {"schema": SCHEMA, "assemblies": original, "steam_build": steam_build(game), "ilspy": version,
                    "decompile_scope": "all-managed"}
        snapshot_id = hashlib.sha256(json.dumps(identity, sort_keys=True).encode()).hexdigest()
        snapshot = output_path(state, "snapshots/" + snapshot_id)
        if snapshot.exists():
            manifest = verify_snapshot(snapshot, snapshot_id)
            if inventory(managed) != original:
                raise RuntimeError("Installed assemblies changed during verification.")
            publish_latest(state, snapshot, manifest)
            return {"status": "reused", "snapshot": str(snapshot), "id": snapshot_id}
        staging_root = output_path(state, "staging")
        staging_root.mkdir(exist_ok=True)
        stage = staging_root / uuid.uuid4().hex
        stage.mkdir()
        try:
            copied = stage / "Managed"
            copied.mkdir()
            print(f"Capturing {len(original)} managed assemblies, Steam build {identity['steam_build']}.", flush=True)
            for item in original:
                shutil.copy2(managed / item["name"], copied / item["name"])
            if inventory(copied) != original:
                raise RuntimeError("Assembly bytes changed while copying.")
            runner(copied, stage / "source")
            if inventory(managed) != original:
                raise RuntimeError("Installed assemblies changed during decompilation; retry after the update.")
            walk_stage = filesystem_path(stage)
            files = {p.relative_to(walk_stage).as_posix(): sha256(p) for p in walk_stage.rglob("*")
                     if p.is_file() and p.name != "decompile.log"}
            manifest = {"schema": SCHEMA, "id": snapshot_id, "identity": identity,
                        "created_utc": datetime.now(timezone.utc).isoformat(), "files": files}
            write_json(stage / "manifest.json", manifest)
            verify_snapshot(stage, snapshot_id)
            snapshot.parent.mkdir(exist_ok=True)
            stage.rename(snapshot)
            verify_snapshot(snapshot, snapshot_id)
            publish_latest(state, snapshot, manifest)
            return {"status": "created", "snapshot": str(snapshot), "id": snapshot_id}
        finally:
            if stage.exists():
                if stage.is_symlink() or stage.resolve().parent != staging_root.resolve():
                    raise RuntimeError("Refusing unexpected staging cleanup path.")
                shutil.rmtree(filesystem_path(stage))


def latest(state: Path, *, managed: Path | None = None) -> tuple[Path, dict]:
    pointer = json.loads((state / "latest.json").read_text(encoding="utf-8"))
    path = state / pointer["snapshot"]
    if path.is_symlink() or path.resolve().parent != (state / "snapshots").resolve():
        raise RuntimeError("Latest pointer escapes the snapshot root.")
    manifest = verify_snapshot(path, pointer["id"])
    if managed is not None and managed.resolve() != (path / "Managed").resolve():
        raise RuntimeError("Compiler input directory does not match the verified snapshot.")
    expected_props = f'<Project><PropertyGroup><DSPManagedDir>{escape(str(path / "Managed"))}</DSPManagedDir></PropertyGroup></Project>\n'
    if (state / "GameSnapshot.props").read_text(encoding="utf-8") != expected_props:
        raise RuntimeError("Build properties do not match latest snapshot. Run capture to repair them.")
    return path, manifest


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["capture", "verify", "check-mod"])
    parser.add_argument("mod", nargs="?")
    parser.add_argument("--game")
    parser.add_argument("--managed", type=Path, help="Require compiler inputs to match the verified snapshot.")
    parser.add_argument("--state-dir", type=Path, default=ROOT / ".local")
    args = parser.parse_args()
    state = args.state_dir.resolve()
    if args.action == "capture":
        version = subprocess.check_output(ILSPY + ["--version"], cwd=ROOT, text=True).strip()
        result = capture(discover_game(args.game), state, version=version)
    else:
        snapshot, manifest = latest(state, managed=args.managed)
        if inventory(discover_game(args.game) / "DSPGAME_Data/Managed") != manifest["identity"]["assemblies"]:
            raise RuntimeError("Snapshot is stale relative to the installed game. Run capture.")
        if args.action == "verify":
            result = {"status": "verified", "id": manifest["id"], "snapshot": str(snapshot)}
        else:
            if not args.mod:
                parser.error("check-mod needs a plugin DLL path")
            subprocess.run(["dotnet", "run", "--project", str(ROOT / "tools/OfflineChecks"), "-c", "Release",
                            "--no-build", "--", "check", "--managed", str(snapshot / "Managed"),
                            "--mod", str(Path(args.mod).resolve()), "--report", str(state / "compatibility.json")],
                           cwd=ROOT, check=True)
            result = {"report": str(state / "compatibility.json"), "id": manifest["id"]}
    print(json.dumps(result, indent=2))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, RuntimeError, subprocess.CalledProcessError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        raise SystemExit(1)
