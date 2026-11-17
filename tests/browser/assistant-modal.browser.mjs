// PLAYWRIGHT_MODULE may point to an existing playwright-core/index.mjs installation.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');
const source = await readFile(new URL('../../src/PlaceContext.Host/wwwroot/placecontext.js', import.meta.url), 'utf8');
const browser = await chromium.launch({ executablePath: process.env.CHROMIUM_PATH || '/usr/bin/chromium', headless: true });
try {
  for (const modal of ['agent', 'workflow', 'step', 'details', 'code']) {
    const page = await browser.newPage();
    await page.setContent(`<style>
      body{margin:0} .dcmodal-overlay,.dcslide,.workflow-code-window{position:fixed;inset:0 380px 0 0;z-index:50;background:white}
      .context-assistant{position:fixed;inset:0 0 0 auto;width:380px;z-index:51;background:white}
      .nested{z-index:70;inset:50px} [hidden]{display:none}
    </style><nav><button id="background">Background</button></nav>
    <div class="assistant-workspace"><main class="assistant-main">
      <div id="layer" class="${modal === 'code' ? 'workflow-code-window' : modal === 'details' ? 'dcslide workflow-editor' : 'dcmodal-overlay' + (modal === 'workflow' ? ' workflow-canvas-overlay' : modal === 'step' ? ' workflow-dialog-overlay' : '')}">
        ${modal === 'code' ? '<div class="code-window-head" style="height:100px"></div>' : ''}
        <section class="dcmodal ${modal === 'agent' ? 'agent-workspace' : modal === 'workflow' ? 'workflow-canvas-dialog' : ''}" role="dialog">${['workflow', 'step'].includes(modal) ? '<div class="dcmodal-head" style="height:100px"></div>' : ''}<button id="edit">Edit</button><button id="close">Close modal</button></section>
      </div></main><aside class="context-assistant"><button id="toggle">Chat</button><div id="panel" hidden><textarea aria-label="Question"></textarea><button id="send">Send</button></div></aside></div>`);
    await page.addScriptTag({ content: source });
    await page.evaluate(() => {
      document.querySelector('#toggle').onclick = () => { document.querySelector('#panel').hidden = !document.querySelector('#panel').hidden; };
      window.placecontext.initFocusLayers();
    });
    if (['workflow', 'step', 'code'].includes(modal)) {
      assert.equal(await page.locator('.assistant-workspace').evaluate(el => el.style.getPropertyValue('--workflow-header-height')), '100px');
      await page.locator('.dcmodal-head, .code-window-head').evaluate(el => { el.style.height = '160px'; });
      await page.waitForFunction(() => document.querySelector('.assistant-workspace').style.getPropertyValue('--workflow-header-height') === '160px');
    }
    assert.equal(await page.locator('#background').evaluate(el => !!el.closest('[inert]')), true);
    assert.equal(await page.locator('#toggle').evaluate(el => !!el.closest('[inert]')), false);
    await page.locator('#toggle').click();
    await page.getByRole('textbox', { name: 'Question' }).fill('Explain this page');
    await page.locator('#send').focus();
    await page.keyboard.press('Tab');
    assert.equal(await page.evaluate(() => document.activeElement.id), 'edit');
    await page.keyboard.press('Shift+Tab');
    assert.equal(await page.evaluate(() => document.activeElement.id), 'send');
    await page.evaluate(() => {
      const nested = document.createElement('div');
      nested.className = 'dcmodal-overlay nested';
      nested.innerHTML = '<section class="dcmodal"><button id="confirm">Confirm</button></section>';
      document.querySelector('#layer').append(nested);
    });
    await page.locator('#confirm').focus();
    assert.equal(await page.locator('#toggle').evaluate(el => !!el.closest('[inert]')), true);
    await page.evaluate(() => document.querySelector('.nested').remove());
    await page.locator('#toggle').click();
    assert.equal(await page.locator('#panel').isVisible(), false);
    await page.locator('#toggle').focus();
    await page.keyboard.press('Tab');
    assert.equal(await page.evaluate(() => document.activeElement.id), 'edit');
    await page.evaluate(() => document.querySelector('#layer').remove());
    await page.locator('#background').click();
    assert.equal(await page.locator('.assistant-workspace').evaluate(el => el.style.getPropertyValue('--workflow-header-height')), '');
    console.log(`${modal}: chat clicks, typing, focus cycling, nested modal isolation, and cleanup passed`);
    await page.close();
  }
} finally { await browser.close(); }
