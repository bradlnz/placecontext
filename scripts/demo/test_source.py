import unittest

from seed_source import source_tables


class SourceDemoTests(unittest.TestCase):
    def test_bounded_fixture_preserves_references_and_totals(self):
        tables = source_tables()
        self.assertEqual(tables, source_tables())
        self.assertEqual(200, len(tables["orders"]))
        orders = {row["order_id"] for row in tables["orders"]}
        customers = {row["customer_id"] for row in tables["customers"]}
        products = {row["sku"] for row in tables["products"]}
        for rows in tables.values():
            self.assertTrue(0 < len(rows) <= 1000)
            for row in rows:
                if "order_id" in row:
                    self.assertIn(row["order_id"], orders)
                if "customer_id" in row:
                    self.assertIn(row["customer_id"], customers)
                if "sku" in row:
                    self.assertIn(row["sku"], products)


if __name__ == "__main__":
    unittest.main()
