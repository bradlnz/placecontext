import json
import os
import shutil
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


class RunTests(unittest.TestCase):
    def run_script(self, state="true", ready_after=1, docker_available=True, health="", object_store=False):
        with tempfile.TemporaryDirectory() as directory:
            script = Path(directory) / "run.sh"
            shutil.copy2(ROOT / "run.sh", script)
            if object_store:
                secrets = Path(directory) / "dist/demo-source"
                secrets.mkdir(parents=True)
                (secrets / "object-store.env").write_text("AWS_ACCESS_KEY_ID=test-access\nAWS_SECRET_ACCESS_KEY=test-secret\n")
            stub = Path(directory) / "stub"
            stub.write_text('''#!/usr/bin/env python3
import json, os, pathlib, sys
name = pathlib.Path(sys.argv[0]).name
args = sys.argv[1:]
log = pathlib.Path(os.environ["TEST_LOG"])
with log.open("a") as output:
    output.write(json.dumps([name, args, os.getcwd()]) + "\\n")
if name == "curl":
    if os.environ["TEST_HEALTH"]:
        print(os.environ["TEST_HEALTH"])
    else:
        sys.exit(7)
if name == "dotnet" and args[0] == "run":
    if os.environ["TEST_OBJECT_STORE"] == "1":
        assert os.environ["PlaceContext__ObjectStore__Endpoint"] == "http://localhost:8333"
        assert os.environ["PlaceContext__ObjectStore__AccessKey"] == "test-access"
        assert os.environ["PlaceContext__ObjectStore__SecretKey"] == "test-secret"
    sys.exit(23)
if name == "docker":
    if args[0] == "info":
        sys.exit(0 if os.environ["TEST_DOCKER"] == "1" else 1)
    if args[:2] == ["container", "inspect"]:
        state = os.environ["TEST_STATE"]
        if state == "missing":
            sys.exit(1)
        print(state)
    if args[0] == "exec":
        calls = [json.loads(line) for line in log.read_text().splitlines()]
        checks = sum(name == "docker" and args[0] == "exec" for name, args, _ in calls)
        sys.exit(0 if checks >= int(os.environ["TEST_READY_AFTER"]) else 1)
''')
            stub.chmod(0o755)
            for name in ("curl", "docker", "dotnet", "sleep"):
                (Path(directory) / name).symlink_to(stub)
            log = Path(directory) / "calls.jsonl"
            result = subprocess.run(
                [str(script), "--no-restore", "--", "--example=value with spaces"],
                cwd=directory, text=True, capture_output=True, timeout=10,
                env={**os.environ, "PATH": directory + os.pathsep + os.environ["PATH"],
                     "TEST_LOG": str(log), "TEST_STATE": state,
                     "TEST_HEALTH": health,
                     "TEST_OBJECT_STORE": "1" if object_store else "0",
                     "TEST_READY_AFTER": str(ready_after),
                     "TEST_DOCKER": "1" if docker_available else "0"},
            )
            calls = [json.loads(line) for line in log.read_text().splitlines()]
            return result, calls

    def test_running_app_exits_successfully_without_starting_dependencies(self):
        result, calls = self.run_script(health='"ok"', docker_available=False)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("The application is already running.\n", result.stdout)
        self.assertEqual(["curl"], [name for name, _, _ in calls])
        self.assertIn("http://localhost:7700/healthz", calls[0][1])

    def test_unrelated_health_response_does_not_skip_startup(self):
        result, _ = self.run_script(health="unrelated service")
        self.assertEqual(23, result.returncode, result.stderr)

    def test_running_database_is_reused_and_arguments_are_forwarded(self):
        result, calls = self.run_script()
        self.assertEqual(23, result.returncode, result.stderr)
        self.assertFalse(any(name == "docker" and args[0] in ("start", "run") for name, args, _ in calls))
        self.assertEqual(["dotnet", ["run", "--project", "src/PlaceContext.Host",
                                    "--launch-profile", "http", "--no-restore", "--",
                                    "--example=value with spaces"], calls[0][2]], calls[-1])

    def test_local_artifact_store_starts_and_credentials_are_loaded(self):
        result, calls = self.run_script(object_store=True)
        self.assertEqual(23, result.returncode, result.stderr)
        self.assertIn(["start", "placecontext-artifacts"], [args for name, args, _ in calls if name == "docker"])
        self.assertNotIn("test-secret", result.stdout + result.stderr)

    def test_stopped_database_is_started_and_readiness_is_retried(self):
        result, calls = self.run_script(state="false", ready_after=3)
        self.assertEqual(23, result.returncode, result.stderr)
        docker = [args for name, args, _ in calls if name == "docker"]
        self.assertIn(["start", "devcontext-validation-postgres"], docker)
        self.assertEqual(3, sum(args[0] == "exec" for args in docker))

    def test_missing_database_is_created_with_persistent_local_storage(self):
        result, calls = self.run_script(state="missing")
        self.assertEqual(23, result.returncode, result.stderr)
        create = next(args for name, args, _ in calls if name == "docker" and args[0] == "run")
        self.assertIn("127.0.0.1:5433:5432", create)
        self.assertIn("placecontext-dev-postgres:/var/lib/postgresql/data", create)
        self.assertEqual("postgres:17-alpine", create[-1])

    def test_unavailable_dependencies_prevent_app_start(self):
        for options in ({"docker_available": False}, {"ready_after": 31}):
            with self.subTest(options=options):
                result, calls = self.run_script(**options)
                self.assertEqual(1, result.returncode, result.stderr)
                self.assertFalse(any(name == "dotnet" and args[0] == "run" for name, args, _ in calls))
                self.assertTrue(result.stderr)


if __name__ == "__main__":
    unittest.main()
