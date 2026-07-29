import sys, json, os, urllib.request, urllib.error, base64

# Hard-coded prompt for the site mockup image.
PROMPT = "A beautiful modern real-estate property website mockup, clean professional design, hero section with property photos, navigation bar, search filter, listing cards, contact form, premium aesthetic"

def main():
    input_data = json.loads(sys.stdin.read() or "{}")

    # Prompt is defined as a constant above; input data can still be logged for traceability.
    prompt = PROMPT
    model = input_data.get("model", "openai-hazel")
    aspect_ratio = input_data.get("aspect_ratio", "1:1")

    # Load MCP connections from env var injected at runtime.
    mcp_json = os.environ.get("MCP_CONNECTIONS_JSON", "[]")
    connections = json.loads(mcp_json)
    if not connections:
        print("No MCP connections configured for this job", file=sys.stderr)
        sys.exit(1)

    # Find Higgsfield connection (or use the first one).
    conn = None
    for c in connections:
        if "higgsfield" in c.get("Name", "").lower():
            conn = c
            break
    if not conn:
        conn = connections[0]

    url = conn.get("Url", "")
    token = conn.get("Token", "")
    if not url or not token:
        print(f"MCP connection {conn.get('Name')} missing URL or token", file=sys.stderr)
        sys.exit(1)

    tool_name = discover_tool(url, token)
    if not tool_name:
        print("no image generation tool found on Higgsfield MCP", file=sys.stderr)
        sys.exit(1)

    response = call_mcp_tool(url, token, tool_name, prompt, model, aspect_ratio)
    if not response:
        print("tool call returned no result", file=sys.stderr)
        sys.exit(1)

    image_b64 = extract_image(response)

    if not image_b64:
        print("could not extract image from result", file=sys.stderr)
        sys.exit(1)

    os.makedirs("/out", exist_ok=True)
    image_bytes = base64.b64decode(image_b64)
    with open("/out/mockup.png", "wb") as f:
        f.write(image_bytes)

    output = {
        "status": "ok",
        "prompt": prompt,
        "image": image_b64,
        "tool": tool_name,
        "model": model,
    }
    with open("/out/result.json", "w") as f:
        json.dump(output, f)
    print(json.dumps(output))

def discover_tool(url, token):
    body = call_mcp_jsonrpc(url, token, "tools/list", {})
    if not body:
        return None
    result = body.get("result")
    if isinstance(result, dict):
        tools = result.get("tools", [])
    elif isinstance(result, list):
        tools = result
    else:
        tools = body.get("tools", [])
    if isinstance(tools, list):
        image_tools = [t["name"] for t in tools if "image" in t.get("name", "").lower()]
        return image_tools[0] if image_tools else None
    return None

def call_mcp_tool(url, token, tool_name, prompt, model, aspect_ratio):
    return call_mcp_jsonrpc(url, token, "tools/call", {
        "name": tool_name,
        "arguments": {
            "params": {
                "model": model,
                "prompt": prompt,
                "aspect_ratio": aspect_ratio,
            },
        },
    })

def extract_image(body):
    """Try to extract image base64 from the MCP tool response."""
    if not isinstance(body, dict):
        return None

    raw = body.get("rawContent")
    if isinstance(raw, list):
        for item in raw:
            if isinstance(item, dict):
                if item.get("type") == "image":
                    data = item.get("data", "")
                    if data:
                        return data
                if item.get("type") == "text":
                    text = item.get("text", "")
                    try:
                        parsed = json.loads(text)
                        if isinstance(parsed, dict):
                            for key in ("image_b64", "image_base64", "b64", "data", "url", "image_url"):
                                val = parsed.get(key)
                                if val:
                                    if key in ("url", "image_url"):
                                        return fetch_image_b64(val)
                                    return val
                    except (json.JSONDecodeError, TypeError):
                        pass

    content = body.get("content")
    if content:
        try:
            parsed = json.loads(content)
            if isinstance(parsed, dict):
                for key in ("image_b64", "image_base64", "b64", "data", "url", "image_url"):
                    val = parsed.get(key)
                    if val:
                        if key in ("url", "image_url"):
                            return fetch_image_b64(val)
                        return val
        except (json.JSONDecodeError, TypeError):
            pass

    for key in ("image_b64", "image_base64", "b64", "data", "url", "image_url"):
        val = body.get(key)
        if val:
            if key in ("url", "image_url"):
                return fetch_image_b64(val)
            return val

    return None

def call_mcp_jsonrpc(url, token, method, params):
    payload = {
        "jsonrpc": "2.0",
        "id": 1,
        "method": method,
        "params": params,
    }
    data = json.dumps(payload).encode()
    req = urllib.request.Request(
        url,
        data=data,
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
        body = e.read().decode()
        print(f"MCP {method} failed: {e.code} {e.reason} — {body}", file=sys.stderr)
        return None
    except Exception as e:
        print(f"MCP {method} call failed: {e}", file=sys.stderr)
        return None

def fetch_image_b64(url):
    try:
        with urllib.request.urlopen(url, timeout=60) as resp:
            return base64.b64encode(resp.read()).decode()
    except Exception as e:
        print(f"fetching image from {url} failed: {e}", file=sys.stderr)
        return None

if __name__ == "__main__":
    main()
