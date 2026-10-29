"""Order Desk business rules, shared by the web app and workflow jobs."""
import re
from uuid import UUID

CATALOGUE = {"starter": {"name": "Starter kit", "cents": 2900}, "team": {"name": "Team kit", "cents": 7900}, "studio": {"name": "Studio kit", "cents": 14900}}


def validate_order(value):
    if not isinstance(value, dict):
        raise ValueError("Send an order object")
    try:
        order_id = str(UUID(str(value["id"])))
    except (KeyError, ValueError, TypeError):
        raise ValueError("A valid order ID is required") from None
    customer = value.get("customer", "")
    sku = value.get("sku")
    quantity = value.get("quantity")
    if not isinstance(customer, str) or not re.fullmatch(r"[^\s@]{1,100}@[^\s@]{1,100}\.[^\s@]{2,20}", customer):
        raise ValueError("Enter a valid customer email")
    if sku not in CATALOGUE or type(quantity) is not int or not 1 <= quantity <= 50:
        raise ValueError("Choose a product and a quantity between 1 and 50")
    return {"id": order_id, "customer": customer, "sku": sku, "quantity": quantity}


def price_order(value):
    order = validate_order(value)
    subtotal = CATALOGUE[order["sku"]]["cents"] * order["quantity"]
    shipping = 0 if subtotal >= 10000 else 900
    return {**order, "subtotal_cents": subtotal, "shipping_cents": shipping, "total_cents": subtotal + shipping, "currency": "AUD"}


def fulfil_order(value):
    order = price_order(value)
    return {**order, "status": "ready", "reference": "OD-" + order["id"][:8].upper(), "review_required": order["total_cents"] >= 100000}
