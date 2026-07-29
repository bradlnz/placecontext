import sys, json, os, urllib.request, urllib.error, base64

# The prompt that describes what image to generate.
PROMPT = "A beautiful modern real-estate property website mockup, clean professional design, hero section with property photos, navigation bar, search filter, listing cards, contact form, premium aesthetic"

# Higgsfield's image generation tool name.
TOOL_NAME = "generate_image"

def main():
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
    result = call_tool(url, token, TOOL_NAME, {"prompt": PROMPT})
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

    output = {"status": "ok", "prompt": PROMPT, "image": image_b64}
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
