#!/usr/bin/env python3
"""Start and seed local demo databases and brokers without replacing existing data."""
import base64
import json
import os
from pathlib import Path
import secrets
import subprocess
import sys
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[2]
STATE = ROOT / "dist/demo-source"
sys.path.insert(0, str(ROOT / "scripts/demo"))
from seed_ecommerce import literal
from seed_source import source_tables


def run(args, **kwargs):
    return subprocess.run(args, text=True, check=True, **kwargs)


def main():
    STATE.mkdir(parents=True, exist_ok=True, mode=0o700)
    env_file = STATE / ".env"
    if not env_file.exists():
        env_file.write_text("".join(f"{key}=Demo!{secrets.token_hex(20)}\n" for key in
                                  ["DEMO_ADMIN_PASSWORD", "DEMO_READER_PASSWORD", "DEMO_BROKER_PASSWORD"]))
        env_file.chmod(0o600)
    credentials = dict(line.split("=", 1) for line in env_file.read_text().splitlines())
    object_env = STATE / "object-store.env"
    if not object_env.exists():
        with os.fdopen(os.open(object_env, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600), "w") as output:
            output.write(f"AWS_ACCESS_KEY_ID=pc-local-{secrets.token_hex(8)}\n"
                         f"AWS_SECRET_ACCESS_KEY={secrets.token_hex(32)}\n"
                         "S3_BUCKET=placecontext-reports,placecontext-deps\n")
    reader = credentials["DEMO_READER_PASSWORD"]
    broker = credentials["DEMO_BROKER_PASSWORD"]
    compose = ["docker", "compose", "--env-file", str(env_file), "-f", str(Path(__file__).with_name("compose.yaml"))]

    def execute(service, args, sql=None, user=None):
        return run(compose + ["exec", "-T"] + (["--user", user] if user else []) + [service] + args,
                   input=sql, capture_output=True).stdout

    def postgres(sql):
        return execute("postgres", ["psql", "-U", "postgres", "-d", "source_demo", "-v", "ON_ERROR_STOP=1", "-At"], sql)

    def mysql(sql):
        return execute("mysql", ["sh", "-c", 'MYSQL_PWD="$MYSQL_ROOT_PASSWORD" exec mysql -u root --batch --skip-column-names source_demo'], sql)

    def mssql(sql):
        return execute("mssql", ["sh", "-c", 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" exec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -l 10'], sql)

    cert, key = STATE / "server.crt", STATE / "server.key"
    if not cert.exists():
        run(["openssl", "req", "-x509", "-newkey", "rsa:2048", "-noenc", "-days", "365",
             "-subj", "/CN=PlaceContext Local Demo Source", "-addext", "subjectAltName=DNS:localhost,IP:127.0.0.1",
             "-addext", "extendedKeyUsage=serverAuth", "-keyout", str(key), "-out", str(cert)], capture_output=True)
    key.chmod(0o600)
    system_ca = Path(os.environ.get("SSL_CERT_FILE", "/etc/ssl/certs/ca-certificates.crt"))
    (STATE / "ca-bundle.crt").write_text(system_ca.read_text() + "\n" + cert.read_text())

    run(compose + ["up", "-d", "--wait", "--wait-timeout", "300"])
    for service, owner, directory in [("postgres", "postgres", "/var/lib/postgresql/data"),
                                      ("mysql", "mysql", "/var/lib/mysql"),
                                      ("mssql", "mssql", "/var/opt/mssql")]:
        for source, target in [(cert, "source-demo.crt"), (key, "source-demo.key")]:
            run(compose + ["cp", str(source), f"{service}:{directory}/{target}"], capture_output=True)
            execute(service, ["chown", owner, f"{directory}/{target}"], user="root")
        execute(service, ["chmod", "600", f"{directory}/source-demo.key"], user="root")
    postgres("ALTER SYSTEM SET ssl_cert_file='source-demo.crt';\nALTER SYSTEM SET ssl_key_file='source-demo.key';\nALTER SYSTEM SET ssl=on;\nSELECT pg_reload_conf();")
    execute("mysql", ["sh", "-c", "printf '[mysqld]\\nssl-cert=/var/lib/mysql/source-demo.crt\\nssl-key=/var/lib/mysql/source-demo.key\\n' > /etc/mysql/conf.d/source-tls.cnf"], user="root")
    for setting, value in [("network.tlscert", "/var/opt/mssql/source-demo.crt"),
                           ("network.tlskey", "/var/opt/mssql/source-demo.key"), ("network.forceencryption", "1")]:
        execute("mssql", ["/opt/mssql/bin/mssql-conf", "set", setting, value], user="root")
    run(compose + ["restart", "mysql", "mssql"])
    run(compose + ["up", "-d", "--wait", "--wait-timeout", "300"])

    postgres(f"DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname='source_reader') THEN CREATE ROLE source_reader LOGIN PASSWORD {literal(reader)}; END IF; END $$;\nREVOKE ALL ON DATABASE source_demo FROM PUBLIC; GRANT CONNECT ON DATABASE source_demo TO source_reader; REVOKE CREATE ON SCHEMA public FROM PUBLIC;")
    run([sys.executable, str(ROOT / "scripts/demo/seed_source.py")])
    tables = source_tables()
    customers = {row["customer_id"]: row for row in tables["customers"]}
    orders = [dict(order_id=row["order_id"], customer_name=customers[row["customer_id"]]["name"],
                   city=customers[row["customer_id"]]["city"], status=row["status"], channel=row["channel"],
                   currency=row["currency"], total_cents=row["total_cents"], placed_at=row["placed_at"].replace(tzinfo=None).isoformat(" "))
              for row in tables["orders"]]
    columns = ",".join(orders[0])
    ddl = "(order_id varchar(30) PRIMARY KEY, customer_name varchar(100) NOT NULL, city varchar(100) NOT NULL, status varchar(30) NOT NULL, channel varchar(50) NOT NULL, currency varchar(3) NOT NULL, total_cents int NOT NULL, placed_at datetime NOT NULL)"
    values = ["(" + ",".join(literal(value) for value in row.values()) + ")" for row in orders]
    mysql(f"CREATE TABLE IF NOT EXISTS orders {ddl};\nSTART TRANSACTION;\nINSERT INTO orders ({columns}) VALUES " + ",\n".join(values) + f" ON DUPLICATE KEY UPDATE order_id=orders.order_id;\nCOMMIT;\nCREATE USER IF NOT EXISTS 'source_reader'@'%' IDENTIFIED BY {literal(reader)} REQUIRE SSL;\nGRANT SELECT ON source_demo.* TO 'source_reader'@'%';")
    mssql("IF DB_ID('source_demo') IS NULL CREATE DATABASE source_demo;\nGO\nUSE source_demo;\nGO\nSET XACT_ABORT ON;\nBEGIN TRANSACTION;\nIF OBJECT_ID('dbo.orders','U') IS NULL CREATE TABLE dbo.orders " + ddl.replace("datetime", "datetime2") + ";\n" + "\n".join(
        f"IF NOT EXISTS (SELECT 1 FROM dbo.orders WHERE order_id={literal(row['order_id'])}) INSERT INTO dbo.orders ({columns}) VALUES {value};" for row, value in zip(orders, values)) +
        f"\nIF SUSER_ID('source_reader') IS NULL CREATE LOGIN source_reader WITH PASSWORD={literal(reader)}, CHECK_POLICY=ON;\nIF USER_ID('source_reader') IS NULL CREATE USER source_reader FOR LOGIN source_reader;\nGRANT SELECT ON SCHEMA::dbo TO source_reader;\nCOMMIT;\nGO\n")

    # shortcut: existing queues/streams are preserved, explicitly remove a demo queue/stream to reseed it.
    auth = base64.b64encode(f"source_demo:{broker}".encode()).decode()
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))

    def rabbit(path, method="GET", body=None):
        request = urllib.request.Request("http://127.0.0.1:15673/api/" + path,
                                         data=json.dumps(body).encode() if body is not None else None, method=method,
                                         headers={"Authorization": "Basic " + auth, "Content-Type": "application/json"})
        with opener.open(request, timeout=15) as response:
            data = response.read()
            return json.loads(data) if data else None

    try:
        rabbit("queues/%2F/demo.orders")
    except urllib.error.HTTPError as error:
        if error.code != 404:
            raise
        rabbit("queues/%2F/demo.orders", "PUT", {"durable": True, "auto_delete": False, "arguments": {}})
        for row in orders[:25]:
            result = rabbit("exchanges/%2F/amq.default/publish", "POST", {
                "properties": {"delivery_mode": 2, "message_id": row["order_id"], "content_type": "application/json"},
                "routing_key": "demo.orders", "payload": json.dumps(row), "payload_encoding": "string"})
            assert result["routed"], "Demo event was not routed to the queue"
    if execute("redis", ["redis-cli", "EXISTS", "demo:orders"]).strip() == "0":
        for index, row in enumerate(orders[:25], 1):
            result = execute("redis", ["redis-cli", "XADD", "demo:orders", f"{index}-0", "event", json.dumps(row)])
            assert result.strip() == f"{index}-0", result

    assert int(postgres("SELECT count(*) FROM orders;").strip()) >= 200
    assert int(mysql("SELECT count(*) FROM orders;").strip()) >= 200
    mssql("USE source_demo; IF (SELECT COUNT(*) FROM dbo.orders) < 200 THROW 50000, 'Missing fixture orders', 1;\nGO\n")
    connections = {"host": "localhost", "database": "source_demo", "username": "source_reader", "password": reader,
                   "postgres": {"port": 5434, "schema": "public"}, "mysql": {"port": 3307, "schema": "source_demo"},
                   "mssql": {"port": 1434, "schema": "dbo"},
                   "rabbitmq": {"port": 5673, "management": "http://localhost:15673", "username": "source_demo", "password": broker, "queue": "demo.orders"},
                   "redis": {"port": 6380, "password": broker, "stream": "demo:orders"}}
    output = STATE / "connections.json"
    output.write_text(json.dumps(connections, indent=2) + "\n")
    output.chmod(0o600)
    print(f"Demo sources ready. Connection details: {output}")


if __name__ == "__main__":
    main()
