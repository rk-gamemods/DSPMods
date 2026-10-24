"""Prove the published patch reconstructs the exact reviewed tree, without a checkout."""
import json
import os
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def main() -> None:
    source = json.loads((ROOT / "sources.json").read_text(encoding="utf-8"))["SphereOpt"]
    clone = ROOT / source["path"]
    patch = ROOT / source["candidate_patch"]
    (ROOT / ".local").mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="sphereopt-patch-", dir=ROOT / ".local") as temporary:
        environment = dict(os.environ, GIT_INDEX_FILE=str(Path(temporary) / "index"))

        def git(*args: str) -> str:
            return subprocess.check_output(["git", "-C", str(clone), *args], env=environment,
                                           text=True, encoding="utf-8").strip()

        git("read-tree", source["reviewed_commit"])
        git("apply", "--cached", "--check", str(patch))
        git("apply", "--cached", str(patch))
        actual = git("write-tree")
        if actual != source["candidate_tree"]:
            raise ValueError("Patch tree mismatch: " + actual)
    print(json.dumps({"status": "PASS", "baseline": source["reviewed_commit"], "tree": actual,
                      "working_tree_and_index_changed": False}, indent=2))


if __name__ == "__main__":
    main()
