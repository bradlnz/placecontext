# Ecommerce demo

Seeds a separate **Harbour & Pine · Ecommerce demo** project in the default workspace of the local `devcontext-validation-postgres` development container. The app's database migrations must already have run.

```sh
python3 -m unittest discover -s scripts/demo -p 'test_*.py'
python3 scripts/demo/seed_ecommerce.py         # validate and preview counts
python3 scripts/demo/seed_ecommerce.py --apply # populate the local database
```

Includes 2,000 customers, 180 products, 8,000 orders, about 24,000 order lines, payments, shipments, returns, inventory, and support tickets across six months. Five reporting views, eight saved queries, and six dashboard charts help explore the data. Prices are in AUD; monetary columns use integer cents, and order totals include GST.

Six workflows use eight editable Python jobs with 360 simulated workflow runs and linked job logs/results. Historical outputs are explicitly marked simulated. New runs process an embedded 25-order sample without external connections; they do not update the business tables, charge cards, issue refunds, or send messages. Run artifacts are attached to job results, not uploaded to the global artifact store. Charts retain SQL definitions and refresh from the underlying tables in Analytics and the dashboard.

The script uses deterministic IDs and a transaction. Reruns preserve existing rows and views, including edits, and reuse the original project's timestamp. It creates an isolated project schema and database role; it never clears existing projects or data. Fictional customer emails use `example.test`.

Open the dashboard URL printed after seeding, or select **Harbour & Pine · Ecommerce demo** in the project menu. Use **Charts** for reporting, **Runs** for recent activity, **Data** for SQL and saved queries, and **Workflows** for the canvas and run history.
