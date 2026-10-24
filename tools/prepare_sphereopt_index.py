"""Audit SphereOpt and prepare complete local text coverage alongside code indexing."""
from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tomllib

from game_snapshot import ROOT, output_path, sha256, write_json


SOURCE_SUFFIXES = {".cs", ".shader", ".compute", ".cginc", ".py"}
EXTRA_EXTENSIONS = {
    ".shader": "cpp", ".compute": "cpp", ".cginc": "cpp",
    ".asset": "yaml", ".unity": "yaml", ".asmdef": "json",
    ".manifest": "yaml", ".config": "xml", ".DotSettings": "xml",
}


def git(clone: Path, *args: str) -> str:
    return subprocess.check_output(["git", "-C", str(clone), *args], text=True).strip()


def decode_text(raw: bytes) -> tuple[str, str] | None:
    encoding = "utf-16" if raw.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8"
    if encoding == "utf-8" and b"\0" in raw:
        return None
    try:
        return raw.decode(encoding), encoding
    except UnicodeDecodeError:
        return None


def projection(path: str, digest: str, text: str) -> bytes:
    longest = max((len(match) for match in re.findall(r"`+", text)), default=0)
    fence = "`" * max(3, longest + 1)
    return (f"# {path}\n\nOriginal path: `{path}`\n\nSHA-256: `{digest}`\n\n"
            f"{fence}\n{text}\n{fence}\n").encode("utf-8")


def prepare(clone: Path, state: Path) -> dict:
    clone = clone.resolve()
    if git(clone, "status", "--porcelain=v1", "--untracked-files=all"):
        raise RuntimeError("Resolve changes in the independent clone before indexing it.")
    revision = git(clone, "rev-parse", "HEAD")
    tracked = set(git(clone, "ls-files", "-z").split("\0")) - {""}
    actual = {p.relative_to(clone).as_posix() for p in clone.rglob("*")
              if p.is_file() and ".git" not in p.relative_to(clone).parts}
    if actual != tracked:
        raise RuntimeError("Clone contains missing or ignored/untracked files; audit them before claiming complete coverage.")
    destination = output_path(state, "SphereOptText-" + revision[:12])
    destination.mkdir(exist_ok=True)
    records = []
    for name in sorted(tracked):
        source = clone / name
        if source.is_symlink() or not source.resolve().is_relative_to(clone):
            raise RuntimeError("Refusing linked clone source: " + name)
        raw = source.read_bytes()
        digest = hashlib.sha256(raw).hexdigest()
        decoded = decode_text(raw)
        record = {"path": name, "sha256": digest, "bytes": len(raw),
                  "implementation_source": source.suffix.lower() in SOURCE_SUFFIXES,
                  "kind": "text" if decoded else "binary"}
        if decoded:
            text, encoding = decoded
            target_name = hashlib.sha256(name.encode("utf-8")).hexdigest()[:24] + ".md"
            target = output_path(destination, target_name)
            expected = projection(name, digest, text)
            if target.exists() and target.read_bytes() != expected:
                raise RuntimeError("Existing projection differs: " + str(target))
            if not target.exists():
                target.write_bytes(expected)
            record.update(encoding=encoding, document=target_name, document_sha256=sha256(target))
        records.append(record)
    catalog = "# SphereOpt binary artifacts\n\nThese are inventoried, not represented as recovered source.\n\n"
    catalog += "\n".join(f"- `{r['path']}`: {r['bytes']} bytes; SHA-256 `{r['sha256']}`."
                         for r in records if r["kind"] == "binary") + "\n"
    catalog_file = output_path(destination, "binary-artifacts.md")
    if catalog_file.exists() and catalog_file.read_bytes() != catalog.encode("utf-8"):
        raise RuntimeError("Existing binary catalog differs.")
    if not catalog_file.exists():
        catalog_file.write_bytes(catalog.encode("utf-8"))
    expected_files = {r["document"] for r in records if r["kind"] == "text"} | {"binary-artifacts.md"}
    if {p.name for p in destination.iterdir() if p.is_file()} != expected_files:
        raise RuntimeError("Unexpected files in the text projection.")
    if not (destination / ".git").exists():
        subprocess.run(["git", "init", "--initial-branch=local", str(destination)], check=True,
                       stdout=subprocess.DEVNULL)
    if git(destination, "remote"):
        raise RuntimeError("Private text projections must have no remote.")
    if git(clone, "status", "--porcelain=v1", "--untracked-files=all") or git(clone, "rev-parse", "HEAD") != revision:
        raise RuntimeError("SphereOpt changed while preparing index inputs.")
    result = {"schema": 1, "source_commit": revision, "source_root": str(clone),
              "text_index_root": str(destination), "text_index_name": "SphereOpt-fulltext-" + revision[:12],
              "tracked_files": len(records), "text_files": sum(r["kind"] == "text" for r in records),
              "binary_files": sum(r["kind"] == "binary" for r in records),
              "implementation_files": sum(r["implementation_source"] for r in records),
              "source_suffix_counts": dict(Counter(Path(r["path"]).suffix for r in records if r["implementation_source"])),
              "text_documents": len(expected_files), "extra_extensions": EXTRA_EXTENSIONS,
              "binary_catalog_sha256": sha256(catalog_file),
              "syntax_limit": "ShaderLab/HLSL use an approximate C++ grammar; all shader text is retained, but shader AST coverage is not guaranteed.",
              "files": records}
    write_json(output_path(state, "sphereopt-index-coverage.json"), result)
    return result


def verify_inventory(manifest: dict, observed: dict) -> dict:
    expected_code = {r["path"] for r in manifest["files"] if r["implementation_source"]}
    expected_text = {r["document"]: r["document_sha256"] for r in manifest["files"] if r["kind"] == "text"}
    expected_text["binary-artifacts.md"] = manifest["binary_catalog_sha256"]
    if observed["code_repo"] != "andyoneal/SphereOpt" or observed["text_repo"] != "local/" + manifest["text_index_name"]:
        raise RuntimeError("Observed indexes do not identify the expected SphereOpt sources.")
    if expected_code - set(observed["code_files"]):
        raise RuntimeError("Implementation source is missing from the code index.")
    if set(observed["text_docs"]) != set(expected_text):
        raise RuntimeError("Text index document inventory does not match the clone.")
    readback = observed.get("body_readback", {})
    for name, digest in expected_text.items():
        entry = readback.get(name, {})
        if (entry.get("content_hash") != digest or not entry.get("hash_verified")
                or not entry.get("source_hash_verified")):
            raise RuntimeError("Text index content was not verified for " + name)
    return {"code_repo": observed["code_repo"], "code_files": len(observed["code_files"]),
            "implementation_files_covered": len(expected_code), "text_repo": observed["text_repo"],
            "text_files_covered": manifest["text_files"], "verified_document_bodies": len(expected_text),
            "binary_files_cataloged": manifest["binary_files"]}


def index_code(clone: Path, state: Path) -> None:
    config = tomllib.loads((Path.home() / ".codex/config.toml").read_text(encoding="utf-8"))
    server = config["mcp_servers"]["jcodemunch"]
    environment = dict(os.environ)
    environment.update(server.get("env", {}))
    receipt = output_path(state, "sphereopt-code-index.json")
    with receipt.open("w", encoding="utf-8") as stream:
        subprocess.run([server["command"], str(Path(__file__).resolve()), "--worker-index-code"], env=environment,
                       cwd=ROOT, stdout=stream, check=True)
    print("Code-index receipt:", receipt, flush=True)


def worker_index_code(clone: Path, state: Path) -> None:
    # Use the configured runtime's public Python API. Environment overrides are
    # fallback-only in Munch, so they cannot override an explicit global mapping.
    # A local config copy preserves other settings while changing this run only.
    from importlib.metadata import version
    from jcodemunch_mcp import config

    storage = Path(os.environ.get("CODE_INDEX_PATH", str(Path.home() / ".code-index")))
    original_config = storage / "config.jsonc"
    config.load_config(create_missing=False)
    extensions = dict(config.get("extra_extensions", {}))
    extensions.update(EXTRA_EXTENSIONS)
    local_config = output_path(state, "sphereopt-code-config")
    local_config.mkdir(exist_ok=True)
    content = original_config.read_text(encoding="utf-8-sig") if original_config.exists() else "{}"
    output_path(local_config, "config.jsonc").write_text(config.set_key(content, "extra_extensions", extensions), encoding="utf-8")
    config.load_config(storage_path=str(local_config), create_missing=False)
    from jcodemunch_mcp.tools.index_folder import index_folder

    result = index_folder(str(clone), use_ai_summaries=False, storage_path=str(storage),
                          incremental=False, identity_mode="git")
    result["runtime_version"] = version("jcodemunch-mcp")
    print(json.dumps(result, indent=2))
    if not result.get("success"):
        raise RuntimeError("Code indexing failed; see the receipt.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--index-code", action="store_true")
    parser.add_argument("--worker-index-code", action="store_true", help=argparse.SUPPRESS)
    parser.add_argument("--verify-index-inventory", type=Path)
    args = parser.parse_args()
    clone, state = ROOT / "external/SphereOpt", ROOT / ".local"
    if args.worker_index_code:
        worker_index_code(clone, state)
    else:
        result = prepare(clone, state)
        print(json.dumps({key: value for key, value in result.items() if key != "files"}, indent=2), flush=True)
        if args.index_code:
            index_code(clone, state)
        if args.verify_index_inventory:
            result["verified_indexes"] = verify_inventory(result, json.loads(args.verify_index_inventory.read_text(encoding="utf-8")))
            write_json(output_path(state, "sphereopt-index-coverage.json"), result)
            print(json.dumps(result["verified_indexes"], indent=2))
