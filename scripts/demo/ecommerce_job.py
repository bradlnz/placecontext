"""Offline example job for the Harbour & Pine data workflow demo."""
import json
import os
import sys
from collections import Counter


def process(payload, operation):
    orders = payload.get("orders", [])
    if not isinstance(orders, list) or not orders:
        raise ValueError("Expected a non-empty orders array from the ingestion step")
    for order in orders:
        if not order.get("order_id") or not isinstance(order.get("total_cents"), int):
            raise ValueError("Order contract requires order_id and integer total_cents")
        if order["total_cents"] < 0:
            raise ValueError("Order totals must not be negative")
    if len({row["order_id"] for row in orders}) != len(orders):
        raise ValueError("Duplicate order_id in input batch")
    result = dict(payload)
    result["demo"] = True
    result["orders"] = orders
    result["records"] = len(orders)
    if operation == "ingest":
        result["source"] = "embedded ecommerce sample; no external store connected"
        return result
    if operation == "validate":
        result["quality"] = {"valid": True, "duplicates": 0, "records_checked": len(orders)}
        return result
    if operation == "reconcile":
        result["reconciliation"] = {"currency": "AUD", "gross_cents": sum(o["total_cents"] for o in orders)}
        return result
    if operation == "inventory":
        result["inventory_review"] = [{"sku": "HP-001", "available": 3, "reorder_point": 20, "action": "review purchase order"}]
        return result
    if operation == "returns":
        result["returns_review"] = {"refunds": sum(o["status"] == "refunded" for o in orders), "action": "review only; no refunds issued"}
        return result
    if operation == "segments":
        result["channel_counts"] = dict(Counter(o["channel"] for o in orders))
        return result
    if operation == "report":
        result["report"] = {"title": "Ecommerce batch summary", "orders": len(orders), "gross_aud": round(sum(o["total_cents"] for o in orders) / 100, 2)}
        return result
    if operation == "publish":
        result["delivery"] = "Demo report ready for inspection; nothing sent externally"
        return result
    raise ValueError(f"Unknown demo operation: {operation}")


if __name__ == "__main__":
    operation = os.environ.get("DEMO_OPERATION", "validate")
    try:
        output = process(json.load(sys.stdin), operation)
        print(f"[demo] {operation}: processed {output['records']} records", file=sys.stderr)
        print(json.dumps(output))
    except (ValueError, TypeError, KeyError) as error:
        print(f"[demo] data contract failed: {error}", file=sys.stderr)
        sys.exit(1)
