#!/usr/bin/env python3
"""Add a deterministic ecommerce demo to the local development PostgreSQL container."""
import argparse
from collections import Counter, defaultdict
from datetime import datetime, timedelta, timezone
import json
from pathlib import Path
import random
import subprocess
import uuid

NAMESPACE = uuid.UUID("bf0fb877-6e90-4b5b-a4eb-2f0eaa902ddd")
PROJECT_PATH = "/demos/harbour-and-pine-ecommerce"
PROJECT_NAME = "Harbour & Pine · Ecommerce demo"
CHANNELS = ["Organic search", "Paid search", "Email", "Social", "Direct", "Marketplace"]
CATEGORIES = ["Kitchen", "Home", "Outdoor", "Travel", "Wellness", "Accessories"]
JOB_NAMES = {
    "ingest": "Ingest storefront orders", "validate": "Validate order data contract",
    "reconcile": "Reconcile payments", "inventory": "Flag low inventory",
    "returns": "Review return exceptions", "segments": "Aggregate sales channels",
    "report": "Build trading summary", "publish": "Prepare report delivery",
}
FLOWS = [
    ("Order ingestion and quality", [["ingest"], ["validate"], ["reconcile"], ["report"]]),
    ("Daily trading report", [["ingest"], ["validate"], ["segments", "reconcile"], ["report"], ["publish"]]),
    ("Inventory replenishment review", [["ingest"], ["validate"], ["inventory"], ["report"]]),
    ("Returns and refund exceptions", [["ingest"], ["validate"], ["returns"], ["report"]]),
    ("Marketing channel performance", [["ingest"], ["segments"], ["report"], ["publish"]]),
    ("Finance reconciliation", [["ingest"], ["validate"], ["reconcile", "returns"], ["report"]]),
]


CHART_QUERIES = {
    "daily_sales": "SELECT week, net_sales_aud FROM (SELECT to_char(date_trunc('week', placed_at), 'IYYY-\"W\"IW') AS week, round(sum(total_cents)/100.0,2) AS net_sales_aud FROM orders WHERE status NOT IN ('cancelled','refunded') GROUP BY 1 ORDER BY 1 DESC LIMIT 24) weeks ORDER BY week",
    "orders": "SELECT status, count(*) AS orders FROM orders GROUP BY status ORDER BY status",
    "returns": "SELECT reason, count(*) AS returns FROM returns GROUP BY reason ORDER BY returns DESC",
    "support_tickets": "SELECT status, count(*) AS tickets FROM support_tickets GROUP BY status ORDER BY status",
    "channel_performance": "SELECT channel, net_sales_aud FROM channel_performance ORDER BY channel",
    "inventory_alerts": "SELECT warehouse, count(*) AS low_stock_locations FROM inventory_alerts GROUP BY warehouse ORDER BY warehouse",
}


def identity(name):
    return str(uuid.uuid5(NAMESPACE, name))


def literal(value):
    if value is None:
        return "NULL"
    if isinstance(value, bool):
        return "TRUE" if value else "FALSE"
    if isinstance(value, (int, float)):
        return str(value)
    if isinstance(value, (dict, list)):
        value = json.dumps(value, separators=(",", ":"))
    if isinstance(value, datetime):
        value = value.isoformat()
    return "'" + str(value).replace("'", "''") + "'"


def insert(table, rows):
    if not rows:
        return ""
    columns = ",".join('"' + name + '"' for name in rows[0])
    statements = []
    for start in range(0, len(rows), 500):
        values = ",\n".join("(" + ",".join(literal(row[key]) for key in rows[0]) + ")" for row in rows[start:start + 500])
        statements.append(f"INSERT INTO {table} ({columns}) VALUES {values} ON CONFLICT DO NOTHING;")
    return "\n".join(statements)


def generate(as_of):
    rng = random.Random(1842)
    tables = {name: [] for name in ["customers", "products", "orders", "order_items", "payments", "shipments", "returns", "inventory", "support_tickets"]}
    first_names = ["Alex", "Morgan", "Taylor", "Sam", "Jordan", "Casey", "Jamie", "Riley", "Avery", "Charlie", "Kai", "Sasha"]
    last_names = ["Wilson", "Nguyen", "Patel", "Martin", "Chen", "Williams", "Singh", "Brown", "Garcia", "Lee", "Walker", "Thompson"]
    cities = ["Brisbane", "Sydney", "Melbourne", "Perth", "Adelaide", "Hobart", "Canberra", "Darwin"]
    products = ["Storage jar", "Linen throw", "Trail bottle", "Packing cube", "Yoga mat", "Canvas tote"]
    for i in range(2000):
        tables["customers"].append(dict(customer_id=f"C{i+1:05}", name=f"{rng.choice(first_names)} {rng.choice(last_names)}", email=f"shopper{i+1}@example.test", city=rng.choice(cities), segment=rng.choices(["New", "Returning", "VIP", "At risk"], [25, 50, 15, 10])[0], created_at=as_of-timedelta(days=rng.randint(190, 360)), marketing_opt_in=rng.random() < .64))
    for i in range(180):
        price = rng.randrange(12, 180) * 100 + 95
        tables["products"].append(dict(sku=f"HP-{i+1:03}", name=f"{products[i%6]} · {['Natural','Ocean','Forest','Charcoal','Clay'][i%5]} {i//30+1}", category=CATEGORIES[i%6], price_cents=price, cost_cents=int(price * rng.uniform(.3, .65)), active=i%23 != 0))
    weights = [(1 + d / 180) * (1.8 if 135 <= d <= 144 else 1) for d in range(180)]
    for i in range(8000):
        order_id = f"HP-{100001+i}"
        day = rng.choices(range(180), weights)[0]
        placed = as_of - timedelta(days=180-day, minutes=rng.randrange(1440))
        age = (as_of-placed).days
        status = rng.choices(["delivered", "refunded", "cancelled"], [88, 7, 5])[0]
        if age < 7:
            status = rng.choice(["paid", "processing", "shipped", "delivered", "cancelled"])
        subtotal = 0
        for product in rng.sample(tables["products"], rng.randint(1, 5)):
            quantity = rng.choices([1, 2, 3], [80, 17, 3])[0]
            line_total = quantity * product["price_cents"]
            subtotal += line_total
            tables["order_items"].append(dict(line_id=f"{order_id}-{product['sku']}", order_id=order_id, sku=product["sku"], quantity=quantity, unit_price_cents=product["price_cents"], line_total_cents=line_total))
        discount = subtotal // 10 if rng.random() < .22 else 0
        shipping = 0 if subtotal-discount >= 10000 else 995
        total = subtotal-discount+shipping
        order = dict(order_id=order_id, customer_id=tables["customers"][int(rng.random()**1.7 * 2000)]["customer_id"], placed_at=placed, status=status, channel=rng.choices(CHANNELS, [28, 22, 18, 12, 14, 6])[0], currency="AUD", subtotal_cents=subtotal, discount_cents=discount, shipping_cents=shipping, tax_cents=round(total/11), total_cents=total)
        tables["orders"].append(order)
        tables["payments"].append(dict(payment_id=f"PAY-{i+1:05}", order_id=order_id, method=rng.choices(["Card", "Digital wallet", "Bank transfer"], [65, 30, 5])[0], status="voided" if status == "cancelled" else "refunded" if status == "refunded" else "captured", amount_cents=0 if status == "cancelled" else total, processed_at=placed+timedelta(minutes=2)))
        if status in ("shipped", "delivered", "refunded"):
            shipped = min(placed+timedelta(days=1), as_of-timedelta(hours=1))
            delivered = min(shipped+timedelta(days=rng.randint(2, 6)), as_of) if status != "shipped" else None
            tables["shipments"].append(dict(shipment_id=f"SHIP-{i+1:05}", order_id=order_id, warehouse=rng.choice(["Brisbane", "Melbourne", "Perth"]), service=rng.choice(["Standard", "Express"]), status="in_transit" if status == "shipped" else "delivered", shipped_at=shipped, delivered_at=delivered))
        if status == "refunded":
            tables["returns"].append(dict(return_id=f"RET-{i+1:05}", order_id=order_id, reason=rng.choice(["Damaged in transit", "Wrong item", "Changed mind", "Quality issue", "Late delivery"]), status="refunded", refund_cents=total, requested_at=min(placed+timedelta(days=rng.randint(8, 20)), as_of)))
    for product in tables["products"]:
        for warehouse in ["Brisbane", "Melbourne", "Perth"]:
            on_hand = rng.randint(0, 180)
            if rng.random() < .14:
                on_hand = rng.randint(0, 12)
            tables["inventory"].append(dict(inventory_id=f"{product['sku']}-{warehouse}", sku=product["sku"], warehouse=warehouse, on_hand=on_hand, reserved=rng.randint(0, min(12, on_hand)), reorder_point=20, updated_at=as_of-timedelta(hours=rng.randint(1, 24))))
    for i, order in enumerate(rng.sample(tables["orders"], 650)):
        opened = min(order["placed_at"]+timedelta(days=2), as_of-timedelta(hours=1))
        tables["support_tickets"].append(dict(ticket_id=f"T-{i+1:05}", order_id=order["order_id"], customer_id=order["customer_id"], category=rng.choice(["Delivery status", "Return request", "Product question", "Payment query"]), priority=rng.choices(["Low", "Normal", "High"], [20, 65, 15])[0], status=rng.choices(["Open", "In progress", "Resolved"], [15, 20, 65])[0], opened_at=opened, satisfaction=rng.choice([None, 2, 3, 4, 5, 5])))
    validate(tables)
    return tables


def validate(tables):
    orders = {o["order_id"]: o for o in tables["orders"]}
    subtotals = Counter()
    for item in tables["order_items"]:
        assert item["line_total_cents"] == item["unit_price_cents"] * item["quantity"]
        subtotals[item["order_id"]] += item["line_total_cents"]
    for order in orders.values():
        assert subtotals[order["order_id"]] == order["subtotal_cents"]
        assert order["total_cents"] == order["subtotal_cents"]-order["discount_cents"]+order["shipping_cents"]
    for refund in tables["returns"]:
        assert refund["refund_cents"] == orders[refund["order_id"]]["total_cents"]
        assert orders[refund["order_id"]]["status"] == "refunded"
    assert all(0 <= row["reserved"] <= row["on_hand"] for row in tables["inventory"])
    assert all(row["email"].endswith("@example.test") for row in tables["customers"])


DDL = """
CREATE TABLE IF NOT EXISTS customers (customer_id text PRIMARY KEY, name text NOT NULL, email text UNIQUE NOT NULL, city text NOT NULL, segment text NOT NULL, created_at timestamptz NOT NULL, marketing_opt_in boolean NOT NULL);
CREATE TABLE IF NOT EXISTS products (sku text PRIMARY KEY, name text NOT NULL, category text NOT NULL, price_cents integer NOT NULL CHECK(price_cents > 0), cost_cents integer NOT NULL, active boolean NOT NULL);
CREATE TABLE IF NOT EXISTS orders (order_id text PRIMARY KEY, customer_id text NOT NULL REFERENCES customers, placed_at timestamptz NOT NULL, status text NOT NULL, channel text NOT NULL, currency text NOT NULL, subtotal_cents integer NOT NULL, discount_cents integer NOT NULL, shipping_cents integer NOT NULL, tax_cents integer NOT NULL, total_cents integer NOT NULL CHECK(total_cents = subtotal_cents-discount_cents+shipping_cents));
CREATE TABLE IF NOT EXISTS order_items (line_id text PRIMARY KEY, order_id text NOT NULL REFERENCES orders, sku text NOT NULL REFERENCES products, quantity integer NOT NULL CHECK(quantity>0), unit_price_cents integer NOT NULL, line_total_cents integer NOT NULL CHECK(line_total_cents=quantity*unit_price_cents));
CREATE TABLE IF NOT EXISTS payments (payment_id text PRIMARY KEY, order_id text UNIQUE NOT NULL REFERENCES orders, method text NOT NULL, status text NOT NULL, amount_cents integer NOT NULL, processed_at timestamptz NOT NULL);
CREATE TABLE IF NOT EXISTS shipments (shipment_id text PRIMARY KEY, order_id text UNIQUE NOT NULL REFERENCES orders, warehouse text NOT NULL, service text NOT NULL, status text NOT NULL, shipped_at timestamptz NOT NULL, delivered_at timestamptz);
CREATE TABLE IF NOT EXISTS returns (return_id text PRIMARY KEY, order_id text UNIQUE NOT NULL REFERENCES orders, reason text NOT NULL, status text NOT NULL, refund_cents integer NOT NULL, requested_at timestamptz NOT NULL);
CREATE TABLE IF NOT EXISTS inventory (inventory_id text PRIMARY KEY, sku text NOT NULL REFERENCES products, warehouse text NOT NULL, on_hand integer NOT NULL, reserved integer NOT NULL CHECK(reserved>=0 AND reserved<=on_hand), reorder_point integer NOT NULL, updated_at timestamptz NOT NULL);
CREATE TABLE IF NOT EXISTS support_tickets (ticket_id text PRIMARY KEY, order_id text NOT NULL REFERENCES orders, customer_id text NOT NULL REFERENCES customers, category text NOT NULL, priority text NOT NULL, status text NOT NULL, opened_at timestamptz NOT NULL, satisfaction integer);
CREATE INDEX IF NOT EXISTS orders_placed_at_idx ON orders(placed_at DESC);
CREATE INDEX IF NOT EXISTS orders_customer_idx ON orders(customer_id);
CREATE INDEX IF NOT EXISTS order_items_order_idx ON order_items(order_id);
CREATE INDEX IF NOT EXISTS order_items_sku_idx ON order_items(sku);
"""
VIEWS = {
    "daily_sales": "SELECT placed_at::date AS day, count(*) AS orders, count(*) FILTER (WHERE status='cancelled') AS cancelled, round(sum(total_cents) FILTER (WHERE status NOT IN ('cancelled','refunded'))/100.0,2) AS net_sales_aud, round(coalesce(sum(total_cents) FILTER (WHERE status='refunded'),0)/100.0,2) AS refunds_aud FROM orders GROUP BY 1 ORDER BY 1",
    "channel_performance": "SELECT channel, count(*) AS orders, round(sum(total_cents) FILTER (WHERE status NOT IN ('cancelled','refunded'))/100.0,2) AS net_sales_aud FROM orders GROUP BY 1 ORDER BY net_sales_aud DESC",
    "category_performance": "SELECT p.category, sum(i.quantity) AS units, round(sum(i.line_total_cents)/100.0,2) AS gross_item_sales_aud FROM order_items i JOIN products p USING(sku) JOIN orders o USING(order_id) WHERE o.status NOT IN ('cancelled','refunded') GROUP BY 1 ORDER BY gross_item_sales_aud DESC",
    "inventory_alerts": "SELECT i.sku,p.name,i.warehouse,i.on_hand-i.reserved AS available,i.reorder_point FROM inventory i JOIN products p USING(sku) WHERE i.on_hand-i.reserved<i.reorder_point ORDER BY available,i.sku",
    "customer_value": "SELECT c.customer_id,c.name,c.segment,c.city,count(o.order_id) AS orders,coalesce(round(sum(o.total_cents) FILTER (WHERE o.status NOT IN ('cancelled','refunded'))/100.0,2),0) AS lifetime_value_aud FROM customers c LEFT JOIN orders o USING(customer_id) GROUP BY c.customer_id ORDER BY lifetime_value_aud DESC",
}


def metadata(tenant, project, as_of, tables):
    common = {"TenantId": tenant, "ProjectId": project}
    code = Path(__file__).with_name("ecommerce_job.py").read_text()
    sample = [{k: o[k] for k in ("order_id", "total_cents", "status", "channel")} for o in tables["orders"][:25]]
    payload = json.dumps({"orders": sample, "demo": True})
    job_ids = {op: identity(f"{tenant}/job/{op}") for op in JOB_NAMES}
    rows = defaultdict(list)
    for operation, name in JOB_NAMES.items():
        rows["jobs"].append(dict(Id=job_ids[operation], **common, Name=name, Description="Ecommerce demo: offline sample batch. No payments, messages, or external store changes.", MapSourceKind="code", MapRuntimeId="python", MapSource=code, MapFilesJson=[{"Path": "main.py", "Content": code}], MapEntrypoint="main.py", InputPayloadsJson=[payload], MapEnvJson={"DEMO_OPERATION": operation}, SuccessCodesJson=[0], PartialCodesJson=[], ConcurrencyLimit=2, ParametersJson=[], PostJobActionsJson=[], ReturnType="Json", AllowNetworkEgress=False, AllowApiInvocation=False, TimeoutSeconds=60, RetryCount=0, RetryDelaySeconds=0, McpConnectionIdsJson=[], CreatedAt=as_of-timedelta(days=180), UpdatedAt=as_of))
    for flow_index, (name, stages) in enumerate(FLOWS):
        chain_id = identity(f"{tenant}/workflow/{name}")
        rows["job_chains"].append(dict(Id=chain_id, **common, Name=name, Description="Harbour & Pine ecommerce demo · simulated history; Run processes a 25-order offline sample. Inspect inputs and outputs at each stage.", StagesJson=[[job_ids[op] for op in stage] for stage in stages], CreatedAt=as_of-timedelta(days=180-flow_index), UpdatedAt=as_of))
        for n in range(60):
            # The latest 12 per workflow are in the last 24 hours; remaining history spans six months.
            started = as_of-timedelta(minutes=15+n*85+flow_index*7) if n < 12 else as_of-timedelta(days=(n-11)*3, minutes=flow_index*17)
            outcome = "Failed" if n%13 == 2 else "Cancelled" if n%19 == 3 else "Succeeded"
            run_id = identity(f"{tenant}/workflow/{name}/run/{n}")
            steps = []
            flat = 0
            for stage_index, stage in enumerate(stages):
                for branch, operation in enumerate(stage):
                    status = outcome if stage_index == 1 and outcome != "Succeeded" else "Succeeded"
                    if stage_index > 1 and outcome != "Succeeded":
                        status = "Skipped"
                    step_start = started+timedelta(seconds=stage_index*20)
                    step_end = step_start+timedelta(seconds=8+branch*3+n%9)
                    job_run = identity(f"{run_id}/step/{flat}") if status != "Skipped" else None
                    error = "Simulated source batch failed the duplicate-order data contract" if status == "Failed" else None
                    steps.append(dict(index=flat,stageIndex=stage_index,branchIndex=branch,jobId=job_ids[operation],jobName=JOB_NAMES[operation],runId=job_run,status=status,startedAt=step_start.isoformat() if job_run else None,finishedAt=step_end.isoformat() if job_run else None,error=error))
                    if job_run:
                        artifact = json.dumps({"demo": True, "simulated": True, "operation": operation, "records": 25, "currency": "AUD", "message": error or "Historical ecommerce sample outcome"})
                        snapshot = {"mapSource": {"kind": "code", "runtimeId": "python", "source": code, "entrypoint": "main.py"}, "inputPayloads": [payload], "mapEnv": {"DEMO_OPERATION": operation}, "concurrencyLimit": 2, "allowNetworkEgress": False}
                        shard_outcome = "Failed" if status in ("Failed", "Cancelled") else "Succeeded"
                        shards = [{"index": 0,"exitCode": 1 if shard_outcome == "Failed" else 0,"outcome": shard_outcome,"artifact": artifact,"log": f"[SIMULATED HISTORY] {JOB_NAMES[operation]}\nRead 25 sample orders\n{error or status}\nNo external systems were contacted.","artifacts": [{"name": "sample-result.json", "content": artifact}]}]
                        rows["job_runs"].append(dict(Id=job_run, **common, JobId=job_ids[operation], Status=status, StartedAt=step_start, FinishedAt=step_end, ShardResultsJson=shards, SnapshotJson=snapshot, FailedShards=int(shard_outcome=="Failed"), PartialShards=0, ShardCount=1, SucceededShards=int(shard_outcome=="Succeeded"), AttemptNumber=1))
                    flat += 1
            finished = max(datetime.fromisoformat(s["finishedAt"]) for s in steps if s["finishedAt"])
            rows["chain_runs"].append(dict(Id=run_id, **common, ChainId=chain_id, ChainName=name, Status=outcome, StepsJson=steps, FinalOutput=json.dumps({"demo": True,"simulated": True,"status": outcome,"records": 25}), StartedAt=started, FinishedAt=finished))
    queries = {
        "Daily net sales and refunds": "SELECT * FROM daily_sales ORDER BY day DESC LIMIT 90",
        "Orders needing fulfilment": "SELECT * FROM orders WHERE status IN ('paid','processing') ORDER BY placed_at DESC LIMIT 100",
        "Inventory below reorder point": "SELECT * FROM inventory_alerts LIMIT 100",
        "Top customers by lifetime value": "SELECT * FROM customer_value LIMIT 50",
        "Sales by channel": "SELECT * FROM channel_performance",
        "Sales by category": "SELECT * FROM category_performance",
        "Return reasons": "SELECT reason, count(*) AS returns, round(sum(refund_cents)/100.0,2) AS refunds_aud FROM returns GROUP BY reason ORDER BY returns DESC",
        "Open customer support": "SELECT t.*, c.email FROM support_tickets t JOIN customers c USING(customer_id) WHERE t.status <> 'Resolved' ORDER BY t.opened_at DESC LIMIT 100",
    }
    for name, sql in queries.items():
        rows["saved_queries"].append(dict(Id=identity(f"{tenant}/query/{name}"), **common, Name=name, Sql=sql, CreatedAt=as_of, UpdatedAt=as_of))
    charts = []
    weeks = defaultdict(int)
    for order in tables["orders"]:
        if order["status"] not in ("cancelled", "refunded"):
            weeks[order["placed_at"].strftime("%G-W%V")] += order["total_cents"]
    recent = sorted(weeks)[-24:]
    charts.append(("daily_sales", "line", "Weekly net sales · AUD", recent, [weeks[w]/100 for w in recent]))
    for table, field, title in [("orders", "status", "Orders by status"), ("returns", "reason", "Return reasons"), ("support_tickets", "status", "Customer support queue")]:
        counts = Counter(row[field] for row in tables[table])
        charts.append((table, "bar", title, list(counts), list(counts.values())))
    channels = {channel: sum(o["total_cents"] for o in tables["orders"] if o["channel"] == channel and o["status"] not in ("cancelled", "refunded"))/100 for channel in CHANNELS}
    charts.append(("channel_performance", "bar", "Net sales by channel · AUD", list(channels), list(channels.values())))
    low = Counter(r["warehouse"] for r in tables["inventory"] if r["on_hand"]-r["reserved"] < r["reorder_point"])
    charts.append(("inventory_alerts", "bar", "Low-stock locations", list(low), list(low.values())))
    for table, kind, title, labels, values in charts:
        spec = {"sql": CHART_QUERIES[table], "type": kind, "title": title, "labels": labels, "series": [{"name": "AUD" if "AUD" in title else "Count", "values": values}]}
        rows["project_charts"].append(dict(Id=identity(f"{tenant}/chart/{table}"), **common, TableName="sql:"+title, Html=spec, GeneratedAt=as_of))
    return rows


def psql(container, sql):
    result = subprocess.run(["docker", "exec", "-i", container, "psql", "-X", "-U", "postgres", "-d", "placecontext", "-v", "ON_ERROR_STOP=1", "-Atq"], input=sql, text=True, capture_output=True, check=True)
    return result.stdout.strip()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply", action="store_true", help="Insert into the local validation database; existing demo rows are preserved")
    args = parser.parse_args()
    container = "devcontext-validation-postgres"
    as_of = datetime.now(timezone.utc).replace(second=0, microsecond=0)
    tenant = "00000000-0000-0000-0000-000000000000"
    if args.apply:
        tenant = psql(container, "SELECT \"Id\" FROM public.tenants WHERE \"Slug\"='default';")
        uuid.UUID(tenant)
    project = identity(f"{tenant}/project")
    schema = "proj_"+project.replace("-", "")
    if args.apply:
        existing = psql(container, f'SELECT "RegisteredAt" FROM public.projects WHERE "Id"={literal(project)};')
        if existing:
            as_of = datetime.fromisoformat(existing)
    tables = generate(as_of)
    rows = metadata(tenant, project, as_of, tables)
    summary = {"project": PROJECT_NAME, "project_id": project, "as_of": as_of.isoformat(), "records": {name: len(data) for name, data in tables.items()}, "platform": {name: len(data) for name, data in rows.items()}}
    print(json.dumps(summary, indent=2))
    if not args.apply:
        print("Validated sample data. Add --apply to populate the local development database.")
        return
    project_row = dict(Id=project, TenantId=tenant, Name=PROJECT_NAME, Path=PROJECT_PATH, Status="Registered", DiscoveredAt=as_of-timedelta(days=180), RegisteredAt=as_of)
    sql = ["BEGIN;", "SELECT pg_advisory_xact_lock(hashtext('harbour-and-pine-demo'));", insert("public.projects", [project_row]), f'CREATE SCHEMA IF NOT EXISTS "{schema}";', f'DO $$ BEGIN CREATE ROLE "{schema}" NOLOGIN; EXCEPTION WHEN duplicate_object THEN NULL; END $$;', f'GRANT USAGE, CREATE ON SCHEMA "{schema}" TO "{schema}";', f'SET LOCAL ROLE "{schema}"; SET LOCAL search_path TO "{schema}";', DDL]
    sql.extend(insert(name, data) for name, data in tables.items())
    # Never replace existing views: a customer may have edited their demo since the first seed.
    for name, query in VIEWS.items():
        sql.append(f"DO $demo$ BEGIN IF to_regclass('{schema}.{name}') IS NULL THEN EXECUTE {literal('CREATE VIEW '+name+' AS '+query)}; END IF; END $demo$;")
    sql.append("RESET ROLE;")
    sql.extend(insert("public."+name, data) for name, data in rows.items())
    sql.append("COMMIT;")
    psql(container, "\n".join(sql))
    counts = psql(container, " UNION ALL ".join(f"SELECT '{name}', count(*) FROM {schema}.{name}" for name in tables)+";")
    print(counts)
    print(f"Open http://localhost:7700/project/{project}/dashboard")


if __name__ == "__main__":
    main()
