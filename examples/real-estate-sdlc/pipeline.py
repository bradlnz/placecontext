"""Artifact packaging and evidence-based readiness checks for the real-estate workflow."""
import base64
import hashlib
import io
import json
import os
from pathlib import PurePosixPath
import sys
from urllib.parse import urlsplit
from uuid import uuid4
import zipfile

# Binary category scores avoid invented precision; missing evidence earns no points.
WEIGHTS = {"requirements": 10, "functional": 20, "automated_tests": 15,
           "security": 15, "data_integrity": 10, "accessibility": 10,
           "performance": 5, "operations": 10, "ui": 5}
HARD_GATES = {"functional", "automated_tests", "security", "data_integrity", "ui"}


def obj(value):
    if isinstance(value, str):
        try:
            value = json.loads(value)
        except ValueError:
            return {}
    return value if isinstance(value, dict) else {}


def artifact(name, content):
    return {"filename": name, "content": json.dumps(content, indent=2)}


def prepare(payload):
    p = dict(payload)
    p["previous_readiness"] = p.get("readiness")
    p["readiness"] = None
    for key in ("review", "qemu_ui", "artifacts", "implementation_ready", "source_sha256", "requirements_gate", "implementation_gate"):
        p[key] = None
    p["verification_id"] = str(uuid4())
    p["project_brief"] = p.get("project_brief") or "Build a production-ready real estate listing platform."
    p["delivery"] = "Store source archive and readiness reports as private platform artifacts in object storage."
    return p


def package(payload):
    p = dict(payload)
    implementation = obj(p.get("implementation"))
    files = implementation.get("files", [])
    errors = []
    output = io.BytesIO()
    seen = set()
    size = 0
    if not isinstance(files, list) or not 1 <= len(files) <= 1000:
        files = []
        errors.append("Supply 1–1000 source files with path and text content.")
    with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED) as archive:
        for file in files:
            path = file.get("path", "") if isinstance(file, dict) else ""
            content = file.get("content") if isinstance(file, dict) else None
            parts = PurePosixPath(path).parts
            if (not path or not parts or path.startswith("/") or "\\" in path or ":" in path
                    or any(part in (".", "..") for part in path.split("/")) or "\0" in path
                    or path in seen or not isinstance(content, str)):
                errors.append("Source contains an invalid or duplicate file path or non-text content.")
                continue
            if any(part in (".git", "node_modules", ".env") for part in parts):
                errors.append("Exclude credentials, .git and installed dependencies from the source archive.")
                continue
            size += len(content.encode())
            if size > 4 * 1024 * 1024:
                errors.append("Source exceeds the 4 MiB text limit; split the project into artifact bundles.")
                break
            seen.add(path)
            archive.writestr(path, content)
    archive_bytes = output.getvalue()
    preview = implementation.get("preview_url", "")
    try:
        url = urlsplit(preview)
        valid_url = url.scheme in ("http", "https") and bool(url.hostname) and not url.username and not url.password
    except (TypeError, ValueError):
        valid_url = False
    if not valid_url:
        errors.append("Supply a preview_url reachable from the QEMU guest.")
    p["implementation_ready"] = not errors
    p["packaging_errors"] = errors
    p["preview_url"] = preview if valid_url else None
    p["source_sha256"] = hashlib.sha256(archive_bytes).hexdigest() if not errors else None
    p["artifacts"] = [artifact("requirements.json", obj(p.get("requirements"))),
                      artifact("packaging-report.json", {"errors": errors, "source_sha256": p["source_sha256"]})]
    if not errors:
        p["artifacts"].append({"filename": "real-estate-source.zip", "base64": base64.b64encode(archive_bytes).decode()})
    return p


def score(payload):
    p = dict(payload)
    review = obj(p.get("review"))
    qemu = obj(p.get("qemu_ui"))
    # QEMU must emit a complete structured report for this attempt and archive.
    browser = obj(qemu.get("stdout"))
    current = bool(p.get("source_sha256")) and review.get("source_sha256") == p["source_sha256"] and review.get("verification_id") == p.get("verification_id")
    browser_current = (qemu.get("exitCode") == 0 and qemu.get("outputTruncated") is False
                       and browser.get("verification_id") == p.get("verification_id")
                       and browser.get("source_sha256") == p.get("source_sha256"))
    categories = obj(review.get("categories"))
    checks = {}
    for name, weight in WEIGHTS.items():
        result = obj(browser if name == "ui" else categories.get(name))
        evidence = result.get("evidence")
        backed = isinstance(evidence, list) and bool(evidence) and all(isinstance(e, str) and e.strip() for e in evidence)
        checks[name] = {"passed": bool((browser_current if name == "ui" else current)
                                      and result.get("passed") is True and backed),
                        "weight": weight, "evidence": evidence if backed else []}
    points = sum(c["weight"] for c in checks.values() if c["passed"])
    blockers = list(p.get("packaging_errors", []))
    for gate_name in ("requirements_gate", "implementation_gate"):
        gate = obj(p.get(gate_name))
        if gate.get("passed") is not True:
            blockers.append(gate_name + " did not pass its score and confidence thresholds.")
            blockers.extend(x for x in gate.get("blockers", []) if isinstance(x, str))
    supplied_blockers = review.get("blockers")
    if not current:
        blockers.append("Independent review is missing or does not match this attempt and source archive.")
    if not isinstance(supplied_blockers, list) or any(not isinstance(x, str) for x in supplied_blockers):
        blockers.append("Review must explicitly list blockers, including an empty list when none exist.")
    else:
        blockers.extend(supplied_blockers)
    for name in sorted(HARD_GATES):
        if not checks[name]["passed"]:
            blockers.append(name + " gate has no passing current evidence.")
    p["readiness"] = {"score": points, "target": 90,
                       "production_ready": p.get("implementation_ready") is True and points >= 90 and not blockers,
                       "checks": checks, "blockers": blockers,
                       "scoring_gates": {key: p.get(key) for key in ("requirements_gate", "implementation_gate")},
                       "verification_id": p.get("verification_id"), "source_sha256": p.get("source_sha256"),
                       "basis": "Evidence supplied by the reviewing agent and QEMU; a readiness assessment, not deployment certification."}
    p["artifacts"] = [artifact("production-readiness.json", p["readiness"])]
    return p


if __name__ == "__main__":
    functions = {"prepare": prepare, "package": package, "score": score}
    print(json.dumps(functions[os.environ["SDLC_STAGE"]](json.load(sys.stdin))))
