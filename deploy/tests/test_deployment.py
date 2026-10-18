"""Offline Bash deployment tests; fake executables prevent all cloud mutations.

Run: python -m unittest discover -s deploy/tests -p "test_*.py"
"""
from __future__ import annotations

import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest


DEPLOY = Path(__file__).resolve().parents[1]
WINDOWS_BASH = Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "Git/bin/bash.exe"
BASH = str(WINDOWS_BASH) if WINDOWS_BASH.is_file() else shutil.which("bash")
CONNECTION = 'Host=db.example;Password=space,comma; $literal "quote"\\slash'
SECRET = 'fake,worker;$literal "quote"\\slash'
FIXTURE = f"""# Export syntax, CRLF, BOM, quotes, commas and literal dollars.
export GRID_ENV=Production
GoogleCloud__ProjectId=test-project
GOOGLE_CLOUD_PROJECT=test-project
GoogleCloud__Bucket=test-bucket
GCS_BUCKET=test-bucket
GoogleCloud__WorkerSecret='{SECRET}'
GRID_WORKER_SECRET='{SECRET}'
ConnectionStrings__Default='{CONNECTION}'
AuthServer__CertificatePassPhrase=fake-certificate
GEMINI_API_KEY=fake-provider
GEMINI_MODEL=fake-model
GCP_REGION=asia-southeast1
FIREBASE_PROJECT_ID=test-web
App__CorsOrigins=https://one.example,https://two.example
PUBSUB_PARSER_SUBSCRIPTION=custom-subscription
AiOptimization__Enabled=true
"""
MOCK = r'''
import json, os, pathlib, sys
tool, *args = sys.argv[1:]
event = {"tool": tool, "args": args, "cwd": str(pathlib.Path.cwd())}
for argument in args:
    if argument.startswith("--env-vars-file="):
        name = argument.split("=", 1)[1]
        event["file"] = name
        event["environment"] = json.loads(pathlib.Path(name).read_text(encoding="utf-8-sig"))
with open(os.environ["GRID_TEST_LOG"], "a", encoding="utf-8") as log:
    log.write(json.dumps(event) + "\n")
command = " ".join([tool, *args])
failure = os.environ.get("GRID_TEST_FAIL", "")
if failure and command.startswith(failure):
    raise SystemExit(23)
if command.startswith("gcloud run services describe"):
    print("https://discovered-api.example.test")
elif os.environ.get("GRID_TEST_MISSING") == "1" and ("describe" in args or command.startswith("gsutil ls")):
    raise SystemExit(1)
'''


@unittest.skipUnless(BASH, "Bash is not installed")
class BashDeploymentTests(unittest.TestCase):
    def setUp(self):
        self.sandbox = tempfile.TemporaryDirectory(prefix="grid deployment tests ")
        self.addCleanup(self.sandbox.cleanup)
        self.root = Path(self.sandbox.name)
        (self.root / "deploy").mkdir()
        for name in ("common.sh", "deploy-cloudrun.sh", "setup-gcp.sh"):
            shutil.copyfile(DEPLOY / name, self.root / "deploy" / name)
        self.bin = self.root / "bin"
        self.bin.mkdir()
        self.mock = self.root / "mock.py"
        self.mock.write_text(MOCK, encoding="utf-8")
        self.log = self.root / "commands.jsonl"
        for tool in ("gcloud", "docker", "gsutil"):
            script = self.bin / tool
            script.write_text(
                f'#!/usr/bin/env bash\nexec "$GRID_TEST_PYTHON" "$GRID_TEST_MOCK" {tool} "$@"\n',
                encoding="utf-8", newline="\n",
            )
            script.chmod(0o755)
        self.write_env(FIXTURE)

    def write_env(self, text):
        (self.root / ".env").write_bytes(b"\xef\xbb\xbf" + text.replace("\n", "\r\n").encode())
        if self.log.exists():
            self.log.unlink()

    def run_script(self, name, *, fail="", missing=False, args=()):
        env = dict(os.environ)
        env.update({
            "PATH": str(self.bin) + os.pathsep + env.get("PATH", ""),
            "GRID_TEST_PYTHON": Path(sys.executable).as_posix(),
            "GRID_TEST_MOCK": self.mock.as_posix(),
            "GRID_TEST_LOG": str(self.log),
            "GRID_TEST_FAIL": fail,
            "GRID_TEST_MISSING": "1" if missing else "0",
            "GOOGLE_CLOUD_PROJECT": "wrong-ambient-project",
        })
        return subprocess.run(
            [BASH, str(self.root / "deploy" / name), *args], cwd=self.root.parent,
            env=env, capture_output=True, text=True, encoding="utf-8", timeout=60,
        )

    def events(self):
        if not self.log.exists():
            return []
        return [json.loads(line) for line in self.log.read_text().splitlines()]

    def test_syntax(self):
        for name in ("common.sh", "deploy-cloudrun.sh", "setup-gcp.sh"):
            result = subprocess.run([BASH, "-n", str(DEPLOY / name)], capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)

    @unittest.skipUnless(os.name == "nt", "Windows launcher test")
    def test_windows_launchers_reject_incomplete_config_before_tools(self):
        for source in [*DEPLOY.glob("*.ps1"), *DEPLOY.glob("*.cmd")]:
            shutil.copyfile(source, self.root / "deploy" / source.name)
        # Cloud scripts stop on development mode; migrations stop on missing DB.
        # No real external deployment or migration tool can be reached.
        self.write_env("GRID_ENV=Development\n")
        for name in ("setup-gcp", "deploy-cloudrun", "deploy-firebase", "run-migrations"):
            result = subprocess.run(
                [os.environ.get("COMSPEC", "cmd.exe"), "/d", "/c", str(self.root / "deploy" / f"{name}.cmd")],
                cwd=self.root.parent, capture_output=True, text=True, timeout=30,
            )
            self.assertNotEqual(result.returncode, 0, f"{name} accepted incomplete .env")
            self.assertRegex(result.stdout + result.stderr, "GRID_ENV=Production|ConnectionStrings__Default")
            self.assertEqual(self.events(), [])

    def test_cloudrun_preserves_values_and_uses_explicit_project(self):
        result = self.run_script("deploy-cloudrun.sh")
        self.assertEqual(result.returncode, 0, result.stderr)
        environment_events = [event for event in self.events() if "environment" in event]
        self.assertEqual(len(environment_events), 3)
        initial, updated, worker = [event["environment"] for event in environment_events]
        self.assertEqual(initial["ConnectionStrings__Default"], CONNECTION)
        self.assertEqual(initial["GoogleCloud__WorkerSecret"], SECRET)
        self.assertEqual(initial["App__CorsOrigins"], "https://one.example,https://two.example")
        self.assertEqual(initial["GEMINI_MODEL"], "fake-model")
        self.assertEqual(initial["AiOptimization__Enabled"], "true")
        self.assertEqual(updated["App__SelfUrl"], "https://discovered-api.example.test")
        self.assertEqual(worker["BACKEND_API_URL"], updated["App__SelfUrl"])
        self.assertEqual(worker["PUBSUB_PARSER_SUBSCRIPTION"], "custom-subscription")
        for event in environment_events:
            self.assertIn("--project=test-project", event["args"])
            self.assertEqual(Path(event["cwd"]), self.root)
            self.assertFalse(Path(event["file"]).exists())
        for value in (CONNECTION, SECRET, "fake-provider"):
            self.assertNotIn(value, result.stdout + result.stderr)

    def test_configured_url_and_resource_overrides(self):
        self.write_env(FIXTURE + "App__SelfUrl=https://configured.example.test\nVITE_API_URL=https://configured.example.test\nCLOUD_RUN_API_SERVICE=custom-api\n")
        result = self.run_script("deploy-cloudrun.sh")
        self.assertEqual(result.returncode, 0, result.stderr)
        events = self.events()
        self.assertFalse(any(event["args"][:3] == ["run", "services", "describe"] for event in events))
        self.assertTrue(any(event["args"][:3] == ["run", "deploy", "custom-api"] for event in events))

    def test_invalid_configuration_never_calls_a_tool(self):
        cases = [
            FIXTURE.replace("GRID_ENV=Production", "GRID_ENV=Development"),
            FIXTURE.replace("GOOGLE_CLOUD_PROJECT=test-project", "GOOGLE_CLOUD_PROJECT=other"),
            FIXTURE.replace("AuthServer__CertificatePassPhrase=fake-certificate", "AuthServer__CertificatePassPhrase="),
            FIXTURE + "PUBSUB_EMULATOR_HOST=localhost:8085\n",
            FIXTURE + "GoogleCloud__Bucket=other\n",
            FIXTURE + "BAD='unclosed\n",
            FIXTURE + "this is not an entry\n",
        ]
        for index, fixture in enumerate(cases):
            with self.subTest(case=index):
                self.write_env(fixture)
                result = self.run_script("deploy-cloudrun.sh")
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual(self.events(), [])
        self.write_env(FIXTURE)
        result = self.run_script("deploy-cloudrun.sh", args=("old-project-argument",))
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.events(), [])

    def test_api_failure_stops_worker_and_cleans_environment_file(self):
        result = self.run_script("deploy-cloudrun.sh", fail="gcloud run deploy")
        self.assertEqual(result.returncode, 23)
        events = self.events()
        self.assertFalse(any("grid-worker" in " ".join(event["args"]) for event in events))
        for event in events:
            if "file" in event:
                self.assertFalse(Path(event["file"]).exists())

    def test_build_push_failure_stops_deployment(self):
        result = self.run_script("deploy-cloudrun.sh", fail="docker push")
        self.assertEqual(result.returncode, 23)
        self.assertFalse(any(event["args"][:2] == ["run", "deploy"] for event in self.events()))

    def test_setup_handles_existing_missing_and_failed_resources(self):
        result = self.run_script("setup-gcp.sh")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertFalse(any("create" in event["args"] or event["args"][:1] == ["mb"] for event in self.events()))
        self.write_env(FIXTURE)
        result = self.run_script("setup-gcp.sh", missing=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        creates = [event for event in self.events() if "create" in event["args"] or event["args"][:1] == ["mb"]]
        self.assertEqual(len(creates), 7)
        self.write_env(FIXTURE)
        result = self.run_script("setup-gcp.sh", missing=True, fail="gcloud artifacts repositories create")
        self.assertEqual(result.returncode, 23)
        self.assertFalse(any(event["tool"] == "gsutil" for event in self.events()))

    def test_dotenv_is_never_executed_and_missing_file_fails(self):
        marker = self.root / "dotenv-must-not-execute"
        self.write_env(FIXTURE + f'LITERAL=$(touch "{marker.as_posix()}")\n')
        result = self.run_script("deploy-cloudrun.sh")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertFalse(marker.exists())
        self.write_env(FIXTURE)
        (self.root / ".env").unlink()
        result = self.run_script("deploy-cloudrun.sh")
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.events(), [])


if __name__ == "__main__":
    unittest.main()
