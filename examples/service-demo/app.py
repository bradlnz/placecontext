"""A dependency-free Services demo. Bind a Redis store when launching it."""
import json
import os
import socket
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import unquote, urlsplit


def visits():
    redis = urlsplit(os.environ["REDIS_URL"])
    with socket.create_connection((redis.hostname, redis.port or 6379), timeout=3) as sock:
        with sock.makefile("rwb") as stream:
            # This demo only needs Redis AUTH and INCR, both scalar RESP replies.
            for args in (("AUTH", unquote(redis.username or "default"), unquote(redis.password or "")),
                         ("INCR", "service-demo:visits")):
                encoded = [arg.encode() for arg in args]
                stream.write(f"*{len(encoded)}\r\n".encode())
                for value in encoded:
                    stream.write(f"${len(value)}\r\n".encode() + value + b"\r\n")
                stream.flush()
                reply = stream.readline(1024)
                if not reply.startswith((b"+", b":")):
                    raise RuntimeError("Redis did not accept the command")
            return int(reply[1:])


class App(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == "/health":
            self.respond(200, b"ok", "text/plain")
            return
        if self.path not in ("/", "/api/visits"):
            self.respond(404, b"Not found", "text/plain")
            return
        try:
            count = visits()
        except (KeyError, OSError, RuntimeError, ValueError):
            print(json.dumps({"level": "ERROR", "message": "Redis unavailable; check the service database binding"}))
            self.respond(503, b"Redis is not ready. Bind a Redis store and retry.", "text/plain")
            return
        print(json.dumps({"level": "INFO", "message": "Page visited", "visits": count}))
        if self.path == "/api/visits":
            self.respond(200, json.dumps({"visits": count}).encode(), "application/json")
            return
        page = f"""<!doctype html><html lang="en"><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1"><title>Services demo</title>
<style>body{{margin:0;background:#101821;color:#ecf2f7;font:18px system-ui;display:grid;min-height:100vh;place-items:center}}
main{{padding:36px;max-width:600px}}small{{color:#65d6af}}h1{{font-size:46px;line-height:1.1}}p{{line-height:1.6;color:#a9bbc9}}
strong{{font-size:64px;color:#65d6af}}a{{display:inline-block;padding:12px 20px;background:#65d6af;color:#101821;text-decoration:none;border-radius:8px}}</style>
<main><small>PLACECONTEXT / SERVICES</small><h1>Your app is running.</h1>
<p>A long-running web service with a private Redis database. The visit count remains when the app restarts.</p>
<strong>{count}</strong><p>Visits stored in Redis</p><a href="/">Visit again</a>
<p>Open Services → Logs to see each request as a structured event.</p></main></html>"""
        self.respond(200, page.encode(), "text/html; charset=utf-8")

    def respond(self, status, body, content_type):
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *_):
        pass


if __name__ == "__main__":
    ThreadingHTTPServer(("0.0.0.0", 8080), App).serve_forever()
