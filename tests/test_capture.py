import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("game_snapshot", Path(__file__).parents[1] / "tools/game_snapshot.py")
capture = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(capture)


class CaptureTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.game = self.root / "game"
        self.managed = self.game / "DSPGAME_Data/Managed"
        self.managed.mkdir(parents=True)
        (self.managed / "Assembly-CSharp.dll").write_bytes(b"fixture v1")
        self.state = self.root / "state"
        self.calls = 0

    def decompile(self, managed, output):
        self.calls += 1
        output.mkdir()
        (output / "Game.cs").write_text("class Game {}", encoding="utf-8")
        (output / "Game.csproj").write_text("<Project />", encoding="utf-8")

    def run_capture(self, runner=None):
        return capture.capture(self.game, self.state, version="fixture compiler", runner=runner or self.decompile)

    def test_unchanged_capture_reuses_verified_bytes(self):
        first = self.run_capture()
        pointer = (self.state / "latest.json").read_bytes()
        second = self.run_capture()
        self.assertEqual(first["id"], second["id"])
        self.assertEqual(second["status"], "reused")
        self.assertEqual(self.calls, 1)
        self.assertEqual(pointer, (self.state / "latest.json").read_bytes())

    def test_changed_input_creates_distinct_retained_snapshot(self):
        first = self.run_capture()
        (self.managed / "Assembly-CSharp.dll").write_bytes(b"fixture v2")
        second = self.run_capture()
        self.assertNotEqual(first["id"], second["id"])
        self.assertTrue(Path(first["snapshot"]).is_dir())

    def test_failure_preserves_latest_and_cleans_own_stage(self):
        first = self.run_capture()
        pointer = (self.state / "latest.json").read_bytes()
        (self.managed / "Assembly-CSharp.dll").write_bytes(b"fixture v2")
        def fail(managed, output):
            raise RuntimeError("simulated decompiler failure")
        with self.assertRaisesRegex(RuntimeError, "simulated"):
            self.run_capture(fail)
        self.assertEqual(pointer, (self.state / "latest.json").read_bytes())
        capture.verify_snapshot(Path(first["snapshot"]))
        self.assertEqual(list((self.state / "staging").iterdir()), [])

    def test_changed_installed_bytes_during_capture_are_rejected(self):
        def update_during_capture(managed, output):
            self.decompile(managed, output)
            (self.managed / "Assembly-CSharp.dll").write_bytes(b"updated by Steam")
        with self.assertRaisesRegex(RuntimeError, "changed during decompilation"):
            self.run_capture(update_during_capture)
        self.assertFalse((self.state / "latest.json").exists())

    def test_corrupt_cached_source_is_not_reused(self):
        first = self.run_capture()
        (Path(first["snapshot"]) / "source/Game.cs").write_text("corrupt", encoding="utf-8")
        with self.assertRaisesRegex(RuntimeError, "verification"):
            self.run_capture()

    def test_overlapping_capture_is_refused(self):
        with capture.capture_lock(self.state):
            with self.assertRaisesRegex(RuntimeError, "Another capture"):
                self.run_capture()

    def test_game_output_overlap_is_refused(self):
        with self.assertRaisesRegex(RuntimeError, "overlap"):
            capture.capture(self.game, self.game / "output", version="fixture", runner=self.decompile)

    def test_pointer_cannot_escape_snapshots(self):
        self.run_capture()
        (self.state / "latest.json").write_text(json.dumps({"id": "bad", "snapshot": "../outside"}), encoding="utf-8")
        with self.assertRaisesRegex(RuntimeError, "escapes"):
            capture.latest(self.state)

    def test_stale_build_properties_are_rejected(self):
        self.run_capture()
        (self.state / "GameSnapshot.props").write_text("stale", encoding="utf-8")
        with self.assertRaisesRegex(RuntimeError, "Build properties"):
            capture.latest(self.state)

    def test_linked_output_directories_are_refused(self):
        for directory in ("staging", "snapshots"):
            with self.subTest(directory=directory):
                self.state = self.root / ("linked-" + directory)
                self.state.mkdir()
                link = self.state / directory
                if os.name == "nt":
                    subprocess.run(["cmd", "/c", "mklink", "/J", str(link), str(self.game)],
                                   capture_output=True, check=True)
                else:
                    link.symlink_to(self.game, target_is_directory=True)
                try:
                    with self.assertRaisesRegex(RuntimeError, "linked|escapes"):
                        self.run_capture()
                    self.assertEqual(sorted(p.name for p in self.game.iterdir()), ["DSPGAME_Data"])
                finally:
                    if os.name == "nt":
                        link.rmdir()
                    else:
                        link.unlink()

    def test_publication_failure_preserves_previous_snapshot_and_properties(self):
        original_replace = capture.os.replace
        for filename in ("GameSnapshot.props", "latest.json"):
            with self.subTest(filename=filename):
                self.state = self.root / ("publish-" + filename)
                (self.managed / "Assembly-CSharp.dll").write_bytes(b"fixture v1")
                self.run_capture()
                pointer = (self.state / "latest.json").read_bytes()
                properties = (self.state / "GameSnapshot.props").read_bytes()
                (self.managed / "Assembly-CSharp.dll").write_bytes(b"fixture v2")
                def fail_publication(source, destination):
                    if Path(destination).name == filename:
                        raise OSError("simulated publication failure")
                    return original_replace(source, destination)
                with patch.object(capture.os, "replace", side_effect=fail_publication):
                    with self.assertRaisesRegex(OSError, "publication failure"):
                        self.run_capture()
                self.assertEqual(pointer, (self.state / "latest.json").read_bytes())
                self.assertEqual(properties, (self.state / "GameSnapshot.props").read_bytes())
                capture.latest(self.state)

    def test_compiler_inputs_must_match_verified_snapshot(self):
        result = self.run_capture()
        capture.latest(self.state, managed=Path(result["snapshot"]) / "Managed")
        with self.assertRaisesRegex(RuntimeError, "Compiler input"):
            capture.latest(self.state, managed=self.managed)


if __name__ == "__main__":
    unittest.main()
