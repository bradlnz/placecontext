import csv
import io
import json
import re
import sys
import urllib.parse
import urllib.request
from collections import Counter

SOURCE = "https://data.brisbane.qld.gov.au/api/explore/v2.1/catalog/datasets"


def clean_html(value):
    return re.sub(r"\s+", " ", re.sub(r"<[^>]+>", " ", value or "")).strip()


def main():
    payload = json.loads(sys.stdin.read() or "{}")
    limit = max(1, min(int(payload.get("limit", 100)), 100))
    query = str(payload.get("query", "")).strip()
    params = {"limit": limit}
    if query:
        params["where"] = f'search(metas, "{query.replace(chr(34), "")}")'
    url = SOURCE + "?" + urllib.parse.urlencode(params)
    request = urllib.request.Request(url, headers={"User-Agent": "PlaceContext-Brisbane-Open-Data/1.0"})
    with urllib.request.urlopen(request, timeout=45) as response:
        data = json.load(response)
    rows = []
    themes = Counter()
    for dataset in data.get("results", []):
        meta = dataset.get("metas", {}).get("default", {})
        dataset_themes = meta.get("theme") or []
        themes.update(dataset_themes)
        rows.append({
            "dataset_id": dataset.get("dataset_id"),
            "title": meta.get("title"),
            "themes": "; ".join(dataset_themes),
            "records_count": meta.get("records_count"),
            "modified": meta.get("modified"),
            "license": meta.get("license"),
            "description": clean_html(meta.get("description"))[:500],
            "records_api": f'https://data.brisbane.qld.gov.au/api/explore/v2.1/catalog/datasets/{dataset.get("dataset_id")}/records',
        })
    csv_buffer = io.StringIO()
    if rows:
        writer = csv.DictWriter(csv_buffer, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
    print(json.dumps({
        "publisher": "Brisbane City Council",
        "source": SOURCE,
        "source_license_note": "Dataset-specific; most Council datasets are CC BY",
        "catalogue_total": data.get("total_count", 0),
        "returned": len(rows),
        "theme_counts": dict(themes.most_common()),
        "datasets": rows,
        "artifacts": [{"filename": "brisbane-open-data-catalogue.csv", "content": csv_buffer.getvalue()}],
    }, separators=(",", ":")))


if __name__ == "__main__":
    main()
