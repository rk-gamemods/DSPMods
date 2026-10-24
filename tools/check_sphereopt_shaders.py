"""Verify the canonical shell source, runtime pin and both delivered bundles."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
CLONE = ROOT / "external/SphereOpt"
CANONICAL = CLONE / "unity/sphereopt-bundle/Assets/sphereopt-bundle/shaders/Dyson Shell Unlit Instanced.shader"


def function_hash(source: str, name: str) -> str:
    source = re.sub(r"/\*.*?\*/|//[^\n]*", "", source, flags=re.S)
    signature = re.search(r"\b(?:v2f|fout)\s+" + re.escape(name) + r"\s*\(", source)
    if signature is None:
        raise ValueError("Missing shader function " + name)
    start = source.index("{", signature.end())
    depth = 0
    for index in range(start, len(source)):
        if source[index] == "{": depth += 1
        elif source[index] == "}": depth -= 1
        if depth == 0:
            body = re.sub(r"\s+", "", source[start:index + 1])
            return hashlib.sha256(body.encode("utf-8")).hexdigest()
    raise ValueError("Unbalanced shader function " + name)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--native-sources", type=Path, help="Also compare the private compiler input bodies; does not publish them.")
    args = parser.parse_args()
    canonical = CANONICAL.read_text(encoding="utf-8-sig")
    functions = {name: function_hash(canonical, name) for name in ("vert", "frag")}
    if args.native_sources:
        for name, filename in (("vert", "shell-vs.hlsl"), ("frag", "shell-ps.hlsl")):
            native = (args.native_sources / filename).read_text(encoding="utf-8-sig")
            if function_hash(native, name) != functions[name]:
                raise ValueError("Canonical source differs from the compiled native input: " + name)
    identity = (CLONE / "ShaderBundleIdentity.cs").read_text(encoding="utf-8-sig")
    pin = re.search(r'Sha256\s*=\s*"([0-9a-f]{64})"', identity).group(1)
    bundles = (CLONE / "thunderstore/sphereopt-bundle",
               CLONE / "unity/sphereopt-bundle/Assets/StreamingAssets/AssetBundles/sphereopt-bundle")
    for bundle in bundles:
        if hashlib.sha256(bundle.read_bytes()).hexdigest() != pin:
            raise ValueError("Delivered bundle differs from the runtime pin: " + str(bundle))
    record = CLONE / "SHADER_PROVENANCE.json"
    if record.exists():
        provenance = json.loads(record.read_text(encoding="utf-8"))
        if provenance["bundle_sha256"] != pin or provenance["canonical_function_sha256"] != functions:
            raise ValueError("Shader provenance differs from source or runtime pin.")
    elif not args.native_sources:
        raise ValueError("Missing SHADER_PROVENANCE.json")
    print(json.dumps({"status": "PASS", "bundle_sha256": pin, "canonical_function_sha256": functions,
        "native_input_bodies_compared": bool(args.native_sources), "bundles": len(bundles)}, indent=2))


if __name__ == "__main__":
    main()
