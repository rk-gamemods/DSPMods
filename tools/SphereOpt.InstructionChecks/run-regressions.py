"""Run the compiled metadata checker with real inputs; write evidence under .local."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def require_safe_summary(path, inputs):
    require(not path.is_symlink(), "Regression summary must not be a symbolic link")
    for key, source in inputs.items():
        same = path.resolve() == source
        if path.exists() and source.exists():
            same = same or path.samefile(source)
        require(not same, f"Regression output collides with input {key}")


def publish_summary(path, data, session, inputs):
    require_safe_summary(path, inputs)
    staged = session / "summary.pending.json"
    staged.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    os.replace(staged, path)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for option in ("checker", "assembly", "plugin", "dsp-optimizations"):
        parser.add_argument("--" + option, type=Path, required=True)
    parser.add_argument("--legacy-plugin", type=Path)
    parser.add_argument("--upstream-license", type=Path)
    parser.add_argument("--report-dir", type=Path, default=Path(".local/build/InstructionChecks/regression"))
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    output = args.report_dir.resolve()
    require(output.is_relative_to(root / ".local"), "Regression evidence must stay under workspace .local")
    inputs = {key: value.resolve() for key, value in vars(args).items() if isinstance(value, Path) and key != "report_dir"}
    inputs["lockfile"] = Path(__file__).with_name("packages.lock.json").resolve()
    inputs["color_source"] = root / "external/SphereOpt/ColorStoreRewriter.cs"
    inputs["cursor_source"] = root / "external/SphereOpt/ShellCursorRewriter.cs"
    notice = Path(__file__).with_name("THIRD_PARTY_NOTICES.md").resolve()
    inputs["notice"] = notice
    inputs["delivered_notice"] = inputs["checker"].parent / notice.name
    before = {key: sha256(path) for key, path in inputs.items()}
    require(before["notice"] == before["delivered_notice"], "Checker output must include the current third-party notice")
    if args.upstream_license:
        copied = notice.read_text(encoding="utf-8-sig")
        start = copied.find("MIT License\n")
        require(start >= 0 and copied[start:].rstrip() == inputs["upstream_license"].read_text(encoding="utf-8-sig").rstrip(),
                "Copied MIT license differs from upstream")
    summary_path = output / "regression-result.json"
    require_safe_summary(summary_path, inputs)
    output.mkdir(parents=True, exist_ok=True)
    # Never accept a previous invocation's proof files or remove existing files
    # merely to establish freshness. Each run owns a newly created directory.
    session = Path(tempfile.mkdtemp(prefix="run-", dir=output))

    def run(extra, expected):
        result = subprocess.run(["dotnet", str(inputs["checker"]), "--assembly", str(inputs["assembly"]), *extra],
                                cwd=root, capture_output=True, text=True, encoding="utf-8")
        require(result.returncode == expected,
                f"Unexpected checker exit {result.returncode}: {(result.stderr or result.stdout)[:1500]}")
        return result

    def inputs_unchanged():
        require(all(sha256(path) == before[key] for key, path in inputs.items()), "A read-only regression input changed")

    publish_summary(summary_path, {"status": "running", "evidenceDirectory": str(session)}, session, inputs)
    try:
        extra = ["--plugin", str(inputs["plugin"]), "--dsp-optimizations", str(inputs["dsp_optimizations"])]
        reports = [session / "candidate-proof.json", session / "candidate-proof.repeat.json"]
        for report in reports:
            run([*extra, "--report", str(report)], 0)
            require(report.is_file() and not report.is_symlink() and report.stat().st_size <= 32768,
                    "Checker did not produce a fresh bounded report: " + str(report))
        require(reports[0].read_bytes() == reports[1].read_bytes(), "Repeated identical checker reports differ")
        proof = json.loads(reports[0].read_text(encoding="utf-8"))
        require(proof["status"] == "passed", "Checker report did not pass")
        require(proof["patchOrdering"]["status"] == "passed", "Actual candidate ordering was skipped")
        require(proof["stackInteroperation"]["installedSourceEquivalence"]["status"] == "passed", "Installed equivalence was skipped")
        require(proof["stackInteroperation"]["drawModels"]["permutationsPassed"] == 6, "DrawModels matrix was incomplete")
        negative = run(["--assembly", str(inputs["assembly"])], 1)
        (session / "duplicate-option-negative.json").write_text(negative.stderr, encoding="utf-8")
        require("Usage:" in json.loads(negative.stderr)["error"], "Duplicate argument failed for an unrelated reason")
        if args.legacy_plugin:
            legacy = run(["--plugin", str(inputs["legacy_plugin"]), "--dsp-optimizations", str(inputs["dsp_optimizations"])], 1)
            (session / "legacy-ordering-negative.json").write_text(legacy.stderr, encoding="utf-8")
            require("missing HarmonyAfter" in json.loads(legacy.stderr)["error"], "Legacy candidate failed for an unrelated reason")
        inputs_unchanged()
        summary = {"status": "passed", "evidenceDirectory": str(session),
                   "reports": [str(path) for path in reports],
                   "reportsByteIdentical": True, "reportSha256": sha256(reports[0]),
                   "duplicateOptionRejected": True, "legacyOrderingRejected": bool(args.legacy_plugin),
                   "upstreamMitNoticeCompared": bool(args.upstream_license),
                   "readOnlyInputsUnchanged": {key: {"path": str(path), "sha256": before[key]} for key, path in inputs.items()}}
        publish_summary(summary_path, summary, session, inputs)
        inputs_unchanged()
    except Exception as error:
        publish_summary(summary_path, {"status": "failed", "error": str(error)[:1500],
                                      "evidenceDirectory": str(session)}, session, inputs)
        raise
    print(json.dumps({"status": "passed", "report": str(summary_path)}))


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(json.dumps({"status": "failed", "error": str(error)[:1500]}), file=sys.stderr)
        sys.exit(1)
