import io
import json
import os
import unittest
from unittest.mock import patch

from pipeline import run


class PipelineTests(unittest.TestCase):
    def test_both_outcomes_and_request(self):
        for case, score, outcome in [("correct", 0.99, "approved"), ("incorrect", 0.01, "needs_review")]:
            data = run("prepare", {"case": case})
            response = {"model": "jev-1.14.0", "answers": {"verification": {"type": "noul", "noul": score}}}
            with patch.dict(os.environ, {"TYPESAFE_API_KEY": "test-key"}), patch("urllib.request.build_opener") as opener:
                opener.return_value.open.return_value = io.BytesIO(json.dumps(response).encode())
                reviewed = run("review", data)
                request = opener.return_value.open.call_args.args[0]
                self.assertEqual(request.full_url, "https://api.typesafe.ai/v1/systemone")
                self.assertEqual(json.loads(request.data)["state"], data["invoice"])
            routed = run(outcome, reviewed)
            summary = run("summary", routed)["summary"]
            self.assertEqual(summary["outcome"], outcome)
            self.assertEqual(summary["score"] >= 0.8, outcome == "approved")
            self.assertNotIn("test-key", json.dumps(routed))

    def test_invalid_scores_fail(self):
        for score in [True, -0.1, 1.1, "0.9", float("nan")]:
            response = {"model": "jev", "answers": {"verification": {"type": "noul", "noul": score}}}
            with patch.dict(os.environ, {"TYPESAFE_API_KEY": "test-key"}), patch("urllib.request.build_opener") as opener:
                opener.return_value.open.return_value = io.BytesIO(json.dumps(response).encode())
                with self.assertRaises(ValueError):
                    run("review", run("prepare", {}))


if __name__ == "__main__":
    unittest.main()
