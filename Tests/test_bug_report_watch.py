import hashlib
import importlib.util
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from unittest.mock import patch
import zipfile


spec = importlib.util.spec_from_file_location("watch_reports", Path(__file__).with_name("Watch-BugReports.py"))
watch = importlib.util.module_from_spec(spec)
spec.loader.exec_module(watch)


class WatchTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="RaidWatchFixture-")
        self.root = Path(self.temp.name)
        self.inbox = self.root / "inbox"
        self.state = self.root / "queue"
        self.inbox.mkdir()
        self.normal = self.root / "normal-save.json"
        self.normal.write_text("ordinary save remains intact", encoding="utf-8")
        self.queue = watch.load(self.state)

    def tearDown(self):
        self.assertEqual(self.normal.read_text(encoding="utf-8"), "ordinary save remains intact")
        self.temp.cleanup()

    def bundle(self, suffix="0123456789", corrupt=False, extra=None):
        path = self.inbox / ("BugReport_20261025_120000_" + suffix + ".zip")
        if corrupt:
            path.write_bytes(b"not a zip")
        else:
            payload = ("Build: test-build\nerror happened " + suffix + "\n").encode("utf-8")
            row = "report.txt\tincluded\t%d\t%s\tredacted copy\n" % (len(payload), hashlib.sha256(payload).hexdigest().upper())
            with zipfile.ZipFile(path, "w") as zf:
                zf.writestr("report.txt", payload)
                zf.writestr("manifest.tsv", "entry\tstatus\tbytes\tSHA256\tnote\n" + row)
                zf.writestr("collection-status.txt", "complete")
                if extra:
                    zf.writestr(extra, "evil")
        os.utime(path, (1000000000, 1000000000))
        return path

    def test_new_duplicate_restart_order_and_normal_save(self):
        a = self.bundle()
        self.assertEqual(watch.scan(self.inbox, self.state, self.queue), 1)
        self.assertEqual(len(self.queue["order"]), 1)
        self.assertEqual(self.queue["reports"][self.queue["order"][0]]["build"], "test-build")
        same = self.inbox / "BugReport_20261025_130000_ffffffffff.zip"
        same.write_bytes(a.read_bytes())
        os.utime(same, (1000000000, 1000000000))
        self.assertEqual(watch.scan(self.inbox, self.state, self.queue), 0)
        self.assertEqual(len(watch.load(self.state)["order"]), 1)
        self.bundle("aaaaaaaaaa")
        restarted = watch.load(self.state)
        self.assertEqual(watch.scan(self.inbox, self.state, restarted), 1)
        self.assertEqual(len(restarted["order"]), 2)
        self.assertTrue((self.state / "reports" / restarted["order"][0] / "evidence" / "report.txt").exists())

    def test_partial_and_unsafe_zip(self):
        partial = self.inbox / "BugReport_20261025_140000_2222222222.zip.partial"
        partial.write_bytes(b"still writing")
        self.assertEqual(watch.scan(self.inbox, self.state, self.queue), 0)
        self.bundle("3333333333", corrupt=True)
        self.bundle("4444444444", extra="../escape.txt")
        good = self.bundle("5555555555")
        self.assertEqual(watch.scan(self.inbox, self.state, self.queue), 3)
        states = [self.queue["reports"][key]["state"] for key in self.queue["order"]]
        self.assertEqual(states.count("처리 실패"), 2)
        self.assertEqual(states.count("대기"), 1)
        self.assertFalse((self.root / "escape.txt").exists())
        self.assertEqual(watch.digest(good), self.queue["order"][-1])

    def test_manifest_mismatch(self):
        source = self.bundle()
        with zipfile.ZipFile(source, "a") as zf:
            zf.writestr("logs/current-session.log", "unlisted")
        os.utime(source, (1000000000, 1000000000))
        watch.scan(self.inbox, self.state, self.queue)
        item = self.queue["reports"][self.queue["order"][0]]
        self.assertEqual(item["state"], "처리 실패")
        self.assertIn("unlisted", item["reason"])

    def test_foreground_watch_stop_and_restart(self):
        script = Path(__file__).with_name("Watch-BugReports.py")
        process = subprocess.Popen([sys.executable, str(script), "watch", "--inbox", str(self.inbox),
                                    "--state-dir", str(self.state)], stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        try:
            self.bundle("6666666666")
            deadline = time.time() + 8
            while time.time() < deadline and not (self.state / "queue.json").exists():
                time.sleep(.1)
            self.assertTrue((self.state / "queue.json").exists(), "watcher detected ZIP")
            subprocess.run([sys.executable, str(script), "stop", "--inbox", str(self.inbox),
                            "--state-dir", str(self.state)], check=True, capture_output=True)
            out, err = process.communicate(timeout=8)
            self.assertEqual(process.returncode, 0, err.decode(errors="replace"))
            self.assertEqual(len(watch.load(self.state)["order"]), 1)
        finally:
            if process.poll() is None:
                process.terminate()
                process.wait(timeout=5)

    def test_agent_queue_runs_one_item_and_records_changes(self):
        self.bundle("7777777777")
        self.bundle("8888888888")
        self.assertEqual(watch.scan(self.inbox, self.state, self.queue), 2)
        workspace = self.root / "workspace"
        (workspace / "Assets" / "Scripts").mkdir(parents=True)
        (workspace / "AGENTS.md").write_text("rules", encoding="utf-8")
        code = workspace / "Assets" / "Scripts" / "Example.cs"
        code.write_text("before", encoding="utf-8")

        def fake_run(command, **options):
            class Done:
                returncode = 0
                stdout = "source-revision\n"
            if command[0] == "codex":
                code.write_text("after", encoding="utf-8")
                Path(command[command.index("-o") + 1]).write_text("checked", encoding="utf-8")
            return Done()

        with patch.object(watch.subprocess, "run", side_effect=fake_run):
            self.assertTrue(watch.run_agent(self.state, self.queue, workspace, 30))
        first = self.queue["reports"][self.queue["order"][0]]
        second = self.queue["reports"][self.queue["order"][1]]
        self.assertEqual(first["state"], "실행 환경 필요")
        self.assertEqual(first["changes"], ["Assets\\Scripts\\Example.cs"] if os.name == "nt" else ["Assets/Scripts/Example.cs"])
        self.assertEqual(second["state"], "대기")


if __name__ == "__main__":
    unittest.main()
