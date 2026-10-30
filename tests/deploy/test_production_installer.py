import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


class ProductionInstallerTests(unittest.TestCase):
    def run_installer(self, managed=True, shared=True):
        with tempfile.TemporaryDirectory() as directory:
            stub = Path(directory) / "kubectl"
            stub.write_text('''#!/usr/bin/env python3
import os, sys
args = sys.argv[1:]
if args[:2] == ['get', 'nodes']:
    if 'node-role.kubernetes.io/control-plane' in args:
        pass
    elif 'placecontext-pool=shared' in args and os.environ['TEST_SHARED'] == '0':
        pass
    else:
        print('worker Ready worker 1d v1.34.0')
elif 'go-template=' in ' '.join(args):
    template = args[-1]
    print('https://objects.example.test' if 'endpoint' in template else 'true' if 'force-path-style' in template else 'test-value')
elif args[:2] == ['auth', 'can-i']:
    print('yes')
elif args[:2] == ['apply', '-f'] and args[-1] == '-':
    sys.stdin.read()
''')
            stub.chmod(0o755)
            command = ['bash', str(ROOT / 'deploy/release/install-production.sh'),
                       '--image', 'registry.example.test/platform@sha256:' + 'a' * 64,
                       '--hostname', 'platform.example.test', '--tls-secret', 'test-tls', '--no-wait']
            if managed:
                command.append('--managed-control-plane')
            return subprocess.run(command, env={**os.environ, 'PATH': directory + ':' + os.environ['PATH'],
                                                 'TEST_SHARED': '1' if shared else '0'},
                                  text=True, capture_output=True, timeout=10)

    def test_managed_cluster_does_not_need_visible_control_plane_nodes(self):
        result = self.run_installer()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn('https://platform.example.test', result.stdout)

    def test_self_hosted_cluster_still_requires_control_plane_redundancy(self):
        result = self.run_installer(managed=False)
        self.assertNotEqual(0, result.returncode)
        self.assertIn('at least 3 control-plane nodes', result.stderr)

    def test_missing_shared_pool_is_rejected(self):
        result = self.run_installer(shared=False)
        self.assertNotEqual(0, result.returncode)
        self.assertIn('placecontext-pool=shared', result.stderr)


if __name__ == '__main__':
    unittest.main()
