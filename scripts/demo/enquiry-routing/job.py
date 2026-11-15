"""Small, offline jobs for the enquiry-routing demonstration."""
import json
import os
import sys
import uuid


def run(data, operation):
    if operation == "validate":
        uuid.UUID(data["request_id"])
        if data.get("demo") is not True or not data.get("email", "").endswith("@example.test"):
            raise ValueError("This demo accepts fictional example.test contacts only")
        if not 10 <= len(data.get("question", "")) <= 2000:
            raise ValueError("A question of 10–2000 characters is required")
        return {**data, "intake_validated": True}
    if operation == "guard":
        route = data["routing"]
        roster = data["team"]["rows"]
        if not any(r["person"] == route["person"] and r["department"] == route["department"] for r in roster):
            raise ValueError("The selected person is not on the team roster")
        if data["review"].get("passed") is not True:
            raise ValueError("Independent routing review did not pass")
        return {**data, "routing_verified": True}
    if operation == "receipt":
        matches = [r for r in data["assignments"]["rows"] if r["request_id"] == data["request_id"]]
        if len(matches) != 1 or matches[0]["assigned_to"] != data["routing"]["person"]:
            raise ValueError("The saved portal assignment does not match the routing decision")
        if matches[0]["status"] != "Assigned":
            raise ValueError("The portal has not saved the assignment")
        receipt = {"request_id": data["request_id"], "status": "Assigned", "assigned_to": matches[0]["assigned_to"],
                   "department": data["routing"]["department"], "question": data["question"],
                   "reason": data["routing"]["reason"], "browser_assignment_verified": True,
                   "external_messages_sent": False}
        return {**data, "receipt": receipt}
    raise ValueError("Unknown demo operation")


if __name__ == "__main__":
    print(json.dumps(run(json.load(sys.stdin), os.environ["DEMO_OPERATION"])))
