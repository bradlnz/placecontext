"""Loopback-only fictional website and service desk for the recorded demo."""
import html
import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import sys
import urllib.error
import urllib.parse
import urllib.request
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from seed_ecommerce import insert, literal, psql
from job import run

OUT = Path(__file__).resolve().parents[3] / "dist/demos/enquiry-routing"
CONFIG = json.loads((OUT / "project.json").read_text())
DB = "devcontext-validation-postgres"
SCHEMA = CONFIG["schema"]
assert SCHEMA == "proj_" + uuid.UUID(CONFIG["project"]).hex
e = html.escape


def shell(content, section="Customer care"):
    return '''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Northstar | Customer care</title>
<style>*{box-sizing:border-box}body{margin:0;background:#f4f6fa;color:#17243b;font:18px/1.5 system-ui,sans-serif}header{height:90px;background:#fff;border-bottom:1px solid #dfe5ee;display:flex;align-items:center;justify-content:space-between;padding:0 7%}.brand{font-size:25px;font-weight:800;letter-spacing:2px}.brand b{color:#4566e8;margin-right:10px}.badge{font-size:13px;color:#4e607c;border:1px solid #d9e0ed;border-radius:30px;padding:6px 13px}main{max-width:1120px;margin:48px auto;padding:0 28px}.eyebrow{font-size:13px;font-weight:700;letter-spacing:2px;color:#526b9d;text-transform:uppercase}h1{font-size:42px;letter-spacing:-1.5px;line-height:1.15;margin:12px 0 20px}h2{font-size:24px;margin:0 0 14px}p{color:#526078}article{background:#fff;border:1px solid #e0e6f0;border-radius:18px;padding:28px;margin-top:22px}.grid{display:grid;grid-template-columns:1.1fr 1fr;gap:54px}.field{margin:17px 0}label{font-size:15px;font-weight:650;display:block;margin:0 0 7px}input,textarea{width:100%;font:inherit;padding:12px;border:1px solid #bcc9dc;border-radius:8px;background:#fcfdff}textarea{height:140px;resize:vertical}button{background:#395de1;border:0;color:#fff;border-radius:8px;font:650 16px system-ui;padding:14px 22px;cursor:pointer}button:hover{background:#2748c1}a{color:#395de1}small{font-size:13px;color:#61718a}.line{border-top:1px solid #e4e9f1;margin:22px 0}.pill{display:inline-block;background:#e5f7ee;color:#146743;padding:7px 15px;border-radius:25px;font-size:14px;font-weight:700}.people{display:flex;gap:14px;margin:22px 0}.person{flex:1;cursor:pointer;border:2px solid #e1e7f0;border-radius:12px;padding:16px}.person:has(input:checked){border-color:#4566e8;background:#f1f4ff}.person input{width:auto;margin-right:8px}.person small{display:block;margin-top:6px}.question{font-size:23px;line-height:1.45;color:#17243b}footer{margin-top:32px;font-size:13px;color:#718099}.notice{background:#e8efff;border-radius:10px;padding:16px}.id{font-family:monospace;font-size:12px;color:#708099}@media(max-width:750px){.grid{display:block}main{margin-top:25px}h1{font-size:32px}}</style>
<header><div class="brand"><b>✦</b>NORTHSTAR</div><span>''' + e(section) + '''</span><span class="badge">Fictional demo</span></header><main>''' + content + '''<footer>Demonstration environment · Fictional people and enquiries · No email is sent</footer></main></html>'''


def rows():
    value = psql(DB, f'SELECT coalesce(json_agg(t),\'[]\'::json) FROM (SELECT * FROM "{SCHEMA}".enquiries ORDER BY created_at DESC LIMIT 50) t;')
    return json.loads(value)


class Handler(BaseHTTPRequestHandler):
    def send(self, body, status=200):
        body = body.encode()
        self.send_response(status)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Content-Type-Options", "nosniff")
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path == "/":
            return self.send(shell('''<div class="grid"><section><div class="eyebrow">A helpful answer starts here</div><h1>Ask our team.</h1><p>Tell us what you need. We’ll get your question to the person who can help.</p><article><h2>One question. The right person.</h2><p>Billing & payments<br>Product & technical support<br>Plans & pricing</p><div class="line"></div><small>Your enquiry is routed by PlaceContext and reviewed before assignment.</small></article></section><article style="margin:0"><form method="post" action="/enquiries"><h2>How can we help?</h2><div class="field"><label for="name">Your name</label><input id="name" name="name" required maxlength="80"></div><div class="field"><label for="email">Email address</label><input id="email" name="email" type="email" required placeholder="you@example.test"></div><div class="field"><label for="question">Your question</label><textarea id="question" name="question" minlength="10" maxlength="2000" required></textarea></div><button type="submit">Send question →</button></form></article></div>'''))
        if self.path == "/desk":
            enquiries = rows()
            cards = ''.join(f'<article><small>{e(r["name"])} · {e(r["status"])}</small><p class="question">{e(r["question"])}</p><a href="/desk/{r["request_id"]}">Open enquiry</a><p class="id">{r["request_id"]}</p></article>' for r in enquiries)
            return self.send(shell('<div class="eyebrow">Service desk</div><h1>Incoming enquiries</h1>'+ (cards or '<article>No enquiries yet.</article>'), "Service desk"))
        if self.path.startswith("/desk/"):
            request_id = self.path.split("/")[-1]
            item = next((r for r in rows() if r["request_id"] == request_id), None)
            if not item:
                return self.send(shell("<h1>Enquiry not found</h1>"), 404)
            from seed import ROSTER
            choices = ''.join(f'<label class="person"><input type="radio" name="person" value="{e(p["person"])}" required {"checked" if p["person"] == item["assigned_to"] else ""}>{e(p["person"])}<small>{e(p["department"])}</small></label>' for p in ROSTER)
            assigned = f'<div class="notice"><strong>Assigned to {e(item["assigned_to"])}</strong> · Saved in the service desk</div>' if item["assigned_to"] else ''
            return self.send(shell(f'<div class="eyebrow">Service desk / enquiry</div><h1>The right person to help.</h1><span class="pill">{e(item["status"])}</span><article><small>{e(item["name"])} · {e(item["email"])}</small><p class="question">{e(item["question"])}</p><p class="id">{request_id}</p>{assigned}<div class="line"></div><form method="post" action="/assign/{request_id}"><h2>Assign this enquiry</h2><div class="people">{choices}</div><button>Save assignment</button></form></article>', "Service desk"))
        self.send(shell("<h1>Page not found</h1>"), 404)

    def do_POST(self):
        try:
            if self.headers.get("Origin") not in (None, "http://localhost:7788", "http://127.0.0.1:7788", "http://10.0.2.2:7788"):
                raise ValueError("Unexpected request origin")
            length = int(self.headers.get("Content-Length", "0"))
            if not 0 < length <= 10000:
                raise ValueError("Invalid request size")
            fields = urllib.parse.parse_qs(self.rfile.read(length).decode(), strict_parsing=True)
            if self.path == "/enquiries":
                payload = dict(request_id=str(uuid.uuid4()), demo=True, **{k:fields[k][0].strip() for k in ("name", "email", "question")})
                if not 1 <= len(payload["name"]) <= 80:
                    raise ValueError("Name required")
                run(payload, "validate")
                psql(DB, insert(f'"{SCHEMA}".enquiries', [{k:v for k,v in payload.items() if k != "demo"}]))
                token = (OUT / "queue-token.private").read_text().strip()
                req = urllib.request.Request(f'http://localhost:7700/api/v1/queues/{CONFIG["queue"]}/messages', data=json.dumps(payload).encode(), headers={"Content-Type":"application/json", "Authorization":"Bearer "+token, "Idempotency-Key":payload["request_id"]})
                with urllib.request.urlopen(req, timeout=20) as result:
                    receipt = json.load(result)
                (OUT / "latest-submission.json").write_text(json.dumps({"payload":payload,"receipt":receipt}, indent=2))
                return self.send(shell(f'<div class="eyebrow">Question received</div><h1>You’re in the right place.</h1><article><span class="pill">Accepted for routing</span><p class="question">{e(payload["question"])}</p><p>We’re finding the right person for your question.</p><p class="id">Reference: {payload["request_id"]}</p></article>'))
            if self.path.startswith("/assign/"):
                request_id = str(uuid.UUID(self.path.split("/")[-1]))
                from seed import ROSTER
                person = fields["person"][0]
                if person not in [p["person"] for p in ROSTER]:
                    raise ValueError("Unknown assignee")
                changed = psql(DB, f'UPDATE "{SCHEMA}".enquiries SET assigned_to={literal(person)}, status=\'Assigned\' WHERE request_id={literal(request_id)} RETURNING request_id;')
                if not changed:
                    raise ValueError("Unknown enquiry")
                self.send_response(303)
                self.send_header("Location", "/desk/"+request_id)
                return self.end_headers()
            self.send(shell("<h1>Page not found</h1>"), 404)
        except (ValueError, KeyError, urllib.error.URLError) as error:
            self.send(shell('<h1>Could not complete the request</h1><p>'+e(str(error))+"</p>"), 400)


if __name__ == "__main__":
    print("Fictional demo website: http://localhost:7788", flush=True)
    ThreadingHTTPServer(("127.0.0.1", 7788), Handler).serve_forever()
