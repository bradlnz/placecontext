import hashlib
from pathlib import Path
import subprocess
import tarfile
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]


class ReleasePackageTests(unittest.TestCase):
    def test_bundle_keeps_runtime_image_and_excludes_removed_ai_worker(self):
        image = "ghcr.io/bradlnz/placecontext@sha256:" + "a" * 64
        with tempfile.TemporaryDirectory() as output:
            subprocess.run(
                ["bash", str(ROOT / "deploy/release/package.sh"), "--version", "test",
                 "--image", image, "--output", output],
                check=True, capture_output=True, text=True,
            )
            release = Path(output) / "releases/test"
            archive = release / "placecontext-deploy.tar.gz"
            checksum = (release / "SHA256SUMS").read_text().split()[0]
            self.assertEqual(hashlib.sha256(archive.read_bytes()).hexdigest(), checksum)
            with tarfile.open(archive) as bundle:
                names = bundle.getnames()
                self.assertFalse(any("local-ai" in name for name in names))
                runtime = bundle.extractfile("placecontext-deploy/RUNTIME_IMAGE")
                self.assertEqual(runtime.read().decode().strip(), image)
                installer = bundle.extractfile("placecontext-deploy/install.sh").read().decode()
                self.assertIn('$ROOT/RUNTIME_IMAGE', installer)
                self.assertNotIn("start_worker", installer)
                subprocess.run(["bash", "-n"], input=installer, text=True, check=True)

    def test_removed_ai_install_options_fail_before_any_provisioning(self):
        installer = ROOT / "deploy/release/install.sh"
        for option in ("--ai", "--ai-shard"):
            with self.subTest(option=option):
                result = subprocess.run(["bash", str(installer), option], capture_output=True, text=True)
                self.assertEqual(result.returncode, 2)
                self.assertIn("Unknown option", result.stderr)
        result = subprocess.run(["bash", str(installer), "--no-ai", "--help"], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0)


if __name__ == "__main__":
    unittest.main()
