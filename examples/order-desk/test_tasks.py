import json
import unittest
from pathlib import Path
from uuid import uuid4
from domain import fulfil_order


class RealisticTasksTests(unittest.TestCase):
    def test_expected_results_match_the_actual_workflow_rules(self):
        tasks = json.loads(Path(__file__).with_name('tasks.json').read_text())
        self.assertEqual(len(tasks), 3)
        for task in tasks:
            with self.subTest(task=task['name']):
                result = fulfil_order({'id': str(uuid4()), **task['order']})
                for key, expected in task['expected'].items():
                    self.assertEqual(result[key], expected)
