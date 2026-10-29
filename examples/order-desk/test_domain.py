import unittest
from domain import validate_order, price_order, fulfil_order

ORDER = {"id": "b1603b0c-32f3-47b3-b0f4-2c8117f7c051", "customer": "customer@example.com", "sku": "starter", "quantity": 2}


class Boundaries(unittest.TestCase):
    def test_pricing_ignores_client_supplied_totals(self):
        self.assertEqual(price_order({**ORDER, "total_cents": 1})["total_cents"], 6700)

    def test_workflow_is_deterministic_for_retries(self):
        self.assertEqual(fulfil_order(price_order(validate_order(ORDER))), fulfil_order(ORDER))

    def test_invalid_and_extreme_inputs(self):
        for changes in ({"quantity": 0}, {"quantity": -1}, {"quantity": 51}, {"quantity": True}, {"quantity": "2"}, {"sku": "unknown"}, {"id": "../other"}, {"customer": "<script>"}):
            with self.subTest(changes=changes), self.assertRaises(ValueError):
                validate_order({**ORDER, **changes})

    def test_free_shipping_threshold_and_review_boundary(self):
        self.assertEqual(price_order({**ORDER, "sku": "studio", "quantity": 1})["shipping_cents"], 0)
        self.assertTrue(fulfil_order({**ORDER, "sku": "studio", "quantity": 50})["review_required"])


if __name__ == "__main__":
    unittest.main()
