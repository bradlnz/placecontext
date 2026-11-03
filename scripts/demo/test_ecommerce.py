"""Checks the fixture, workflow links, and offline sample jobs without a database."""
from datetime import datetime, timezone
import json
import unittest

from ecommerce_job import process
from seed_ecommerce import FLOWS, generate, identity, metadata


class EcommerceDemoTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.as_of = datetime(2026, 11, 3, tzinfo=timezone.utc)
        cls.tables = generate(cls.as_of)
        cls.rows = metadata(identity("tenant"), identity("project"), cls.as_of, cls.tables)

    def test_deterministic_data_reconciles_and_has_valid_references(self):
        self.assertEqual(self.tables, generate(self.as_of))
        orders = {row["order_id"]: row for row in self.tables["orders"]}
        customers = {row["customer_id"] for row in self.tables["customers"]}
        products = {row["sku"] for row in self.tables["products"]}
        for order in orders.values():
            self.assertIn(order["customer_id"], customers)
            self.assertLess(order["placed_at"], self.as_of)
        for item in self.tables["order_items"]:
            self.assertIn(item["order_id"], orders)
            self.assertIn(item["sku"], products)
        for payment in self.tables["payments"]:
            order = orders[payment["order_id"]]
            self.assertEqual(payment["amount_cents"], 0 if order["status"] == "cancelled" else order["total_cents"])
        for shipment in self.tables["shipments"]:
            self.assertIn(shipment["order_id"], orders)
            if shipment["delivered_at"]:
                self.assertLessEqual(shipment["shipped_at"], shipment["delivered_at"])
                self.assertLessEqual(shipment["delivered_at"], self.as_of)

    def test_history_links_to_jobs_and_dashboard_charts_are_renderable(self):
        jobs = {row["Id"] for row in self.rows["jobs"]}
        runs = {row["Id"]: row for row in self.rows["job_runs"]}
        for chain in self.rows["chain_runs"]:
            self.assertLessEqual(chain["FinishedAt"], self.as_of)
            self.assertTrue(json.loads(chain["FinalOutput"])["simulated"])
            for step in chain["StepsJson"]:
                self.assertIn(step["jobId"], jobs)
                if step["runId"]:
                    self.assertEqual(runs[step["runId"]]["JobId"], step["jobId"])
        self.assertEqual(len(self.rows["chain_runs"]), 360)
        self.assertEqual(len(self.rows["project_charts"]), 6)
        for chart in self.rows["project_charts"]:
            self.assertTrue(chart["TableName"].startswith("sql:"))
            self.assertTrue(chart["Html"]["sql"].startswith("SELECT"))
            for series in chart["Html"]["series"]:
                self.assertEqual(len(series["values"]), len(chart["Html"]["labels"]))

    def test_all_workflow_operations_accept_sample_and_preserve_orders(self):
        sample = json.loads(self.rows["jobs"][0]["InputPayloadsJson"][0])
        for _, stages in FLOWS:
            output = sample
            for stage in stages:
                for operation in stage:
                    output = process(output, operation)
            self.assertEqual(output["orders"], sample["orders"])
            self.assertEqual(output["records"], 25)
        for payload in [{}, {"orders": []}, {"orders": [{"order_id": "bad", "total_cents": -1}]}, {"orders": sample["orders"] * 2}]:
            with self.assertRaises(ValueError):
                process(payload, "validate")


if __name__ == "__main__":
    unittest.main()
