#!/usr/bin/env python3
"""Real Chromium walkthrough of managed PlaceContext signup, login and first-run setup.
Uses only a disposable PostgreSQL + k3d environment; no external mail or Stripe."""
import json
import os
import re
from pathlib import Path
from playwright.sync_api import sync_playwright, expect

dest = Path(os.environ.get("SCREENSHOT_DIR", "screenshots/auth"))
dest.mkdir(parents=True, exist_ok=True)
base = os.environ.get("SAAS_TEST_ORIGIN", "http://localhost:7700")
slug = "new-team" + os.environ.get("GITHUB_RUN_ID", "dev")[-6:]
email = "owner@example.invalid"
password = "G!7jdN3M-wB4pQ9xe"
screens = []

def capture(page, filename):
    page.screenshot(path=str(dest / filename), animations="disabled", full_page=True)
    screens.append({"name": filename, "url": page.url})

with sync_playwright() as browser_tool:
    chromium = browser_tool.chromium.launch(headless=True, args=[
        "--no-sandbox", "--host-resolver-rules=MAP *.localhost 127.0.0.1"
    ])
    context = chromium.new_context(viewport={"width": 1400, "height": 930},
                                   color_scheme="dark", reduced_motion="reduce")
    page = context.new_page()
    failures = []
    page.on("pageerror", lambda err: failures.append(str(err)))
    response = page.goto(base + "/signup", wait_until="domcontentloaded", timeout=35000)
    assert response is not None and response.status == 200, response
    expect(page.get_by_role("heading", name="Start building with PlaceContext")).to_be_visible(timeout=35000)
    expect(page.locator('input[name="workspace"]')).to_be_visible()
    capture(page, "01-create-workspace.png")

    page.locator('input[name="workspaceName"]').fill("Northstar Operations")
    page.locator('input[name="workspace"]').fill(slug)
    page.locator('input[name="displayName"]').fill("Northstar Owner")
    page.locator('input[name="email"]').fill(email)
    page.locator('input[name="password"]').fill(password)
    page.locator('input[name="confirmPassword"]').fill(password)
    page.locator('input[name="plan"][value="standard"]').check()
    page.get_by_role("button", name="Create workspace").click(timeout=20000)
    page.wait_for_url(re.compile(r"/onboarding(?:\?.*)?$"), timeout=45000)
    expect(page.get_by_role("heading", name="Choose where your jobs run")).to_be_visible(timeout=35000)
    expect(page.get_by_text("Execution location")).to_be_visible(timeout=35000)
    assert page.url.startswith("http://" + slug + ".localhost:7700/"), page.url
    capture(page, "02-workspace-created-onboarding.png")
    print("Verified: signup created owner workspace and landed on cluster setup.")

    # Creating a workspace must not silently sign the browser into any other host.
    apex_context = chromium.new_context(viewport={"width": 1400, "height": 930})
    apex = apex_context.new_page()
    apex.goto(base + "/login", wait_until="domcontentloaded", timeout=35000)
    expect(apex.get_by_role("heading", name="Find your workspace")).to_be_visible(timeout=35000)
    capture(apex, "03-find-workspace.png")
    apex.locator('input[name="workspace"]').fill(slug)
    apex.get_by_role("button", name="Continue to workspace").click()
    apex.wait_for_url(re.compile(re.escape(slug + ".localhost:7700/login")), timeout=45000)
    expect(apex.get_by_role("heading", name="Welcome back")).to_be_visible(timeout=35000)
    capture(apex, "04-tenant-sign-in.png")

    apex.locator('input[name="email"]').fill(email)
    apex.locator('input[name="password"]').fill(password)
    apex.get_by_role("button", name=re.compile("Sign in")).click()
    apex.wait_for_url(re.compile(re.escape(slug + ".localhost:7700/") + r"(?:\?.*)?$"), timeout=45000)
    expect(apex.get_by_text("Workflow overview")).to_be_visible(timeout=35000)
    print("Verified: fresh-browser workspace discovery and real password login.")

    # Historic self-hosted first-admin bootstrap must be blocked on hosted tenants.
    res = apex.request.post(f"http://{slug}.localhost:7700/auth/setup/cli",
        data={"email": "second@example.invalid", "password": "NewValidPW#9031"},
        headers={"Content-Type": "application/json"})
    assert res.status == 404, (res.status, res.text()[:200])
    print("Verified: managed /auth/setup/cli cannot create a second owner.")

    # Capture the login on a genuinely narrow viewport.
    phone_context = chromium.new_context(viewport={"width": 390, "height": 844},
                                         device_scale_factor=1, is_mobile=True, has_touch=True)
    phone = phone_context.new_page()
    phone.goto(f"http://{slug}.localhost:7700/login", wait_until="domcontentloaded", timeout=35000)
    expect(phone.get_by_role("heading", name="Welcome back")).to_be_visible(timeout=35000)
    capture(phone, "05-mobile-login.png")
    assert not failures, "Browser errors: " + repr(failures[:3])
    chromium.close()

(dest / "manifest.json").write_text(json.dumps({
    "source": "Actual PlaceContext .NET Blazor on isolated PostgreSQL and k3d",
    "developmentVerificationBypass": True,
    "workspace": slug, "screenshots": screens
}, indent=2))
print(f"PASS: {len(screens)} authenticated managed login/signup screenshots.")
