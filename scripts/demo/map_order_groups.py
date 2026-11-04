"""Map demo order groups only after the built-in AI's noul classification check."""
import json
import math
import sys

THRESHOLD = 0.9
STATUS_GROUPS = {
    "paid": ("active_orders", "fulfilment_pending"),
    "processing": ("active_orders", "fulfilment_pending"),
    "shipped": ("active_orders", "in_transit"),
    "delivered": ("completed_orders", "delivered"),
    "cancelled": ("cancellations", "cancelled"),
    "refunded": ("refunds", "refunded"),
}


def organize(payload, force_review=False):
    records = payload["records"]
    if not isinstance(records, list) or not 1 <= len(records) <= 250:
        raise ValueError("Expected 1–250 validated orders")
    verification = payload.get("classification")
    verification = verification if isinstance(verification, dict) else {}
    score = verification.get("noul")
    valid_score = (type(score) in (int, float) and math.isfinite(score) and 0 <= score <= 1
                   and verification.get("type") == "noul"
                   and isinstance(verification.get("model"), str) and bool(verification["model"].strip()))
    approved = valid_score and score >= THRESHOLD and not force_review
    accepted, review, seen = [], [], set()
    for record in records:
        key = record["order_id"]
        if not isinstance(key, str) or not key or key in seen:
            raise ValueError("Input order IDs must be nonempty and unique")
        seen.add(key)
        status, total = record.get("status"), record.get("total_cents")
        group, tag = STATUS_GROUPS.get(status, ("needs_review", "insufficient_evidence"))
        tags = [tag]
        valid_value = type(total) is int and total >= 0 and record.get("currency") == "AUD"
        if valid_value:
            tags.append("low_value" if total < 10000 else "medium_value" if total < 50000 else "high_value")
        needs_review = not approved or group == "needs_review" or not valid_value
        reason = f"Source status: {status}; total: {total} {record.get('currency')} cents. "
        reason += f"AI {verification['model']} noul={score}, threshold={THRESHOLD}." if valid_score else "Missing or invalid AI noul evidence."
        if needs_review:
            reason += " Held for review; not accepted for grouped analysis."
        row = {**record, "group_name": "needs_review" if needs_review else group,
               "proposed_group": group, "tags": tags, "confidence": score if valid_score else 0,
               "noul": score if valid_score else None,
               "classification_model": verification.get("model") if valid_score else None,
               "classification_reason": reason, "taxonomy_version": "orders-v2",
               "original_record": record}
        (review if needs_review else accepted).append(row)
    return {"records": accepted, "review_records": review,
            "summary": {"accepted": len(accepted), "needs_review": len(review)}}


if __name__ == "__main__":
    try:
        raw = sys.stdin.buffer.read(1_048_577)
        if len(raw) > 1_048_576:
            raise ValueError("Input exceeds 1 MiB")
        print(json.dumps(organize(json.loads(raw), force_review="--review" in sys.argv[1:]), allow_nan=False))
    except (ValueError, TypeError, KeyError, AttributeError) as error:
        print(json.dumps({"error": str(error)}))
        sys.exit(1)
