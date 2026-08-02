import importlib.util
import re
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]
DEPLOY_SCRIPT = ROOT / "deploy.sh"
DA_DEPLOYER = ROOT.parent / "ossen-reports" / "placecontext_jobs" / "deploy_da_application.py"


def load_da_deployer():
    spec = importlib.util.spec_from_file_location("deploy_da_application", DA_DEPLOYER)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"unable to load {DA_DEPLOYER}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class DeployDaBundleTest(unittest.TestCase):
    def test_bundle_contains_every_file_required_by_da_deployer(self):
        deployer = load_da_deployer()
        script = DEPLOY_SCRIPT.read_text()
        bundle_match = re.search(r"da_bundle=\((.*?)\n\)", script, re.DOTALL)
        if bundle_match is None:
            self.fail("deploy.sh has no da_bundle array")
        bundle = set(re.findall(r"^\s+([^\s]+)$", bundle_match.group(1), re.MULTILINE))

        required = {"deploy_da_application.py", *deployer.EXISTING_JOB_FILES.values()}
        for files in deployer.JOB_FILES.values():
            required.update(files.values())

        self.assertEqual(set(), required - bundle)


if __name__ == "__main__":
    unittest.main()
