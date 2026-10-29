"""Synthetic invoice review; the workflow selects each stage through DEMO_STAGE."""
import json
import math
import os
import sys
import urllib.error
import urllib.request


def run(stage, data):
    if stage == "prepare":
        case = data.get("case", "correct")
        if case not in ("correct", "incorrect"):
            raise ValueError("case must be correct or incorrect")
        return {
            "case": case,
            "invoice": {
                "currency": "AUD",
                "items": [{"quantity": 2, "unit_price": 25}, {"quantity": 1, "unit_price": 10}],
                "total": 60 if case == "correct" else 600,
            },
        }
    if stage == "review":
        key = os.environ.get("TYPESAFE_API_KEY") or os.environ.get("PlaceContext__TypeSafe__ApiKey")
        if not key or any(c.isspace() or ord(c) < 32 for c in key):
            raise ValueError("Add TYPESAFE_API_KEY to this project's vault")
        question = "Does the invoice total equal the sum of quantity multiplied by unit_price for every item? No taxes, discounts or other charges apply."
        body = {"model": "jev-1.14.0", "state": data["invoice"],
                "questions": {"verification": {"type": "noul", "instructions": question}}}
        request = urllib.request.Request("https://api.typesafe.ai/v1/systemone",
            data=json.dumps(body).encode(), headers={"Content-Type": "application/json", "Authorization": "Bearer " + key})
        # Reject redirects so the vault credential is sent only to the configured provider.
        class NoRedirect(urllib.request.HTTPRedirectHandler):
            def redirect_request(self, *args, **kwargs):
                return None
        try:
            with urllib.request.build_opener(NoRedirect()).open(request, timeout=90) as response:
                raw = response.read(65537)
            if len(raw) > 65536:
                raise ValueError("Jev response exceeded 64 KiB")
            result = json.loads(raw)
            answer = result["answers"]["verification"]
            score = answer["noul"]
            if answer["type"] != "noul" or type(score) not in (int, float) or not math.isfinite(score) or not 0 <= score <= 1:
                raise ValueError("Invalid Jev score")
            data["review"] = {"noul": score, "model": result["model"], "question": question}
            return data
        except urllib.error.HTTPError as error:
            raise ValueError(f"Jev returned HTTP {error.code}; check key and quota") from None
        except urllib.error.URLError:
            raise ValueError("Could not reach Jev") from None
    if stage in ("approved", "needs_review"):
        data["outcome"] = stage
        return data
    if stage == "summary":
        data["summary"] = {"outcome": data["outcome"], "score": data["review"]["noul"],
                           "threshold": 0.8, "case": data["case"], "demo_only": True}
        return data
    raise ValueError("Unknown demo stage")


if __name__ == "__main__":
    try:
        print(json.dumps(run(os.environ["DEMO_STAGE"], json.load(sys.stdin)), allow_nan=False))
    except (ValueError, KeyError, TypeError, TimeoutError):
        print("Jev demo failed: check the stage input, vault key and provider availability.", file=sys.stderr)
        sys.exit(1)
