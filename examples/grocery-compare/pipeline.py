"""Grocery evidence assessment and comparison; QEMU browser execution stays in Rust."""
import html
import json
import math
import os
import re
import sys
from datetime import datetime, timezone
from decimal import Decimal, ROUND_HALF_UP
from urllib.parse import urlsplit

RETAILERS = {"woolworths": ("Woolworths", "www.woolworths.com.au", "/shop/productdetails/"),
             "coles": ("Coles", "www.coles.com.au", "/product/")}
THRESHOLD = 0.85


def prepare(data):
    product = data.get("product_name", "")
    postcode = data.get("postcode") or "4101"
    if not isinstance(product, str) or not 2 <= len(product.strip()) <= 160 or any(ord(c) < 32 for c in product):
        raise ValueError("Enter a product name of 2–160 characters, ideally including brand and pack size")
    if not isinstance(postcode, str) or not re.fullmatch(r"\d{4}", postcode, re.ASCII):
        raise ValueError("Postcode must be four digits, for example 4101")
    return {"product_name": product.strip(), "postcode": postcode, "offers": []}


def pack_quantity(label):
    if not isinstance(label, str) or len(label) > 100:
        raise ValueError("Invalid visually read pack size")
    match = re.fullmatch(r"\s*(?:(\d+)\s*[x×]\s*)?(\d+(?:\.\d+)?)\s*(kg|ml|g|l|pack|pk)\s*", label, re.I)
    if not match:
        raise ValueError("Unsupported pack size")
    count, size, unit = match.groups()
    quantity = Decimal(count or "1") * Decimal(size)
    if quantity <= 0:
        raise ValueError("Invalid package quantity")
    unit = unit.lower()
    if unit in ("g", "ml"):
        quantity /= 1000
    return str(quantity), "kg" if unit in ("g", "kg") else "L" if unit in ("ml", "l") else "each"


def probability(value):
    if type(value) not in (int, float) or not math.isfinite(value) or not 0 <= value <= 1:
        raise ValueError("Invalid Jev probability")
    return value


def noul(answer):
    if answer.get("type") != "noul":
        raise ValueError("Invalid Jev yes/no answer")
    return probability(answer.get("noul"))


def assess(data, retailer):
    browser = data.pop("browser")
    name, host, product_path = RETAILERS[retailer]
    evidence = json.loads(browser["stdout"])
    source = evidence.get("final_url", "")
    parsed = urlsplit(source)
    # Keep only compact evidence between QEMU steps; the full replay stays in run history.
    offer = {"retailer": name, "source": source if parsed.scheme == "https" and parsed.hostname == host else None,
             "observed_at": datetime.now(timezone.utc).isoformat(), "product": evidence.get("final_title", ""),
             "status": "unverified", "reason": "No verified product price", "postcode": data["postcode"]}
    data["offers"].append(offer)
    if (browser["exitCode"] != 0 or browser.get("outputTruncated")
            or evidence.get("status") != "goal_achieved"):
        offer["reason"] = "Browser did not finish with complete evidence"
        return data
    visual = evidence.get("visual_evidence")
    if not isinstance(visual, dict) or visual.get("source") != "screenshot":
        offer["reason"] = "No screenshot-based Jev verification; HTML text is not price evidence"
        return data
    if offer["source"] is None or not parsed.path.startswith(product_path):
        offer["reason"] = "No retailer product detail page"
        return data
    checks = {key: probability(visual.get("confidence", {}).get(key))
              for key in ("price", "pack", "match", "location", "available")}
    offer["confidence"] = checks
    candidate = visual.get("offer", {})
    if min(checks.values()) < THRESHOLD:
        offer["reason"] = "Jev could not verify price, pack, product, location or availability from the screenshot"
        return data
    price, pack = candidate.get("price_aud"), candidate.get("pack")
    if (not isinstance(price, str) or not re.fullmatch(r"\d{1,4}\.\d{2}", price)
            or Decimal(price) <= 0 or candidate.get("postcode") != data["postcode"]
            or not isinstance(candidate.get("product"), str) or not 1 <= len(candidate["product"]) <= 300):
        offer["reason"] = "Invalid visually read price, product or postcode"
        return data
    try:
        quantity, unit = pack_quantity(pack)
    except ValueError:
        offer["reason"] = "Unsupported or unreadable package size"
        return data
    digest, frame = visual.get("sha256"), visual.get("frame")
    if not isinstance(digest, str) or not re.fullmatch(r"[a-f0-9]{64}", digest) or type(frame) is not int or frame < 0:
        offer["reason"] = "Missing captured screenshot reference"
        return data
    offer.update(status="verified", reason="Jev verified each claim against the captured screenshot",
                 product=candidate["product"], price_aud=price, pack=pack, quantity=quantity, unit=unit,
                 unit_price_aud=str(Decimal(price) / Decimal(quantity)),
                 evidence_frame=frame, evidence_sha256=digest)
    return data


def compare(data):
    offers = data["offers"]
    if len(offers) != 2 or {offer["retailer"] for offer in offers} != {v[0] for v in RETAILERS.values()}:
        raise ValueError("Both retailers must have an observation")
    result = {"status": "incomplete", "winners": [], "basis": None,
              "note": "A cheapest option requires two verified, comparable offers."}
    if all(offer["status"] == "verified" for offer in offers):
        if offers[0]["unit"] != offers[1]["unit"]:
            result["note"] = "Pack units differ; no comparison made."
        elif noul(data["comparability"]) < THRESHOLD:
            result["note"] = "Jev could not verify that these are the same product and variety."
        else:
            same_size = Decimal(offers[0]["quantity"]) == Decimal(offers[1]["quantity"])
            basis = "price_aud" if same_size else "unit_price_aud"
            amounts = [Decimal(offer[basis]) for offer in offers]
            low = min(amounts)
            result.update(status="tie" if amounts[0] == amounts[1] else "compared", basis=basis,
                          winners=[offer["retailer"] for offer in offers if Decimal(offer[basis]) == low],
                          difference_aud=str(abs(amounts[0] - amounts[1])),
                          note="Lowest observed price for the same pack." if same_size
                          else "Best observed unit value; pack sizes and checkout totals differ.")
    data["comparison"] = result
    return data


def report(data):
    esc = lambda value: html.escape(str(value), quote=True)
    money = lambda value: "$" + str(Decimal(value).quantize(Decimal("0.01"), rounding=ROUND_HALF_UP))
    result = data["comparison"]
    rows = []
    for offer in data["offers"]:
        verified = offer["status"] == "verified"
        source = offer.get("source")
        # Sources are emitted as links only for the two retailer HTTPS hosts.
        safe = source and urlsplit(source).scheme == "https" and urlsplit(source).hostname in {v[1] for v in RETAILERS.values()}
        link = f'<a href="{esc(source)}" rel="noopener noreferrer">View product</a>' if safe else "Unavailable"
        rows.append("<tr>" + "".join(f"<td>{value}</td>" for value in [esc(offer["retailer"]), esc(offer["product"]),
                    esc(offer.get("pack", "Unverified")), money(offer["price_aud"]) if verified else "Unverified",
                    money(offer["unit_price_aud"]) + "/" + esc(offer["unit"]) if verified else "Unverified",
                    esc(offer["reason"]), link, esc(offer["observed_at"])]) + "</tr>")
    headline = "No cheapest option verified" if not result["winners"] else (
        "Equal observed prices: " if result["status"] == "tie" else "Lowest observed pack price: " if result["basis"] == "price_aud" else "Best observed unit value: ") + ", ".join(result["winners"])
    return f'''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Grocery comparison — {esc(data['product_name'])}</title>
<style>body{{font:16px system-ui;margin:32px;color:#193c32}}table{{border-collapse:collapse;width:100%}}td,th{{text-align:left;padding:12px;border-bottom:1px solid #ccd8d0;vertical-align:top}}.table{{overflow-x:auto}}small{{color:#486052}}</style>
<h1>{esc(data['product_name'])}</h1><p>Requested postcode: {esc(data['postcode'])} · AUD · Coles and Woolworths</p>
<h2>{esc(headline)}</h2><p>{esc(result['note'])}</p>
<div class="table"><table><thead><tr><th>Retailer</th><th>Observed product</th><th>Pack</th><th>Item price</th><th>Unit price</th><th>Evidence status</th><th>Source</th><th>Checked (UTC)</th></tr></thead><tbody>{''.join(rows)}</tbody></table></div>
<p><small>Comparison of observed online offers, not every Australian retailer or every pack on sale. Delivery fees, loyalty-only prices and multibuy conditions are excluded. Prices and availability can change. Jev assessments can be wrong; verify the linked offer before buying.</small></p></html>'''


def run(stage, data):
    if stage == "prepare":
        return prepare(data)
    if stage in RETAILERS:
        return assess(data, stage)
    if stage == "compare":
        return compare(data)
    if stage == "report":
        return report(data)
    raise ValueError("Unknown grocery workflow stage")


if __name__ == "__main__":
    try:
        output = run(os.environ["GROCERY_STAGE"], json.load(sys.stdin))
        print(output if isinstance(output, str) else json.dumps(output, allow_nan=False))
    except (ValueError, KeyError, TypeError, OSError):
        print("Grocery workflow stopped: check input, browser evidence, project TYPESAFE_API_KEY and provider availability.", file=sys.stderr)
        sys.exit(1)
