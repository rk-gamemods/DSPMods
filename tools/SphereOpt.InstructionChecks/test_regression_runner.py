"""Filesystem/process-boundary regressions; never execute a plugin or game DLL."""
import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

TOOL = Path(__file__).resolve().parent
ROOT = TOOL.parents[1]
SOURCE = Path(os.environ.get("SPHEREOPT_RUNNER_UNDER_TEST", TOOL / "run-regressions.py"))
spec = importlib.util.spec_from_file_location("regression_runner", SOURCE)
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)
# A historical copy must still locate the real, read-only test source inputs.
runner.__file__ = str(TOOL / "run-regressions.py")


def proof():
    return {"status": "passed", "patchOrdering": {"status": "passed"},
            "stackInteroperation": {"installedSourceEquivalence": {"status": "passed"},
                                    "drawModels": {"permutationsPassed": 6}}}


class RegressionRunnerTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="runner-gates-", dir=ROOT / ".local")
        self.addCleanup(self.temporary.cleanup)
        self.base = Path(self.temporary.name).resolve()
        self.inputs = {name: self.base / (name + ".dll") for name in
                       ("checker", "assembly", "plugin", "dsp-optimizations")}
        for name, path in self.inputs.items():
            path.write_bytes(("fixture: " + name).encode())
        (self.base / "THIRD_PARTY_NOTICES.md").write_bytes((TOOL / "THIRD_PARTY_NOTICES.md").read_bytes())
        self.output = self.base / "evidence"
        self.output.mkdir()
        self.summary = self.output / "regression-result.json"
        self.calls = []

    def invoke(self, emit=True, only_first=False):
        arguments = [str(TOOL / "run-regressions.py")]
        for key, path in self.inputs.items():
            arguments += ["--" + key, str(path)]
        arguments += ["--report-dir", str(self.output)]
        self.positives = 0

        def checker(command, **kwargs):
            self.calls.append(command)
            if command.count("--assembly") == 2:
                return SimpleNamespace(returncode=1, stdout="", stderr=json.dumps({"error": "Usage: duplicate option"}))
            self.positives += 1
            if emit and (not only_first or self.positives == 1):
                Path(command[command.index("--report") + 1]).write_text(json.dumps(proof()), encoding="utf-8")
            return SimpleNamespace(returncode=0, stdout="", stderr="")

        with patch.object(sys, "argv", arguments), patch.object(runner.subprocess, "run", checker), contextlib.redirect_stdout(io.StringIO()):
            runner.main()

    def seed_old_reports(self):
        for name in ("candidate-proof.json", "candidate-proof.repeat.json"):
            (self.output / name).write_text(json.dumps(proof()), encoding="utf-8")

    def test_fresh_reports_pass_and_preserve_inputs(self):
        before = {key: path.read_bytes() for key, path in self.inputs.items()}
        self.invoke()
        result = json.loads(self.summary.read_text())
        self.assertEqual("passed", result["status"])
        self.assertEqual(before, {key: path.read_bytes() for key, path in self.inputs.items()})
        for path in result["reports"]:
            self.assertEqual(Path(result["evidenceDirectory"]), Path(path).parent)

    def test_stale_reports_cannot_satisfy_missing_emission(self):
        self.seed_old_reports()
        with self.assertRaisesRegex(RuntimeError, "fresh bounded report"):
            self.invoke(emit=False)
        self.assertEqual("failed", json.loads(self.summary.read_text())["status"])

    def test_second_invocation_must_emit_its_own_report(self):
        self.seed_old_reports()
        with self.assertRaisesRegex(RuntimeError, "fresh bounded report"):
            self.invoke(only_first=True)

    def test_summary_input_collision_is_rejected_before_writes(self):
        self.summary.write_bytes(b"declared plugin input")
        self.inputs["plugin"] = self.summary
        before = self.summary.read_bytes()
        with self.assertRaisesRegex(RuntimeError, "collides with input plugin"):
            self.invoke()
        self.assertEqual(before, self.summary.read_bytes())
        self.assertEqual([], self.calls)

    def test_hardlink_summary_alias_is_rejected(self):
        os.link(self.inputs["plugin"], self.summary)
        before = self.inputs["plugin"].read_bytes()
        with self.assertRaisesRegex(RuntimeError, "collides with input plugin"):
            self.invoke()
        self.assertEqual(before, self.inputs["plugin"].read_bytes())
        self.assertEqual([], self.calls)

    def test_new_runs_have_distinct_owned_evidence_directories(self):
        self.invoke()
        first = json.loads(self.summary.read_text())
        self.invoke()
        second = json.loads(self.summary.read_text())
        self.assertNotEqual(first["evidenceDirectory"], second["evidenceDirectory"])
        self.assertEqual(first["reportSha256"], second["reportSha256"])
        self.assertTrue(all(Path(path).is_file() for path in first["reports"] + second["reports"]))

    def test_input_mutation_after_summary_publication_is_detected(self):
        publish = runner.publish_summary

        def mutate_after_publication(path, data, session, inputs):
            publish(path, data, session, inputs)
            if data["status"] == "passed":
                self.inputs["plugin"].write_bytes(b"changed after summary write")

        with patch.object(runner, "publish_summary", mutate_after_publication):
            with self.assertRaisesRegex(RuntimeError, "read-only regression input changed"):
                self.invoke()
        self.assertEqual("failed", json.loads(self.summary.read_text())["status"])


if __name__ == "__main__":
    unittest.main()
