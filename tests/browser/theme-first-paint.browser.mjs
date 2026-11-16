// Verify saved themes before the server-rendered shell catches up with browser preferences.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');
const browser = await chromium.launch({ executablePath: process.env.CHROMIUM_PATH, headless: true });
try {
  const page = await browser.newPage();
  const css = await readFile(new URL('../../src/PlaceContext.Host/wwwroot/app.css', import.meta.url), 'utf8');
  for (const theme of ['dark', 'light']) {
    await page.setContent(`<html data-pc-theme="${theme}"><head><style>${css}</style></head><body><div id="dcshell" data-theme="light">Workspace</div></body></html>`);
    const expected = theme === 'dark' ? 'rgb(0, 0, 0)' : 'rgb(255, 255, 255)';
    assert.equal(await page.locator('#dcshell').evaluate(el => getComputedStyle(el).backgroundColor), expected);
    assert.equal(await page.locator('#dcshell').evaluate(el => getComputedStyle(el).getPropertyValue('--ch-bg').trim()), theme === 'dark' ? '#000000' : '#ffffff');
    await page.evaluate(theme => document.querySelector('#dcshell').dataset.theme = theme, theme);
    assert.equal(await page.locator('#dcshell').evaluate(el => getComputedStyle(el).backgroundColor), expected);
  }
  console.log('Passed: saved dark and light themes apply before shell initialization.');
} finally { await browser.close(); }
