// Run against the local development app with at least one project; no server is saved.
// PLAYWRIGHT_MODULE and CHROMIUM_PATH can use an existing browser installation.
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');
import assert from 'node:assert/strict';
const browser = await chromium.launch({ executablePath: process.env.CHROMIUM_PATH, headless: true });
try {
  const page = await browser.newPage();
  const base = process.env.PLACECONTEXT_BASE_URL || 'http://localhost:7700';
  await page.goto(base + '/auth/portal?returnUrl=%2Fsettings%2Fmcp');
  await page.getByRole('button', { name: 'Add server', exact: true }).click();
  const name = page.getByRole('textbox', { name: 'Name', exact: true });
  await name.waitFor();
  await name.click();
  await page.keyboard.type('Focus test server', { delay: 150 });
  console.log('Name:', await name.inputValue());
  assert.equal(await name.inputValue(), 'Focus test server');
  const endpoint = page.getByRole('textbox', { name: 'Server URL', exact: true });
  await endpoint.click();
  await page.keyboard.type('https://example.test/mcp', { delay: 150 });
  assert.equal(await endpoint.inputValue(), 'https://example.test/mcp');
  assert.equal(await endpoint.evaluate(el => el === document.activeElement), true);
  await page.getByRole('combobox', { name: /^Authentication/ }).selectOption('bearer');
  const token = page.getByLabel('Token', { exact: true });
  await token.click();
  await page.keyboard.type('test-only-token', { delay: 150 });
  assert.equal(await token.inputValue(), 'test-only-token');
  assert.equal(await token.evaluate(el => el === document.activeElement), true);
  await page.getByRole('combobox', { name: /^Transport/ }).selectOption('stdio');
  const command = page.getByRole('textbox', { name: 'Command', exact: true });
  await command.click();
  await page.keyboard.type('test-command', { delay: 150 });
  assert.equal(await command.inputValue(), 'test-command');
  assert.equal(await command.evaluate(el => el === document.activeElement), true);
  assert.equal(await name.inputValue(), 'Focus test server');
  await token.focus();
  await page.keyboard.press('Tab');
  assert.equal(await page.getByRole('button', { name: 'Cancel', exact: true }).evaluate(el => el === document.activeElement), true);
  await page.keyboard.press('Escape');
  await page.locator('.mcp-form').waitFor({ state: 'detached' });
  console.log('PASS: typing, authentication change, Tab and Escape retain expected focus. No server saved.');
} finally {
  await browser.close();
}
