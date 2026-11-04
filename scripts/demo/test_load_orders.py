import json
import unittest
from load_orders import prepare


class LoadOrdersTests(unittest.TestCase):
    def records(self, count):
        return [{'order_id': f'LOAD-{i:06d}', 'status': 'delivered', 'currency': 'AUD',
                 'total_cents': 10000, 'placed_at': '2026-11-04T00:00:00Z'} for i in range(1, count + 1)]

    def test_retries_use_same_key_and_changed_data_changes_key(self):
        rows = self.records(250)
        body, key = prepare(rows, 1, 250)
        self.assertEqual((body, key), prepare(rows, 1, 250))
        self.assertEqual(len(json.loads(body)['records']), 250)
        rows[0]['status'] = 'paid'
        self.assertNotEqual(key, prepare(rows, 1, 250)[1])

    def test_missing_or_reordered_records_fail(self):
        for rows in [self.records(2)[:1], self.records(2)[::-1]]:
            with self.assertRaises(ValueError):
                prepare(rows, 1, 2)

    def test_context_budget_is_checked_before_submission(self):
        rows = self.records(250)
        rows[0]['extra'] = 'x' * 60000
        with self.assertRaises(ValueError):
            prepare(rows, 1, 250)


if __name__ == '__main__':
    unittest.main()
