import importlib.util
import json
import os
from pathlib import Path
import threading
import unittest
from unittest.mock import patch
from urllib.error import HTTPError
from urllib.request import Request, urlopen

os.environ.setdefault("SYNC_TRIGGER_TOKEN", "test-token")
MODULE_PATH = Path(__file__).parents[2] / "deploy" / "opensearch-sync-trigger" / "server.py"
SPEC = importlib.util.spec_from_file_location("opensearch_sync_trigger", MODULE_PATH)
assert SPEC and SPEC.loader
trigger = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(trigger)


class OpenSearchSyncTriggerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.server = trigger.ThreadingHTTPServer(("127.0.0.1", 0), trigger.Handler)
        cls.thread = threading.Thread(target=cls.server.serve_forever, daemon=True)
        cls.thread.start()
        cls.base_url = f"http://127.0.0.1:{cls.server.server_port}"

    @classmethod
    def tearDownClass(cls):
        cls.server.shutdown()
        cls.server.server_close()
        cls.thread.join(timeout=2)

    @patch.object(trigger, "start_unit")
    @patch.object(trigger, "unit_is_active", return_value=False)
    def test_authorized_request_queues_ingestion(self, _active, start):
        status, body = self._post("Bearer test-token")

        self.assertEqual(202, status)
        self.assertTrue(body["accepted"])
        self.assertEqual("queued", body["status"])
        start.assert_called_once_with()

    @patch.object(trigger, "start_unit")
    def test_invalid_token_is_rejected_without_starting_ingestion(self, start):
        status, body = self._post("Bearer wrong-token")

        self.assertEqual(401, status)
        self.assertEqual("Unauthorized.", body["message"])
        start.assert_not_called()

    @patch.object(trigger, "start_unit")
    @patch.object(trigger, "unit_is_active", return_value=True)
    def test_running_ingestion_is_not_started_twice(self, _active, start):
        status, body = self._post("Bearer test-token")

        self.assertEqual(409, status)
        self.assertEqual("running", body["status"])
        start.assert_not_called()

    def _post(self, authorization):
        request = Request(
            self.base_url + "/v1/sync",
            method="POST",
            headers={"Authorization": authorization},
        )
        try:
            response = urlopen(request, timeout=2)
        except HTTPError as error:
            response = error
        with response:
            return response.status, json.loads(response.read())


if __name__ == "__main__":
    unittest.main()
