"""Order Desk: PostgreSQL outbox → platform queue → three-job workflow → order result."""
import json
import os
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.error import HTTPError
from urllib.request import Request, urlopen

import psycopg
from psycopg.rows import dict_row
from domain import CATALOGUE, validate_order

STOP = threading.Event()


def connect():
    return psycopg.connect(os.environ["DATABASE_URL"], connect_timeout=5, row_factory=dict_row)


def platform(path, payload=None, key=None):
    headers = {"Authorization": "Bearer " + os.environ["PLACECONTEXT_API_TOKEN"], "Host": os.environ["PLACECONTEXT_TENANT_HOST"], "Accept": "application/json"}
    if key:
        headers["Idempotency-Key"] = key
    if payload is not None:
        headers["Content-Type"] = "application/json"
    req = Request(os.environ["PLACECONTEXT_API_URL"] + path, data=json.dumps(payload).encode() if payload is not None else None, headers=headers)
    with urlopen(req, timeout=10) as response:
        return json.load(response)


def initialize():
    with connect() as db:
        db.execute("""CREATE TABLE IF NOT EXISTS orders (
            id uuid PRIMARY KEY, customer text NOT NULL, sku text NOT NULL, quantity integer NOT NULL CHECK (quantity BETWEEN 1 AND 50),
            status text NOT NULL DEFAULT 'pending', tracking_id uuid, workflow_run_id uuid, result jsonb, error text,
            created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now())""")


def process_orders():
    queue = os.environ["PLACECONTEXT_QUEUE_IDS"].split(",")[0]
    endpoint = f"/api/apps/queues/{queue}/messages"
    with connect() as db:
        # Only one reconciler claims an order. An HTTP timeout can resubmit safely using the order ID.
        row = db.execute("SELECT * FROM orders WHERE status IN ('pending','queued','running') ORDER BY updated_at FOR UPDATE SKIP LOCKED LIMIT 1").fetchone()
        if row is None:
            return
        if row["tracking_id"] is None:
            order = {"id": str(row["id"]), "customer": row["customer"], "sku": row["sku"], "quantity": row["quantity"]}
            receipt = platform(endpoint, order, str(row["id"]))
            db.execute("UPDATE orders SET tracking_id=%s,workflow_run_id=%s,status='queued',updated_at=now() WHERE id=%s", (receipt["trackingId"], receipt["chainRunId"], row["id"]))
        else:
            data = platform(endpoint + "/" + str(row["tracking_id"]))
            receipt = data["receipt"]
            state = receipt["status"]
            if state in ("Succeeded", "Partial"):
                result = json.loads(data["output"] or "{}")
                if result.get("id") != str(row["id"]):
                    raise ValueError("Workflow returned a different order ID")
                db.execute("UPDATE orders SET status='ready',result=%s::jsonb,error=NULL,workflow_run_id=%s,updated_at=now() WHERE id=%s", (json.dumps(result), receipt["chainRunId"], row["id"]))
            elif state in ("Failed", "Expired", "Cancelled"):
                db.execute("UPDATE orders SET status='failed',error=%s,updated_at=now() WHERE id=%s", ((receipt.get("error") or state)[:1000], row["id"]))
            else:
                db.execute("UPDATE orders SET status=%s,workflow_run_id=%s,updated_at=now() WHERE id=%s", ("running" if state == "Running" else "queued", receipt["chainRunId"], row["id"]))
        print(json.dumps({"level": "INFO", "message": "Order reconciled", "order_id": str(row["id"])}))


def worker():
    initialized = False
    while not STOP.is_set():
        try:
            if not initialized:
                initialize()
                initialized = True
            process_orders()
        except (psycopg.Error, OSError, KeyError, ValueError):
            # Connection strings and authorization headers are never included in logs.
            print(json.dumps({"level": "WARN", "message": "Order processing waiting for database or platform; retrying"}))
        STOP.wait(2)


class App(BaseHTTPRequestHandler):
    def do_GET(self):
        try:
            if self.path == "/":
                return self.respond(200, Path("/app/index.html").read_bytes(), "text/html; charset=utf-8")
            if self.path == "/health":
                return self.respond(200, b"ok", "text/plain")
            if self.path == "/api/orders":
                with connect() as db:
                    rows = db.execute("SELECT * FROM orders ORDER BY created_at DESC LIMIT 100").fetchall()
                return self.respond(200, json.dumps(rows, default=str).encode())
            if self.path == "/api/catalogue":
                return self.respond(200, json.dumps(CATALOGUE).encode())
            return self.respond(404, b'{"error":"Not found"}')
        except (psycopg.Error, KeyError):
            return self.respond(503, b'{"error":"Database is not ready. Assign PostgreSQL to this app in Databases."}')

    def do_POST(self):
        if self.path != "/api/orders":
            return self.respond(404, b'{"error":"Not found"}')
        try:
            length = int(self.headers.get("Content-Length", "0"))
            if not 0 < length <= 8192:
                return self.respond(413, b'{"error":"Order must be at most 8 KiB"}')
            order = validate_order(json.loads(self.rfile.read(length)))
            with connect() as db:
                db.execute("INSERT INTO orders (id,customer,sku,quantity) VALUES (%s,%s,%s,%s) ON CONFLICT (id) DO NOTHING", (order["id"], order["customer"], order["sku"], order["quantity"]))
                existing = db.execute("SELECT customer,sku,quantity FROM orders WHERE id=%s", (order["id"],)).fetchone()
                if any(existing[key] != order[key] for key in ("customer", "sku", "quantity")):
                    return self.respond(409, b'{"error":"This order ID already has different details"}')
            print(json.dumps({"level": "INFO", "message": "Order accepted", "order_id": order["id"]}))
            return self.respond(202, json.dumps({"id": order["id"], "status": "pending"}).encode())
        except (ValueError, TypeError):
            return self.respond(400, b'{"error":"Invalid order. Check the customer, product, quantity and order ID."}')
        except (psycopg.Error, KeyError):
            return self.respond(503, b'{"error":"Database is not ready"}')

    def respond(self, status, body, content_type="application/json"):
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Content-Type-Options", "nosniff")
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *_):
        pass


if __name__ == "__main__":
    threading.Thread(target=worker, daemon=True).start()
    ThreadingHTTPServer(("0.0.0.0", 8080), App).serve_forever()
