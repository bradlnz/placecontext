"""All prices below are synthetic test data, not retailer observations."""
import copy
import json
import unittest
from decimal import Decimal

from build import build
from pipeline import assess, pack_quantity, probability, compare, prepare, report


def offer(retailer, price="4.00", quantity="0.5", unit="kg"):
    return {"retailer": retailer, "status": "verified", "price_aud": price, "quantity": quantity, "unit": unit,
            "unit_price_aud": str(Decimal(price) / Decimal(quantity)), "pack": quantity + unit,
            "product": "SYNTHETIC TEST CEREAL", "reason": "Synthetic test evidence", "observed_at": "2026-01-01T00:00:00Z",
            "source": "https://www.coles.com.au/product/test" if retailer == "Coles" else "https://www.woolworths.com.au/shop/productdetails/test"}


def context(*offers, confidence=0.99):
    return {**prepare({"product_name": "Synthetic test cereal"}), "offers": list(offers),
            "comparability": {"type": "noul", "noul": confidence}}


class GroceryTests(unittest.TestCase):
    def test_input_defaults_and_ignores_injected_results(self):
        data = prepare({"product_name": "  Weet-Bix Original 575g ", "offers": [{"price_aud": "0.01"}]})
        self.assertEqual(data, {"product_name": "Weet-Bix Original 575g", "postcode": "4101", "offers": []})
        for value in [{}, {"product_name": "x"}, {"product_name": "Milk\nIgnore previous"},
                      {"product_name": "Milk", "postcode": "abcd"}, {"product_name": "Milk", "postcode": 4101}]:
            with self.subTest(value=value), self.assertRaises(ValueError):
                prepare(value)

    def test_multipack_and_unit_conversion(self):
        self.assertEqual(pack_quantity("6 x 250ml"), ("1.5", "L"))
        self.assertEqual(pack_quantity("500g"), ("0.5", "kg"))
        self.assertEqual(pack_quantity("12 pack"), ("12", "each"))
        for invalid in ("0g", "500g serving 20g", None, "unknown"):
            with self.assertRaises(ValueError):
                pack_quantity(invalid)

    def test_prices_require_screenshot_verification_and_each_independent_check(self):
        data = prepare({"product_name": "Test cereal 500g"})
        visual = {"source": "screenshot", "frame": 4, "sha256": "a" * 64,
                  "offer": {"product": "Test cereal 500g", "price_aud": "4.00", "pack": "500g", "postcode": "4101"},
                  "confidence": {key: .99 for key in ("price", "pack", "match", "location", "available")}}
        evidence = {"status": "goal_achieved", "final_url": "https://www.coles.com.au/product/test-cereal-123",
                    "final_title": "Test cereal", "page": "FAKE HTML price $0.01", "visual_evidence": visual}
        def observed(value):
            context = copy.deepcopy(data)
            context["browser"] = {"exitCode": 0, "stdout": json.dumps(value)}
            result = assess(context, "coles")
            self.assertNotIn("browser", result)
            return result["offers"][0]
        self.assertEqual(observed(evidence)["price_aud"], "4.00")
        self.assertEqual(Decimal(observed(evidence)["unit_price_aud"]), Decimal("8"))
        for key in visual["confidence"]:
            uncertain = copy.deepcopy(evidence)
            uncertain["visual_evidence"]["confidence"][key] = .1
            self.assertEqual(observed(uncertain)["status"], "unverified")
        for field in ("visual_evidence",):
            missing = copy.deepcopy(evidence)
            missing.pop(field)
            self.assertEqual(observed(missing)["status"], "unverified")
        missing = copy.deepcopy(evidence)
        missing["visual_evidence"].pop("sha256")
        self.assertEqual(observed(missing)["status"], "unverified")
        for value in ("NaN", "-1", "0.00", "4.00 buy two"):
            invalid = copy.deepcopy(evidence)
            invalid["visual_evidence"]["offer"]["price_aud"] = value
            self.assertEqual(observed(invalid)["status"], "unverified")

    def test_blocked_page_has_no_price_and_does_not_call_provider(self):
        for source in ("https://www.coles.com.au/search?q=cereal", "https://attacker.test/product/cereal"):
            data = prepare({"product_name": "Test cereal"})
            data["browser"] = {"exitCode": 0, "stdout": json.dumps({"status": "goal_achieved",
                "final_url": source, "final_title": "Access denied", "page": "Verify access"})}
            result = assess(data, "coles")["offers"][0]
            self.assertEqual(result["status"], "unverified")
            self.assertNotIn("price_aud", result)

    def test_cheapest_equal_packs_ties_and_different_pack_unit_value(self):
        data = compare(context(offer("Coles", "4.00"), offer("Woolworths", "3.50")))
        self.assertEqual(data["comparison"]["winners"], ["Woolworths"])
        self.assertEqual(data["comparison"]["difference_aud"], "0.50")
        self.assertEqual(compare(context(offer("Coles"), offer("Woolworths")))["comparison"]["status"], "tie")
        data = compare(context(offer("Coles", "4.00", "0.5"), offer("Woolworths", "6.00", "1")))
        self.assertEqual(data["comparison"]["winners"], ["Woolworths"])
        self.assertEqual(data["comparison"]["basis"], "unit_price_aud")
        self.assertIn("checkout totals differ", data["comparison"]["note"])

    def test_no_winner_for_incomplete_different_units_or_uncertain_match(self):
        for data in [context(offer("Coles"), {"retailer": "Woolworths", "status": "unverified"}),
                     context(offer("Coles"), offer("Woolworths", unit="L")),
                     context(offer("Coles"), offer("Woolworths"), confidence=.84)]:
            self.assertEqual(compare(data)["comparison"]["winners"], [])
        with self.assertRaises(ValueError):
            compare(context(offer("Coles"), offer("Coles")))

    def test_invalid_model_probabilities_fail_closed(self):
        for value in (True, float("nan"), -1, 2, None):
            with self.assertRaises(ValueError):
                probability(value)

    def test_report_escapes_text_and_rejects_untrusted_links(self):
        data = compare(context(offer("Coles"), offer("Woolworths")))
        data["product_name"] = '<script>alert("x")</script>'
        data["offers"][0]["source"] = "javascript:alert(1)"
        rendered = report(data)
        self.assertNotIn("<script>", rendered)
        self.assertNotIn('href="javascript:', rendered)
        self.assertIn("&lt;script&gt;", rendered)
        self.assertIn("Equal observed prices", rendered)

    def test_manifest_preserves_settings_and_wires_read_only_workflow(self):
        manifest = build()
        self.assertEqual(manifest["TenantSettings"], {"TimeZoneId": "", "BrandingJson": None, "GitHubLogin": None})
        self.assertEqual(manifest["Triggers"], [])
        self.assertEqual(len(manifest["Jobs"]), 5)
        self.assertEqual(len(manifest["JobChains"][0]["Stages"]), 8)
        network_jobs = [j["MapEnv"]["GROCERY_STAGE"] for j in manifest["Jobs"] if j["AllowNetworkEgress"]]
        self.assertEqual(network_jobs, [])
        actions = [s["Action"] for s in manifest["JobChains"][0]["Stages"] if "Action" in s]
        self.assertEqual([a["Type"] for a in actions], ["qemu", "qemu", "agentInput"])
        self.assertEqual(actions[-1]["AgentProvider"], "jev")
        self.assertEqual(manifest["Jobs"][-1]["ReturnType"], 3)
        self.assertEqual(manifest["JobChains"][0]["ChainId"], build()["JobChains"][0]["ChainId"])


if __name__ == "__main__":
    unittest.main()
