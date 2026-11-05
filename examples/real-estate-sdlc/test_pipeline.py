import base64
import io
import json
import unittest
import zipfile
from build import build
from pipeline import prepare, package, score, WEIGHTS


class PipelineTests(unittest.TestCase):
    def packaged(self):
        p = prepare({})
        p['implementation'] = json.dumps({'files': [{'path': 'README.md', 'content': 'source'}], 'preview_url': 'http://preview.test'})
        return package(p)

    def passing(self):
        p = self.packaged()
        evidence = {'passed': True, 'evidence': ['tests/report.json: inspected passing assertions']}
        identity = {k: p[k] for k in ('verification_id', 'source_sha256')}
        p['review'] = json.dumps(dict(identity, categories={k: evidence for k in WEIGHTS}, blockers=[]))
        p['qemu_ui'] = {'exitCode': 0, 'outputTruncated': False, 'stdout': json.dumps(dict(identity, **evidence))}
        p["requirements_gate"] = {"passed": True}
        p["implementation_gate"] = {"passed": True}
        return p

    def test_automatic_agent_configuration(self):
        from build import build
        connection = 'c0fcd241-94f3-eee0-6241-1d1ff1c3912a'
        manifest = build(connection_id=connection, model='test-model')
        agents = [s['Action'] for s in manifest['JobChains'][0]['Stages']
                  if s.get('Action', {}).get('Type') in ('agentTask', 'agentInput')]
        self.assertEqual(4, len(agents))
        self.assertTrue(all(a['AgentProvider'] == 'automatic' and a['ConnectionId'] == connection and a['Model'] == 'test-model' for a in agents))
        with self.assertRaises(ValueError):
            build(connection_id=connection)

    def test_missing_evidence_fails_closed(self):
        result = score(prepare({}))['readiness']
        self.assertEqual(0, result['score'])
        self.assertFalse(result['production_ready'])

    def test_current_evidence_passes_and_stale_or_truncated_browser_fails(self):
        p = self.passing()
        self.assertTrue(score(p)['readiness']['production_ready'])
        self.assertEqual(100, score(p)['readiness']['score'])
        p['verification_id'] = 'new attempt'
        self.assertFalse(score(p)['readiness']['production_ready'])
        p = self.passing()
        p['qemu_ui']['outputTruncated'] = True
        self.assertFalse(score(p)['readiness']['production_ready'])

    def test_hard_gate_and_blocker_prevent_ready_even_at_90(self):
        p = self.passing()
        review = json.loads(p['review'])
        review['categories']['data_integrity']['passed'] = False
        p['review'] = review
        result = score(p)['readiness']
        self.assertEqual(90, result['score'])
        self.assertFalse(result['production_ready'])
        p = self.passing()
        p['review'] = dict(json.loads(p['review']), blockers=['restore not tested'])
        self.assertFalse(score(p)['readiness']['production_ready'])

    def test_scoring_failure_blocks_even_perfect_readiness(self):
        p = self.passing()
        p["implementation_gate"] = {"passed": False, "blockers": ["security confidence below floor"]}
        readiness = score(p)["readiness"]
        self.assertEqual(100, readiness["score"])
        self.assertFalse(readiness["production_ready"])
        self.assertIn("security confidence below floor", readiness["blockers"])
        fresh = prepare(p)
        self.assertIsNone(fresh["implementation_gate"])
        self.assertIsNone(fresh["requirements_gate"])

    def test_source_archive_and_unsafe_paths(self):
        p = self.packaged()
        blob = base64.b64decode(p['artifacts'][-1]['base64'])
        self.assertEqual(b'source', zipfile.ZipFile(io.BytesIO(blob)).read('README.md'))
        for path in ('../secret', '/etc/passwd', 'a/../b', 'a\\b', '.env', 'x/.git/config'):
            bad = package({'implementation': {'files': [{'path': path, 'content': 'bad'}], 'preview_url': 'http://preview.test'}})
            self.assertFalse(bad['implementation_ready'])
            self.assertFalse(any(a['filename'].endswith('.zip') for a in bad['artifacts']))

    def test_prepare_clears_prior_evidence_even_when_pipeline_merges_output(self):
        previous = self.passing()
        previous['readiness'] = {'production_ready': True}
        result = prepare(previous)
        self.assertIsNone(result['qemu_ui'])
        self.assertIsNone(result['review'])
        self.assertIsNone(result['readiness'])
        self.assertIsNotNone(result['previous_readiness'])
        self.assertNotEqual(previous['verification_id'], result['verification_id'])

    def test_manifest_has_handoffs_qemu_gates_artifacts_and_bounded_repeat(self):
        manifest = build()
        stages = manifest['JobChains'][0]['Stages']
        self.assertEqual('agentTask', stages[1]['Action']['Type'])
        self.assertEqual('qemu', stages[7]['Action']['Type'])
        self.assertEqual('eq:implementation_gate.passed:true', stages[7]['Gate']['Expression'])
        self.assertEqual(3, stages[-1]['Action']['MaxAttempts'])
        self.assertTrue(all(3 in job['PostJobActions'] for job in manifest['Jobs']))
        self.assertEqual([], manifest['Triggers'])


if __name__ == '__main__':
    unittest.main()
