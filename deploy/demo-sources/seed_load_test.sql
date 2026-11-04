BEGIN;
SELECT pg_advisory_xact_lock(hashtext('source-demo-load-test-v1'));
CREATE TABLE IF NOT EXISTS orders_load_test (
    order_id text PRIMARY KEY,
    status text NOT NULL,
    currency text NOT NULL,
    total_cents integer NOT NULL CHECK (total_cents >= 0),
    placed_at timestamptz NOT NULL
);
INSERT INTO orders_load_test
SELECT 'LOAD-' || lpad(n::text, 6, '0'),
       CASE WHEN n % 100 < 65 THEN 'delivered'
            WHEN n % 100 < 75 THEN 'processing'
            WHEN n % 100 < 85 THEN 'shipped'
            WHEN n % 100 < 90 THEN 'paid'
            WHEN n % 100 < 95 THEN 'cancelled' ELSE 'refunded' END,
       'AUD', 100 + (n::bigint * 7919 % 99900)::integer,
       timestamptz '2026-11-04 00:00:00+00' - (n % 365) * interval '1 day'
           + (n % 86400) * interval '1 second'
FROM generate_series(1, 100000) AS n
ON CONFLICT (order_id) DO NOTHING;
GRANT SELECT ON orders_load_test TO source_reader;
DO $$ BEGIN
    IF (SELECT count(*) FROM orders_load_test) <> 100000
       OR EXISTS (SELECT 1 FROM orders_load_test WHERE order_id !~ '^LOAD-[0-9]{6}$'
                  OR currency <> 'AUD' OR total_cents < 0
                  OR status NOT IN ('paid','processing','shipped','delivered','cancelled','refunded')) THEN
        RAISE EXCEPTION 'Invalid 100k order fixture';
    END IF;
END $$;
COMMIT;
ANALYZE orders_load_test;
SELECT status, count(*) AS records FROM orders_load_test GROUP BY status ORDER BY status;
