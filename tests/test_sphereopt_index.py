import importlib.util
import os
from pathlib import Path
import shutil
import stat
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch


TOOLS = Path(__file__).resolve().parents[1] / "tools"
with patch.object(sys, "path", [str(TOOLS), *sys.path]):
    spec = importlib.util.spec_from_file_location("sphereopt_index", TOOLS / "prepare_sphereopt_index.py")
    indexing = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(indexing)


class SphereOptIndexTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.addCleanup(self.cleanup)
        self.clone = self.root / "clone"
        self.clone.mkdir()
        self.state = self.root / "state"
        self.state.mkdir()
        files = {"Mod.cs": b"class Mod {}", "shaders/Test.shader": b'Shader "Test" {}',
                 "shaders/Test.compute": b"void CSMain() {}", "shaders/Common.cginc": b"float common;",
                 "ProjectSettings.asset": "name: sample".encode("utf-16"),
                 "README.md": b"# Fixture\n\n```\nexample\n```\n", ".gitignore": b"LocalIgnored.txt\n",
                 "bundle": b"UnityFS\0binary\0"}
        for name, content in files.items():
            path = self.clone / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(content)
        self.run_git("init", "--initial-branch=main")
        self.run_git("add", ".")
        self.run_git("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "commit", "-m", "Fixture")

    def run_git(self, *args):
        subprocess.run(["git", "-C", str(self.clone), *args], check=True,
                       stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)

    def cleanup(self):
        def clear_fixture_readonly(function, path, error):
            if not isinstance(error, PermissionError) or not os.stat(path).st_file_attributes & stat.FILE_ATTRIBUTE_READONLY:
                raise error
            os.chmod(path, stat.S_IWRITE)
            function(path)
        if self.root.exists():
            assert self.root.resolve().parent == Path(tempfile.gettempdir()).resolve()
            shutil.rmtree(self.root, onexc=clear_fixture_readonly)
        self.temporary.cleanup()

    def test_complete_source_and_text_projection_repeats_without_changes(self):
        first = indexing.prepare(self.clone, self.state)
        second = indexing.prepare(self.clone, self.state)
        self.assertEqual(first, second)
        self.assertEqual((first["tracked_files"], first["text_files"], first["binary_files"], first["implementation_files"]), (8, 7, 1, 4))
        self.assertEqual(indexing.git(self.clone, "status", "--porcelain"), "")
        readme = next(r for r in first["files"] if r["path"] == "README.md")
        projected = (Path(first["text_index_root"]) / readme["document"]).read_bytes()
        self.assertIn(b"````\n# Fixture\n\n```\nexample\n```\n\n````", projected)
        self.assertEqual(next(r["kind"] for r in first["files"] if r["path"] == "ProjectSettings.asset"), "text")

    def test_ignored_local_file_prevents_complete_coverage_claim(self):
        (self.clone / "LocalIgnored.txt").write_text("Unaccounted input", encoding="utf-8")
        self.assertEqual(indexing.git(self.clone, "status", "--porcelain"), "")
        with self.assertRaisesRegex(RuntimeError, "ignored/untracked"):
            indexing.prepare(self.clone, self.state)

    def test_changed_projection_is_preserved_and_rejected(self):
        first = indexing.prepare(self.clone, self.state)
        target = Path(first["text_index_root"]) / first["files"][0]["document"]
        target.write_bytes(b"Changed copy")
        with self.assertRaisesRegex(RuntimeError, "projection differs"):
            indexing.prepare(self.clone, self.state)
        self.assertEqual(target.read_bytes(), b"Changed copy")

    def test_verifier_rejects_missing_shader_and_unverified_text(self):
        manifest = indexing.prepare(self.clone, self.state)
        hashes = {r["document"]: r["document_sha256"] for r in manifest["files"] if r["kind"] == "text"}
        hashes["binary-artifacts.md"] = manifest["binary_catalog_sha256"]
        observed = {"code_repo": "andyoneal/SphereOpt", "text_repo": "local/" + manifest["text_index_name"],
                    "code_files": [r["path"] for r in manifest["files"] if r["implementation_source"]],
                    "text_docs": list(hashes), "body_readback": {
                        name: {"content_hash": digest, "hash_verified": True, "source_hash_verified": True}
                        for name, digest in hashes.items()}}
        self.assertEqual(indexing.verify_inventory(manifest, observed)["implementation_files_covered"], 4)
        observed["code_files"].remove("shaders/Test.compute")
        with self.assertRaisesRegex(RuntimeError, "Implementation source is missing"):
            indexing.verify_inventory(manifest, observed)
        observed["code_files"].append("shaders/Test.compute")
        observed["body_readback"][next(iter(hashes))]["source_hash_verified"] = False
        with self.assertRaisesRegex(RuntimeError, "content was not verified"):
            indexing.verify_inventory(manifest, observed)


if __name__ == "__main__":
    unittest.main()
