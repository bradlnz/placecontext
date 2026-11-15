import unittest
from job import run


class DemoJobsTests(unittest.TestCase):
    def setUp(self):
        self.data = dict(request_id="7e40de73-2c3e-488e-84e5-f4cae967832e", demo=True,
                         email="jordan@example.test", question="I was charged twice for my invoice.")

    def test_intake_rejects_real_contact(self):
        self.assertTrue(run(self.data, "validate")["intake_validated"])
        with self.assertRaises(ValueError):
            run({**self.data, "email":"person@real-company.com"}, "validate")

    def test_review_and_roster_are_required(self):
        data = {"routing":{"person":"Maya Chen", "department":"Billing"},
                "team":{"rows":[{"person":"Maya Chen", "department":"Billing"}]}, "review":{"passed":True}}
        self.assertTrue(run(data, "guard")["routing_verified"])
        with self.assertRaises(ValueError):
            run({**data, "review":{"passed":False}}, "guard")
        with self.assertRaises(ValueError):
            run({**data, "routing":{"person":"Other person", "department":"Billing"}}, "guard")

    def test_receipt_checks_actual_saved_assignee(self):
        data = {**self.data, "routing":{"person":"Maya Chen", "department":"Billing", "reason":"Duplicate charge"},
                "assignments":{"rows":[{"request_id":self.data["request_id"], "assigned_to":"Maya Chen", "status":"Assigned"}]}}
        self.assertTrue(run(data, "receipt")["receipt"]["browser_assignment_verified"])
        data["assignments"]["rows"][0]["assigned_to"] = "Alex Morgan"
        with self.assertRaises(ValueError):
            run(data, "receipt")


if __name__ == "__main__":
    unittest.main()
