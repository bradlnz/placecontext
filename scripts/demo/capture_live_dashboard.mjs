#!/usr/bin/env node
/**
 * Capture the *real, current* PlaceContext dashboard and automatic workflow agents.
 * This script never manufactures chat, tool calls, run status, or agent output.
 * An operator signs in and drives an existing sample-data workflow in the browser.
 *
 * Install: npm install --no-save playwright && npx playwright install chromium
 * Run: PC_PROJECT_ID=<guid> node scripts/demo/capture_live_dashboard.mjs
 * Optional: PC_URL, PC_STORAGE_STATE, PC_CAPTURE_DIR, PC_SOURCE_REVISION
 */
import { chromium } from 'playwright';
import { createInterface } from 'node:readline/promises';
import { stdin as input, stdout as output } from 'node:process';
import { mkdir, copyFile, writeFile } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { join, resolve } from 'node:path';

const projectId = process.env.PC_PROJECT_ID;
if (!projectId || !/^[0-9a-f]{8}-[0-9a-f-]{27,}$/i.test(projectId)) {
  throw new Error('Set PC_PROJECT_ID to an existing project GUID containing an automatic multi-agent workflow.');
}
const baseUrl = (process.env.PC_URL || 'http://localhost:7700').replace(/\/$/, '');
const captureDir = resolve(process.env.PC_CAPTURE_DIR || 'artifacts/onboarding-dashboard');
const route = `${baseUrl}/project/${projectId}/dashboard`;
await mkdir(captureDir, { recursive: true });
const terminal = createInterface({ input, output });
const browser = await chromium.launch({ headless: false });
const context = await browser.newContext({
  viewport: { width: 1920, height: 1080 },
  deviceScaleFactor: 1,
  colorScheme: 'dark',
  recordVideo: { dir: captureDir, size: { width: 1920, height: 1080 } },
  ...(process.env.PC_STORAGE_STATE ? { storageState: process.env.PC_STORAGE_STATE } : {}),
});
const page = await context.newPage();
const video = page.video();
let evidence = null;
let succeeded = false;

try {
  await page.goto(route, { waitUntil: 'domcontentloaded', timeout: 45_000 });
  if (!(await page.locator('.build-chat .chat-main').isVisible().catch(() => false))) {
    console.log('Sign in in the browser, then navigate to the project dashboard.');
    await terminal.question('Press Enter when the current dashboard is visible. ');
    if (!page.url().includes(`/project/${projectId}/dashboard`)) {
      await page.goto(route, { waitUntil: 'domcontentloaded' });
    }
  }
  // Refuse to publish footage from the retired overview-based dashboard.
  await page.locator('.build-chat .chat-main .chat-messages').waitFor({ timeout: 30_000 });
  await page.locator('.chat-header select[id$="-chat-agent"]').waitFor();
  await page.locator('.chat-composer').waitFor();

  // Prefer the app's real dark-mode toggle, never override its styling or DOM.
  await page.evaluate(() => {
    if (document.documentElement.dataset.pcTheme !== 'dark' && window.placecontext?.toggleTheme) {
      window.placecontext.toggleTheme();
    }
  });
  await page.screenshot({ path: join(captureDir, '01-current-dashboard.png') });
  console.log('Verified: the current chat-first dashboard, agent selector and composer are present.');

  await page.getByRole('button', { name: 'Agent', exact: true }).click();
  await page.getByRole('button', { name: 'Sub-agents', exact: true }).click();
  await page.getByRole('heading', { name: 'Workflow agents' }).waitFor();
  const cards = page.locator('.agent-panel .agent-card');
  const workflowsWithAgents = await cards.count();
  const configuredAgents = await page.locator('.agent-panel .agent-card p strong').count();
  if (!workflowsWithAgents || configuredAgents < 2) {
    throw new Error('At least two automatic workflow agents must be configured in an accessible workflow before recording.');
  }
  await page.screenshot({ path: join(captureDir, '02-configured-subagents.png') });
  console.log(`Verified: ${configuredAgents} configured automatic workflow agents across ${workflowsWithAgents} workflow(s).`);
  await terminal.question('Show the configured sub-agents, then press Enter to continue. ');
  await page.getByRole('button', { name: 'Close workspace' }).click();

  console.log('\nIn the current dashboard, choose "Run a workflow" and start an AUTOMATIC multi-agent run on FICTIONAL sample data.');
  console.log('Keep the dashboard and its run preview open. Show the model/tool loop, agent activity and output.');
  await terminal.question('Press Enter only when a real automatic agent run is visible in the workspace. ');

  // This component is populated by real persisted workflow-agent activity.
  const activities = page.locator('.agent-activity .agent-terminal');
  await activities.first().waitFor({ timeout: 30_000 });
  const activeLines = page.locator('.agent-activity .terminal-line:not(.system):not(.failed)');
  await activeLines.first().waitFor({ timeout: 60_000 });
  const messages = (await activeLines.allTextContents()).map(x => x.trim()).filter(Boolean);
  if (!messages.length) {
    throw new Error('The run has no real automatic agent activity. Refusing to publish illustrative footage as execution.');
  }
  await page.screenshot({ path: join(captureDir, '03-live-agent-activity.png') });
  console.log(`Verified ${messages.length} recorded agent activity line(s).`);

  console.log('\nShow a tool invocation or output in the workspace, then the Human in the loop checkpoint.');
  console.log('Approve the FICTIONAL sample handoff manually and wait for the final agent result and run status.');
  await terminal.question('Press Enter after the completed result is visible. ');
  await page.screenshot({ path: join(captureDir, '04-result-after-human-review.png') });
  const finalText = (await page.locator('body').innerText()).slice(0, 50000);
  succeeded = /\bSucceeded\b/.test(finalText);
  evidence = {
    captured_at: new Date().toISOString(),
    source_revision: process.env.PC_SOURCE_REVISION || 'not supplied',
    base_url: new URL(baseUrl).origin,
    project_id: projectId,
    dashboard_route_verified: true,
    workflow_agent_cards: workflowsWithAgents,
    configured_automatic_agent_steps: configuredAgents,
    observed_real_agent_activity_lines: messages.length,
    observed_succeeded_text: succeeded,
    human_approval: 'performed manually by the operator; script does not approve',
    capture_type: 'real-browser-video; no mocked statuses or fabricated chat',
  };
  if (!succeeded) {
    throw new Error('A Succeeded status was not visible. Check the workflow result before marking the video complete.');
  }
} finally {
  await context.close();
  await browser.close();
  terminal.close();
}

const rawVideo = await video.path();
const rawPath = join(captureDir, 'dashboard-agents-real.webm');
await copyFile(rawVideo, rawPath);
await writeFile(join(captureDir, 'capture-evidence.json'), JSON.stringify(evidence, null, 2) + '\n');
const mp4 = join(captureDir, 'dashboard-agents-real.mp4');
try {
  execFileSync('ffmpeg', ['-hide_banner', '-loglevel', 'error', '-y', '-i', rawPath, '-c:v', 'libx264', '-crf', '21', '-preset', 'medium', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', '-an', mp4], { stdio: 'inherit' });
  console.log(`Verified live capture: ${mp4}`);
} catch {
  console.log(`ffmpeg unavailable or conversion failed; verified live capture available as ${rawPath}`);
}