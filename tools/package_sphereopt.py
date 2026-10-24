"""Build a local, deterministic SphereOpt package from the reviewed clean clone."""
from __future__ import annotations

import hashlib
import io
import json
from pathlib import Path
import subprocess
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[1]
CLONE = ROOT / "external/SphereOpt"


def command(*args: str, cwd: Path = ROOT) -> str:
    return subprocess.check_output(args, cwd=cwd, text=True, encoding="utf-8").strip()


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def archive(files: dict[str, bytes]) -> bytes:
    output = io.BytesIO()
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as package:
        for name, data in sorted(files.items()):
            info = zipfile.ZipInfo(name, (2026, 10, 23, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.create_system = 3
            info.external_attr = 0o100644 << 16
            package.writestr(info, data)
    return output.getvalue()


def main() -> None:
    if command("git", "status", "--porcelain", "--untracked-files=all", cwd=CLONE):
        raise ValueError("Commit reviewed SphereOpt changes before packaging.")
    source = command("git", "rev-parse", "HEAD", cwd=CLONE)
    subprocess.run([sys.executable, str(ROOT / "tools/game_snapshot.py"), "verify"], cwd=ROOT, check=True)
    # The build project lives in the outer workspace. Its documentation commits
    # must not become this independent mod's assembly informational revision.
    subprocess.run(["dotnet", "build", "tools/SphereOpt.OfflineBuild", "-c", "Release", "--no-restore", "-v", "minimal",
                    "-p:SourceRevisionId=" + source], cwd=ROOT, check=True)
    dll = ROOT / ".local/build/SphereOpt/SphereOpt.dll"
    subprocess.run([sys.executable, str(ROOT / "tools/game_snapshot.py"), "check-mod", str(dll)], cwd=ROOT, check=True)
    compatibility = json.loads((ROOT / ".local/compatibility.json").read_text(encoding="utf-8"))
    if compatibility["Errors"] or compatibility["CheckedInstructionContracts"] != 2:
        raise ValueError("Candidate requires passing metadata and both instruction contracts.")
    subprocess.run([sys.executable, str(ROOT / "tools/check_sphereopt_shaders.py")], cwd=ROOT, check=True)
    manifest_bytes = (CLONE / "thunderstore/manifest.json").read_bytes()
    manifest = json.loads(manifest_bytes)
    identity = json.loads(command("dotnet", str(ROOT / "tools/OfflineChecks/bin/Release/net8.0/OfflineChecks.dll"), "identity", str(dll)))
    if identity["guid"] != "SphereOpt" or identity["version"] != manifest["version_number"]:
        raise ValueError("Plugin identity/version and package manifest disagree.")
    files = {
        "SphereOpt.dll": dll.read_bytes(),
        "sphereopt-bundle": (CLONE / "thunderstore/sphereopt-bundle").read_bytes(),
        "manifest.json": manifest_bytes,
        "README.md": (CLONE / "CURRENT_GAME.md").read_bytes(),
        "LICENSE.txt": (CLONE / "LICENSE.txt").read_bytes(),
        "SHADER_PROVENANCE.json": (CLONE / "SHADER_PROVENANCE.json").read_bytes(),
        "icon.png": (CLONE / "thunderstore/icon.png").read_bytes(),
    }
    vendor = (CLONE / "thirdparty/UnityMeshSimplifier/Runtime/MeshSimplifier.cs").read_text(encoding="utf-8-sig")
    # Retain the complete supplied copyright/permission and original-author region.
    files["THIRD_PARTY_NOTICES.txt"] = vendor[:vendor.index("#if UNITY_2018")].encode("utf-8")
    if (command("git", "status", "--porcelain", "--untracked-files=all", cwd=CLONE)
            or command("git", "rev-parse", "HEAD", cwd=CLONE) != source):
        raise ValueError("Source changed while packaging.")
    latest = json.loads((ROOT / ".local/latest.json").read_text(encoding="utf-8"))
    provenance = {
        "source_commit": source, "build_source_revision": source, "snapshot_id": latest["id"],
        "version": manifest["version_number"], "plugin": identity, "unofficial": True,
        "files": {name: {"bytes": len(data), "sha256": digest(data)} for name, data in sorted(files.items())},
        "validation": {"references": compatibility["CheckedReferences"],
                       "harmony_targets": compatibility["CheckedPatchTargets"],
                       "instruction_contracts": compatibility["CheckedInstructionContracts"]},
        "limits": ["No game launch, GPU rendering test or performance benchmark was performed."],
    }
    files["provenance.json"] = (json.dumps(provenance, indent=2, sort_keys=True) + "\n").encode("utf-8")
    payload = archive(files)
    if payload != archive(files):
        raise ValueError("Package generation was not deterministic.")
    with zipfile.ZipFile(io.BytesIO(payload)) as check:
        if sorted(check.namelist()) != sorted(files) or any(check.read(name) != data for name, data in files.items()):
            raise ValueError("Package readback differs from the reviewed inputs.")
    output = ROOT / ".local/packages" / f"SphereOpt-{manifest['version_number']}-{source[:12]}.zip"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(payload)
    report = {"package": str(output), "bytes": len(payload), "sha256": digest(payload), **provenance}
    output.with_suffix(".json").write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(json.dumps({"package": str(output), "bytes": len(payload), "sha256": digest(payload)}, indent=2))


if __name__ == "__main__":
    main()
