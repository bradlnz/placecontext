import sys, json, os, urllib.request, urllib.error, base64

# Higgsfield's image generation tool name.
TOOL_NAME = "generate_image"


def main():
    payload = json.loads(sys.stdin.read() or "{}")

    # ── Extract data from the nested payload ─────────────────────────────────
    site = payload.get("site") or {}
    subdiv = payload.get("subdiv_plan") or {}
    aerial = payload.get("aerial") or {}

    address = site.get("address", "").strip()
    land_m2 = site.get("land_m2")
    frontage_m = subdiv.get("frontage_m")
    layout = subdiv.get("layout", "")

    # Lot details from the subdivision plan.
    lots = subdiv.get("lots", [])
    lot_front = next((l for l in lots if l.get("role") == "lot_front"), {})
    lot_rear = next((l for l in lots if l.get("role") == "lot_rear"), {})

    lot_front_area = lot_front.get("area_m2")
    lot_rear_area = lot_rear.get("area_m2")
    lot_front_label = lot_front.get("label", "Lot A (front)")
    lot_rear_label = lot_rear.get("label", "Lot B (rear)")

    # Nearest facility.
    nearest = site.get("nearest_facility") or {}
    park_name = nearest.get("name", "")
    park_distance = nearest.get("distance_m", "")
    if park_name.startswith("("):
        park_name = "Local park"

    # Streets from the address.
    street1 = site.get("suburb", "")
    street2 = ""

    # Validate required fields.
    required = {
        "address": address,
        "total_area": land_m2,
        "lot1_area": lot_front_area,
        "lot2_area": lot_rear_area,
        "frontage": frontage_m,
    }
    missing = [k for k, v in required.items() if v is None or v == ""]
    if missing:
        print(f"Missing required fields: {', '.join(missing)}", file=sys.stderr)
        sys.exit(1)

    # Build a data-driven prompt.
    prompt = build_prompt(
        address=address,
        total_area=f"{land_m2} m²",
        lot1_area=f"{lot_front_area:.0f} m²",
        lot1_label=lot_front_label,
        lot2_area=f"{lot_rear_area:.0f} m²",
        lot2_label=lot_rear_label,
        layout=layout,
        frontage=f"{frontage_m:.0f} m",
        street1=street1,
        street2=street2,
        park_name=park_name,
        park_distance=f"{park_distance}m" if park_distance else "",
    )

    # Load MCP connection details injected at runtime.
    mcp_json = os.environ.get("MCP_CONNECTIONS_JSON", "[]")
    connections = json.loads(mcp_json)
    if not connections:
        print("No MCP connections configured for this job", file=sys.stderr)
        sys.exit(1)

    conn = next(
        (c for c in connections if "higgsfield" in c.get("Name", "").lower()),
        connections[0],
    )
    url = conn.get("Url", "")
    token = conn.get("Token", "")
    if not url or not token:
        print(f"MCP connection {conn.get('Name')} missing URL or token", file=sys.stderr)
        sys.exit(1)

    # Call Higgsfield.
    result = call_tool(url, token, TOOL_NAME, {"prompt": prompt})
    if not result:
        print("tool call returned no result", file=sys.stderr)
        sys.exit(1)

    image_b64 = extract_image(result)
    if not image_b64:
        print("could not extract image from result", file=sys.stderr)
        sys.exit(1)

    os.makedirs("/out", exist_ok=True)
    with open("/out/mockup.png", "wb") as f:
        f.write(base64.b64decode(image_b64))

    output = {"status": "ok", "prompt": prompt, "image": image_b64}
    with open("/out/result.json", "w") as f:
        json.dump(output, f)
    print(json.dumps(output))


def build_prompt(address, total_area, lot1_area, lot1_label, lot2_area, lot2_label,
                 layout, frontage, street1, street2, park_name, park_distance):
    """Build a detailed isometric mockup prompt from the actual property data."""

    layout_desc = "battle-axe" if layout == "battle_axe" else "side-by-side"
    access_handle = "A narrow access handle connects the front street to the rear lot." if layout == "battle_axe" else ""

    prompt = f"""An isometric, miniature-style architectural mockup of a suburban property subdivision, presented on a clean, beveled white studio plinth. The scene depicts the development potential for "{address}". The original property is a {layout_desc} layout with {frontage} frontage and a total area of {total_area}.

On the left side, a detailed existing Queenslander-style house (cream weatherboard, corrugated metal roof, wrap-around veranda) sits on a fenced lot labeled "{lot1_label}: {lot1_area}". Behind it, a newly created access handle leads to a rear lot. On the right, a modern, two-story contemporary dwelling is shown on a lot labeled "{lot2_label}: {lot2_area}". The entire original property is outlined with a precise red boundary line. Manicured hedges, varied miniature trees, and realistic grass detail the landscaping.

The surrounding suburban streets are asphalt with white markings and include miniature street signs. Professional, vector-sharp text overlays and thin leader lines annotate all key features. The header reads "{address}. Total Area: {total_area}" and the footer plaque reads "SITE MOCKUP: SUBDIVISION POTENTIAL, {address.upper()}". The lighting is soft, even, and studio-quality, casting subtle, realistic shadows. Highly detailed, photorealistic miniature model style."""

    return prompt


def call_tool(url, token, tool_name, arguments):
    """Call an MCP tool via JSON-RPC."""
    payload = {
        "jsonrpc": "2.0",
        "id": 1,
        "method": "tools/call",
        "params": {"name": tool_name, "arguments": arguments},
    }
    req = urllib.request.Request(
        url,
        data=json.dumps(payload).encode(),
        headers={
            "Content-Type": "application/json",
            "Accept": "*/*",
            "Authorization": f"Bearer {token}",
            "User-Agent": "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36",
        },
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=300) as resp:
            return json.loads(resp.read())
    except urllib.error.HTTPError as e:
        print(f"MCP call failed: {e.code} {e.reason} — {e.read().decode()}", file=sys.stderr)
        return None
    except Exception as e:
        print(f"MCP call failed: {e}", file=sys.stderr)
        return None


def extract_image(body):
    """Extract a base64 image from an MCP tool result."""
    if not isinstance(body, dict):
        return None

    result = body.get("result")
    if isinstance(result, dict):
        content = result.get("content", [])
        if isinstance(content, list):
            for item in content:
                if isinstance(item, dict) and item.get("type") == "image":
                    return item.get("data", "")

    for key in ("image_b64", "image_base64", "b64", "data"):
        val = body.get(key)
        if val:
            return val

    return None


if __name__ == "__main__":
    main()
