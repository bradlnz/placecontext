import copy
import unittest

from map_order_groups import organize


class OrderGroupingTests(unittest.TestCase):
    def payload(self, score=0.98, **changes):
        return {"records": [{"order_id": "ORD-1", "status": "delivered", "currency": "AUD",
                              "total_cents": 2500, "input_source_id": "source-1", **changes}],
                "classification": {"type": "noul", "noul": score, "model": "test-model"}}

    def test_accepted_group_preserves_source_evidence(self):
        payload = self.payload()
        original = copy.deepcopy(payload)
        row = organize(payload)["records"][0]
        self.assertEqual(row["group_name"], "completed_orders")
        self.assertEqual(row["tags"], ["delivered", "low_value"])
        self.assertEqual(row["noul"], 0.98)
        self.assertEqual(row["classification_model"], "test-model")
        self.assertEqual(row["original_record"], payload["records"][0])
        self.assertEqual(payload, original)

    def test_threshold_and_review_branch(self):
        self.assertEqual(len(organize(self.payload(0.9))["records"]), 1)
        for score, forced in [(0.899, False), (0.99, True)]:
            result = organize(self.payload(score), force_review=forced)
            self.assertEqual(result["records"], [])
            self.assertEqual(result["review_records"][0]["group_name"], "needs_review")

    def test_missing_or_malformed_ai_evidence_never_accepts(self):
        for evidence in [None, 'external response', {}, {"noul": 1},
                         *[{"type": "noul", "model": "test", "noul": score}
                           for score in [True, float("nan"), 1.1, -0.1, "0.99"]]]:
            payload = self.payload()
            payload["classification"] = evidence
            self.assertEqual(organize(payload)["records"], [])

    def test_unsupported_record_is_held_even_with_high_ai_score(self):
        for changes in [{"status": "unclear"}, {"currency": "USD"}, {"total_cents": -1},
                        {"total_cents": True}, {"total_cents": 1.5}]:
            result = organize(self.payload(**changes))
            self.assertEqual(result["records"], [])
            self.assertEqual(len(result["review_records"]), 1)

    def test_duplicate_ids_fail_closed(self):
        payload = self.payload()
        payload["records"] *= 2
        with self.assertRaises(ValueError):
            organize(payload)

    def test_batch_limit(self):
        payload = self.payload()
        row = payload["records"][0]
        payload["records"] = [{**row, "order_id": f"ORD-{i}"} for i in range(250)]
        self.assertEqual(len(organize(payload)["records"]), 250)
        payload["records"].append({**row, "order_id": "ORD-250"})
        with self.assertRaises(ValueError):
            organize(payload)


if __name__ == "__main__":
    unittest.main()
