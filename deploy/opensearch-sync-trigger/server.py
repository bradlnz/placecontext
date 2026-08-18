#!/usr/bin/env python3
"""Authenticated private-network trigger for an operator-managed OpenSearch ingestion unit."""

from __future__ import annotations

import hmac
import json
import os
import subprocess
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

TOKEN = os.environ["SYNC_TRIGGER_TOKEN"]
UNIT = os.environ.get("SYNC_TRIGGER_UNIT", "opensearch-ingest.service")
BIND = os.environ.get("SYNC_TRIGGER_BIND", "127.0.0.1")
PORT = int(os.environ.get("SYNC_TRIGGER_PORT", "9340"))


def unit_is_active() -> bool:
    result = subprocess.run(
        ["systemctl", "is-active", "--quiet", UNIT],
        check=False,
        timeout=10,
    )
    return result.returncode == 0


def start_unit() -> None:
    subprocess.run(
        ["systemctl", "start", "--no-block", UNIT],
        check=True,
        timeout=10,
    )


class Handler(BaseHTTPRequestHandler):
    server_version = "PlaceContextOpenSearchSync/1.0"

    def do_GET(self) -> None:
        if self.path != "/health":
            self._json(HTTPStatus.NOT_FOUND, {"message": "Not found."})
            return
        self._json(HTTPStatus.OK, {"status": "ok"})

    def do_POST(self) -> None:
        if self.path != "/v1/sync":
            self._json(HTTPStatus.NOT_FOUND, {"message": "Not found."})
            return
        expected = f"Bearer {TOKEN}"
        supplied = self.headers.get("Authorization", "")
        if not hmac.compare_digest(supplied, expected):
            self._json(HTTPStatus.UNAUTHORIZED, {"message": "Unauthorized."})
            return
        if unit_is_active():
            self._json(
                HTTPStatus.CONFLICT,
                {"accepted": False, "status": "running", "message": "Collector sync is already running."},
            )
            return
        try:
            start_unit()
        except (subprocess.SubprocessError, OSError):
            self._json(
                HTTPStatus.INTERNAL_SERVER_ERROR,
                {"accepted": False, "status": "failed", "message": "Collector sync could not be started."},
            )
            return
        self._json(
            HTTPStatus.ACCEPTED,
            {"accepted": True, "status": "queued", "message": "Collector sync queued."},
        )

    def log_message(self, format: str, *args: object) -> None:
        # Keep authorization headers out of logs; BaseHTTPRequestHandler does not log them.
        super().log_message(format, *args)

    def _json(self, status: HTTPStatus, payload: dict[str, object]) -> None:
        body = json.dumps(payload, separators=(",", ":")).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)


if __name__ == "__main__":
    ThreadingHTTPServer((BIND, PORT), Handler).serve_forever()
