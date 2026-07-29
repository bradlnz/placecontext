import sys, json, os, urllib.request, urllib.error, base64

# Prompt template — placeholders are filled from the input JSON.
PROMPT_TEMPLATE = """An isometric, miniature-style architectural mockup of a suburban property subdivision, presented on a clean, beveled white studio plinth. The scene depicts the development potential for "{address}". On the left, a detailed existing Queenslander-style house (cream weatherboard, corrugated metal roof, veranda) sits on a fenced lot labeled "Lot 1: {lot1_area}, {lot1_label}". Behind it, a newly created "New Access Handle" leads to a rear lot. On the right, a modern, two-story dwelling is shown on a lot labeled "Lot 2: {lot2_area}, {lot2_label}". The entire original property is outlined with a precise boundary line. Manicured hedges, varied miniature trees, and realistic grass detail the landscaping. The surrounding streets, "{street1}" and "{street2}", are asphalt with white markings and include miniature street signs, one of which reads "{park_name} {park_distance}". Professional, vector-sharp text overlays and thin leader lines annotate all key features, including the header: "{address}. Total Area: {total_area}" and the footer plaque: "SITE MOCKUP: SUBDIVISION POTENTIAL, {address_upper}". The lighting is soft, even, and studio-quality, casting subtle, realistic shadows."""

TOOL_NAME = "generate_image"


def main():
    # Read the input payload (address, lot details, etc.).
    input_data = json.loads(sys.stdin.read() or "{}")

    # Fill the prompt template with input values (or sensible defaults).
    prompt = PROMPT_TEMPLATE.format(
        address=input_data.get("address", "36 Southern Cross Ave, Darra"),
        address_upper=input_data.get("address", "36 Southern Cross Ave, Darra").upper(),
        total_area=input_data.get("total_area", "1012 m²"),
        lot1_area=input_data.get("lot1_area", "506 m²"),
        lot1_label=input_data.get("lot1_label", "Existing Dwelling"),
        lot2_area=input_data.get("lot2_area", "506 m²"),
        lot2_label=input_data.get("lot2_label", "Proposed New Dwelling"),
        street1=input_data.get("street1", "Southern Cross Ave"),
        street2=input_data.get("street2", "Monier Rd"),
        park_name=input_data.get("park_name", "Monier Road Park"),
        park_distance=input_data.get("park_distance", "408m"),
    )

    # Load the MCP connection details injected at runtime by the job system.
    mcp_json = os.environ.get("MCP_CONNECTIONS_JSON", "[]")
    connections = json.loads(mcp_json)
    if not connections:
        print("No MCP connections configured for this job", file=sys.stderr)
        sys.exit(1)

    # Find the Higgsfield connection.
    conn = next(
        (c for c in connections if "higgsfield" in c.get("Name", "").lower()),
        connections[0],
    )

    url = conn.get("Url", "")
    token = conn.get("Token", "")
    if not url or not token:
        print(f"MCP connection {conn.get('Name')} missing URL or token", file=sys.stderr)
        sys.exit(1)

    # Call the Higgsfield image generation tool.
    result = call_tool(url, token, TOOL_NAME, {"prompt": prompt})
    if not result:
        print("tool call returned no result", file=sys.stderr)
        sys.exit(1)

    # Extract the generated image.
    image_b64 = extract_image(result)
    if not image_b64:
        print("could not extract image from result", file=sys.stderr)
        sys.exit(1)

    # Write the image and metadata to /out.
    os.makedirs("/out", exist_ok=True)
    with open("/out/mockup.png", "wb") as f:
        f.write(base64.b64decode(image_b64))

    output = {"status": "ok", "prompt": prompt, "image": image_b64}
    with open("/out/result.json", "w") as f:
        json.dump(output, f)
    print(json.dumps(output))


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

    # result.content is the standard MCP content array.
    result = body.get("result")
    if isinstance(result, dict):
        content = result.get("content", [])
        if isinstance(content, list):
            for item in content:
                if isinstance(item, dict) and item.get("type") == "image":
                    return item.get("data", "")

    # Also try the raw response fields as fallbacks.
    for key in ("image_b64", "image_base64", "b64", "data"):
        val = body.get(key)
        if val:
            return val

    return None


if __name__ == "__main__":
    main()
