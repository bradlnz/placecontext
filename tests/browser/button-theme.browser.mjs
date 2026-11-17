import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');
const css = await Promise.all([
  '../../src/PlaceContext.Host/wwwroot/app.css',
  '../../src/PlaceContext.Host/Components/Pages/ProjectData.razor.css',
  '../../src/PlaceContext.Host/Components/Pages/DataSources.razor.css',
  '../../src/PlaceContext.Host/Components/Pages/JobChains.razor.css',
].map(path => readFile(new URL(path, import.meta.url), 'utf8')));
const browser = await chromium.launch({ executablePath: process.env.CHROMIUM_PATH, headless: true });
try {
  const page = await browser.newPage();
  for (const theme of ['dark', 'light']) {
    await page.setContent(`<html data-pc-theme="${theme}"><style>${css.join('\n')}</style><body>
      <div id="dcshell">
        <div class="data-page"><div class="run-bar"><button class="dcbtn primary">Run query</button></div></div>
        <div class="source-page"><a href="#" class="dcbtn primary">Connect source</a></div>
        <div class="list-stage-footer"><button class="dcbtn">Add step</button></div>
        <button class="studio-new-table">New table</button>
        <button class="studio-run">Run query</button>
        <button class="studio-run" disabled>Running</button>
        <button class="dcbtn" disabled>Unavailable</button>
        <button class="dcbtn danger">Delete</button>
      </div></body></html>`);
    for (const button of await page.locator('button:not(:disabled):not(.danger), a.dcbtn').all()) {
      await button.hover();
      await page.waitForTimeout(200);
      const style = await button.evaluate(el => {
        const s = getComputedStyle(el);
        return { background: s.backgroundColor, color: s.color, radius: s.borderRadius };
      });
      assert.equal(style.background, theme === 'dark' ? 'rgb(255, 255, 255)' : 'rgb(32, 37, 50)');
      assert.equal(style.color, theme === 'dark' ? 'rgb(0, 0, 0)' : 'rgb(255, 255, 255)');
      if (await button.evaluate(el => el.classList.contains('dcbtn'))) assert.equal(style.radius, '0px');
    }
    for (const button of await page.locator(':disabled').all()) {
      await button.hover();
      assert.equal(await button.evaluate(el => getComputedStyle(el).backgroundColor), 'rgba(0, 0, 0, 0)');
    }
    await page.locator('.danger').hover();
    await page.waitForTimeout(200);
    assert.equal(await page.locator('.danger').evaluate(el => getComputedStyle(el).color), 'rgb(255, 255, 255)');
  }
  console.log('Passed: button hover contrast survives page styles in dark and light mode; disabled and danger states remain distinct.');
} finally { await browser.close(); }
