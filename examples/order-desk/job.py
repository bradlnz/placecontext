import json
import os
import sys
from pathlib import Path
from domain import validate_order, price_order, fulfil_order

try:
    payload = json.load(sys.stdin)
    action = {"validate": validate_order, "price": price_order, "fulfil": fulfil_order}[os.environ["ORDER_STEP"]]
    result = action(payload)
    Path("/out/result.json").write_text(json.dumps(result))
    print(json.dumps({"level": "INFO", "message": "Order stage completed", "step": os.environ["ORDER_STEP"], "order_id": result["id"]}))
except (ValueError, KeyError, TypeError) as error:
    print(json.dumps({"level": "ERROR", "message": str(error)}), file=sys.stderr)
    sys.exit(2)
