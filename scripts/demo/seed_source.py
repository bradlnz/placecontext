#!/usr/bin/env python3
"""Seed the separate local source database with a bounded ecommerce fixture."""
from datetime import datetime, timezone
import subprocess

from seed_ecommerce import DDL, generate, insert, validate


def source_tables():
    tables = generate(datetime(2026, 11, 4, tzinfo=timezone.utc))
    orders = {row["order_id"] for row in tables["orders"][:200]}
    for name, rows in tables.items():
        if rows and "order_id" in rows[0]:
            tables[name] = [row for row in rows if row["order_id"] in orders]
    customers = {row["customer_id"] for row in tables["orders"]}
    tables["customers"] = [row for row in tables["customers"] if row["customer_id"] in customers]
    validate(tables)
    assert all(0 < len(rows) <= 1000 for rows in tables.values())
    return tables


if __name__ == "__main__":
    tables = source_tables()
    sql = "\n".join([
        "BEGIN;",
        "SELECT pg_advisory_xact_lock(hashtext('source-demo-seed'));",
        DDL,
        *(insert(name, rows) for name, rows in tables.items()),
        "GRANT USAGE ON SCHEMA public TO source_reader;",
        "GRANT SELECT ON ALL TABLES IN SCHEMA public TO source_reader;",
        "ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO source_reader;",
        "COMMIT;",
    ])
    subprocess.run([
        "docker", "exec", "-i", "placecontext-source-demo", "psql",
        "--username", "postgres", "--dbname", "source_demo", "--no-psqlrc",
        "--set", "ON_ERROR_STOP=1", "--quiet",
    ], input=sql, text=True, check=True)
    for name, rows in tables.items():
        print(f"{name}: {len(rows)} fixture rows (existing rows preserved)")
