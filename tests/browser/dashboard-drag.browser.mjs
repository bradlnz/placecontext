// Run with Playwright installed: node tests/browser/dashboard-drag.browser.mjs
// PLAYWRIGHT_MODULE may point to an existing playwright-core/index.mjs installation.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createServer } from 'node:http';
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');
const source = new URL('../../src/PlaceContext.Host/', import.meta.url);
const js = await readFile(new URL('wwwroot/build-chat.js', source), 'utf8');
const css = await readFile(new URL('Components/Pages/BuildChat.razor.css', source), 'utf8');
const html = `<!doctype html><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><link rel="stylesheet" href="/style.css">
<style>:root{--card:white;--brand:#1976d2;--brand-bg:#e3f2fd;--border:#ddd;--text:#111;--text-3:#555}body{margin:0;height:100vh;font-family:sans-serif}</style>
<div id="root" class="build-chat"><section class="chat-main"><div class="build-body"><div class="chat-content empty-chat dashboard-landing"><div class="chat-messages"><div class="chat-welcome">
<p class="dashboard-layout-status" role="status"></p><div class="dashboard-actions"></div>
</div></div></div></div></section></div>
<script type="module">
import { mountDashboardMove, mountDashboardResize } from '/build-chat.js';
const grid = document.querySelector('.dashboard-actions');
const saved = JSON.parse(localStorage.getItem('order') || 'null');
for (const key of saved || ['a','b','c','d','e','f','g','h','i','j']) {
 const tile = document.createElement('article');
 tile.className = 'dashboard-tile tile-square';
 if (JSON.parse(localStorage.getItem('rows') || '[]').includes(key)) tile.classList.add('tile-row-start'); tile.dataset.tile = key; tile.dataset.tileSize = JSON.parse(localStorage.getItem('sizes') || '{}')[key] || 'square';
 const cell = JSON.parse(localStorage.getItem('cells') || '{}')[key]; if (cell) { tile.dataset.tileColumn = cell.column; tile.dataset.tileRow = cell.row; }
 tile.classList.replace('tile-square', 'tile-' + tile.dataset.tileSize); tile.dataset.moveEnabled = 'true';
 tile.innerHTML = '<div class="dashboard-move"><button class="dashboard-drag-handle">Move '+key+'</button></div><button class="dashboard-action"><span class="dashboard-action-icon" aria-hidden="true">▶</span><span><strong>Project workflow statistics</strong><small>View recent activity and results for your project.</small></span></button><button class="dashboard-resize" aria-label="Resize tile">↘</button>';
 grid.append(tile);
}
window.calls = []; window.rejectSave = false;
const receiver = { async invokeMethodAsync(...args) {
 window.calls.push(args);
 await new Promise(resolve => setTimeout(resolve, 50));
 if (window.rejectSave) return false;
 const tiles = [...grid.children]; const tile = tiles.find(t => t.dataset.tile === args[1]);
 if (args[0] === 'ResizeDashboardTileAsync') {
   tile.classList.replace('tile-' + tile.dataset.tileSize, 'tile-' + args[2]);
   tile.dataset.tileSize = args[2];
   const sizes = JSON.parse(localStorage.getItem('sizes') || '{}'); sizes[args[1]] = args[2];
   localStorage.setItem('sizes', JSON.stringify(sizes)); return true;
 }
 tiles.splice(tiles.indexOf(tile), 1); tiles.splice(args[2], 0, tile);
 tile.classList.toggle('tile-row-start', args[3]);
 const cells = JSON.parse(localStorage.getItem('cells') || '{}');
 if (args[4] != null) { cells[args[1]] = { column: args[4], row: args[5] }; tile.dataset.tileColumn = args[4]; tile.dataset.tileRow = args[5]; } else { delete cells[args[1]]; delete tile.dataset.tileColumn; delete tile.dataset.tileRow; }
 if (args[6]) for (const [key, cell] of Object.entries(args[6])) { cells[key] = cell; const t = tiles.find(t => t.dataset.tile === key); t.dataset.tileColumn = cell.column; t.dataset.tileRow = cell.row; }
 localStorage.setItem('cells', JSON.stringify(cells));
 localStorage.setItem('rows', JSON.stringify(tiles.filter(t => t.classList.contains('tile-row-start')).map(t => t.dataset.tile)));
 grid.append(...tiles); localStorage.setItem('order', JSON.stringify(tiles.map(t => t.dataset.tile)));
 return true;
}};
const unmountMove = mountDashboardMove(document.querySelector('#root'), receiver);
const unmountResize = mountDashboardResize(document.querySelector('#root'), receiver);
window.unmount = () => { unmountMove(); unmountResize(); };
window.ready = true;
</script>`;
const server = createServer((request, response) => {
 const path = request.url;
 response.setHeader('Content-Type', path === '/build-chat.js' ? 'text/javascript' : path === '/style.css' ? 'text/css' : 'text/html');
 response.end(path === '/build-chat.js' ? js : path === '/style.css' ? css : html);
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const browser = await chromium.launch({ headless: true, executablePath: process.env.CHROMIUM_PATH || '/usr/bin/chromium' });
const base = `http://127.0.0.1:${server.address().port}`;
const errors = [];
try {
 const context = await browser.newContext({ viewport: { width: 1100, height: 750 } });
 const page = await context.newPage(); page.on('pageerror', error => errors.push(error.message));
 const load = async () => { await page.goto(base); await page.waitForFunction(() => window.ready); };
 const tile = key => page.locator('.dashboard-actions > [data-tile="'+key+'"]');
 const point = async key => { const b = await tile(key).locator('.dashboard-drag-handle').boundingBox(); return { x:b.x + b.width / 2, y:b.y + b.height / 2 }; };
 const order = () => page.locator('.dashboard-actions > .dashboard-tile').evaluateAll(nodes => nodes.map(n => n.dataset.tile));
 const drag = async (from, to) => { const a = await point(from), b = await point(to); await page.mouse.move(a.x,a.y); await page.mouse.down(); await page.mouse.move(b.x,b.y,{steps:12}); };
 await load();
 // Real tile text must fit phones and narrow panels, including saved wide/tall tiles.
 await page.evaluate(() => {
   const tiles = [...document.querySelectorAll('.dashboard-tile')];
   for (const [index, size] of ['square', 'wide', 'tall', 'large'].entries()) {
     tiles[index].className = 'dashboard-tile tile-' + size;
     tiles[index].dataset.tileSize = size;
     tiles[index].dataset.tileColumn = String(index + 1);
     tiles[index].dataset.tileRow = '1';
   }
   tiles[0].querySelector('strong').textContent = 'ProjectWorkflowStatistics'.repeat(2);
 });
 const savedPositions = () => page.locator('.dashboard-tile').evaluateAll(nodes => nodes.map(n => [n.dataset.tileSize, n.dataset.tileColumn, n.dataset.tileRow]));
 const desktopPositions = await savedPositions();
 for (const width of [320, 375, 600, 800, 1100]) {
   await page.setViewportSize({ width, height:750 });
   await page.waitForFunction(() => [...document.querySelectorAll('.dashboard-tile')].every(el => {
     const tile = el.getBoundingClientRect(), grid = el.parentElement.getBoundingClientRect();
     return tile.left >= grid.left - 1 && tile.right <= grid.right + 1;
   }));
   if (width <= 375) {
     assert.ok((await page.locator('.dashboard-actions').boundingBox()).width >= width - 24, 'phone tiles use the available panel width');
     assert.equal(await tile('a').locator('strong').evaluate(el => {
       const range = document.createRange(); range.selectNodeContents(el);
       const text = range.getBoundingClientRect(), content = el.parentElement.getBoundingClientRect();
       return text.right <= content.right + 1 && text.bottom <= content.bottom + 1;
     }), true, 'long tile titles must wrap inside the phone layout');
     assert.equal(await tile('b').locator('small').evaluate(el =>
       el.clientHeight >= Math.floor(parseFloat(getComputedStyle(el).lineHeight)) && el.scrollHeight <= el.clientHeight + 1),
       true, 'tile descriptions remain readable without clipping');
   }
   assert.deepEqual(await savedPositions(), desktopPositions, 'responsive layout must not overwrite saved tile sizes or positions');
 }
 await load();
 await tile('j').evaluate(el => { el.dataset.tileColumn = '5'; el.dataset.tileRow = '1'; });
 await page.setViewportSize({width:320,height:750});
 await page.waitForFunction(() => [...document.querySelectorAll('.dashboard-tile')].every(el =>
   el.getBoundingClientRect().width >= 160 && el.style.gridColumnStart === '1'));
 await page.setViewportSize({width:1100,height:750});
 await page.locator('.chat-content').evaluate(el => { el.style.flexBasis = '30%'; });
 await page.waitForFunction(() => [...document.querySelectorAll('.dashboard-tile')].every(el => el.style.gridColumnStart === '1'));
 await page.locator('.chat-content').evaluate(el => { el.style.flexBasis = ''; });
 await tile('j').evaluate(el => { delete el.dataset.tileColumn; delete el.dataset.tileRow; });
 await load();
 await drag('a','c');
 assert.equal(await page.locator('.dashboard-drag-ghost').count(), 1, 'tile must follow the pointer before release');
 assert.equal(await tile('a').evaluate(n => n.style.order), '2', 'placeholder must preview the destination');
 assert.deepEqual(await page.evaluate(() => window.calls), [], 'do not save during movement');
 await page.screenshot({ path:'/tmp/dashboard-drag-preview.png' });
 await page.mouse.up();
 await page.waitForFunction(() => document.querySelector('[role=status]').textContent.includes('saved.'));
 assert.deepEqual((await order()).slice(0,3), ['b','c','a']);
 assert.equal(await page.locator('.dashboard-drag-ghost').count(), 0);
 await load(); assert.deepEqual((await order()).slice(0,3), ['b','c','a'], 'saved order survives reload');
 await drag('b','d'); await page.keyboard.press('Escape'); await page.mouse.up();
 assert.deepEqual((await order()).slice(0,3), ['b','c','a']);
 assert.deepEqual(await page.evaluate(() => window.calls), []);
 await drag('b','d'); await page.mouse.move(5,5); await page.mouse.up();
 assert.deepEqual(await page.evaluate(() => window.calls), [], 'dropping outside cancels the preview');
 await tile('b').locator('button').first().click();
 assert.deepEqual(await page.evaluate(() => window.calls), [], 'a click must not reorder');
 await page.evaluate(() => window.rejectSave = true);
 await drag('b','d'); await page.mouse.up();
 await page.waitForFunction(() => document.querySelector('[role=status]').textContent.includes('not saved'));
 assert.equal((await order())[0], 'b', 'failed save restores original order');
 assert.equal(await tile('b').evaluate(n => n.style.order), '');
 await page.evaluate(() => window.rejectSave = false);
 await tile('b').locator('button').first().focus(); await page.keyboard.press('End');
 await page.waitForFunction(() => document.querySelector('[role=status]').textContent.includes('saved.'));
 assert.equal((await order()).at(-1), 'b', 'keyboard reordering');
 assert.equal(await page.evaluate(() => document.activeElement.closest('[data-tile]')?.dataset.tile),'b', 'keep keyboard focus after saving');
 const action = await tile('c').locator('.dashboard-action').boundingBox();
 await page.mouse.move(action.x + 30, action.y + 20); await page.mouse.down(); await page.mouse.move(action.x + 50,action.y + 40,{steps:8});
 assert.equal(await page.locator('.dashboard-drag-ghost').count(),0, 'tile actions stay usable without an arranging mode');
 await page.mouse.up();
 // Different tile spans still wrap within the viewport.
 await tile('a').evaluate(n => { n.className = 'dashboard-tile tile-large'; n.dataset.tileSize = 'large'; });
 const wideBefore = await tile('a').boundingBox();
 const wideHandle = await tile('a').locator('.dashboard-drag-handle').boundingBox();
 await page.mouse.move(wideHandle.x + wideHandle.width - 8, wideHandle.y + 18); await page.mouse.down();
 await page.mouse.move(wideHandle.x + wideHandle.width - 2, wideHandle.y + 18);
 assert.deepEqual(await tile('a').boundingBox(), wideBefore, 'grabbing the far edge of a wide tile must not jump a column');
 await page.keyboard.press('Escape'); await page.mouse.up();
 await drag('a','d'); await page.mouse.up();
 await page.waitForFunction(() => document.querySelector('[role=status]').textContent.includes('saved.'));
 assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth),true);
 // A short dashboard must grow vertically even when all its tiles fit on one row.
 await page.evaluate(() => { localStorage.clear(); localStorage.setItem('order', JSON.stringify(['a','b','c'])); });
 await load();
 const firstRow = await tile('a').boundingBox();
 const start = await point('c');
 await page.mouse.move(start.x,start.y); await page.mouse.down();
 await page.mouse.move(firstRow.x + firstRow.width / 2,firstRow.y + firstRow.height + 50,{steps:10});
 assert.ok((await tile('c').boundingBox()).y > firstRow.y, 'dropping below a partial row previews a new row');
 await page.mouse.up();
 await page.waitForFunction(() => document.querySelector('[role=status]').textContent.includes('saved.'));
 assert.ok(Number(await tile('c').getAttribute('data-tile-row')) > 1);
 // Resizing previews smoothly without changing the grid or losing a forced row break.
 const resize = tile('c').locator('.dashboard-resize');
 const beforeResize = await tile('c').boundingBox();
 const neighbour = await tile('a').boundingBox();
 const grip = await resize.boundingBox();
 await page.mouse.move(grip.x + 18,grip.y + 18); await page.mouse.down();
 const resizeX = beforeResize.width * .65, resizeY = beforeResize.height * .65;
 await page.mouse.move(grip.x + 18 + resizeX,grip.y + 18 + resizeY,{steps:12});
 const duringResize = await tile('c').boundingBox();
 assert.equal(duringResize.x,beforeResize.x); assert.equal(duringResize.y,beforeResize.y);
 assert.ok(Math.abs(duringResize.width - beforeResize.width - resizeX) < 1, 'preview follows the pointer without snapping the grid');
 assert.deepEqual(await tile('a').boundingBox(),neighbour, 'neighbours stay put while resizing');
 assert.equal(await tile('c').evaluate(n => n.style.gridColumn),'', 'preserve saved row starts');
 await page.mouse.up();
 await page.waitForFunction(() => document.querySelector('[role=status]').textContent === 'Tile size saved.');
 assert.equal(await tile('c').getAttribute('data-tile-size'),'large');
 const savedSize = await tile('c').boundingBox();
 assert.ok(savedSize.width > beforeResize.width * 1.8, 'a tile moved vertically must actually occupy two columns after resizing');
 assert.ok(savedSize.height > beforeResize.height * 1.8, 'a tile moved vertically must actually occupy two rows after resizing');
 await load();
 assert.equal(await tile('c').getAttribute('data-tile-size'),'large', 'resized dimensions survive reload');
 assert.equal((await tile('c').boundingBox()).width,savedSize.width, 'actual resized width survives reload');
 assert.ok((await tile('c').boundingBox()).y > (await tile('a').boundingBox()).y);
 const largeGrip = await resize.boundingBox();
 await page.mouse.move(largeGrip.x+18,largeGrip.y+18); await page.mouse.down();
 await page.mouse.move(largeGrip.x-100,largeGrip.y-100,{steps:8}); await page.keyboard.press('Escape'); await page.mouse.up();
 assert.equal(await tile('c').evaluate(n => n.style.width),'');
 assert.deepEqual(await page.evaluate(() => window.calls),[], 'cancelled resize does not save');
 await page.evaluate(() => window.rejectSave = true);
 await resize.focus(); await page.keyboard.press('ArrowUp');
 await page.waitForFunction(() => document.querySelector('[role=status]').textContent.includes('not saved'));
 assert.equal(await tile('c').getAttribute('data-tile-size'),'large');
 assert.equal(await tile('c').evaluate(n => n.style.height),'', 'failed saves restore the original size');
 await page.evaluate(() => window.rejectSave = false);
 const rowBeforeUp = Number(await tile('c').getAttribute('data-tile-row'));
 await tile('c').locator('button').first().focus(); await page.keyboard.press('ArrowUp');
 await page.waitForFunction(() => document.querySelector('[role=status]').textContent.includes('saved.'));
 assert.ok(Number(await tile('c').getAttribute('data-tile-row')) < rowBeforeUp, 'up arrow moves back to the row above');
 const rowBeforeDown = Number(await tile('c').getAttribute('data-tile-row'));
 await tile('c').locator('button').first().focus(); await page.keyboard.press('ArrowDown');
 await page.waitForFunction(() => document.querySelector('[role=status]').textContent.includes('saved.'));
 assert.ok(Number(await tile('c').getAttribute('data-tile-row')) > rowBeforeDown, 'down arrow creates a row below');
 // Place two small tiles in the same column beside a tall tile.
 await page.evaluate(() => { localStorage.clear(); localStorage.setItem('order', JSON.stringify(['a','b','c'])); localStorage.setItem('sizes', JSON.stringify({a:'tall'})); });
 await load();
 const tall = await tile('a').boundingBox(), top = await tile('b').boundingBox();
 const stackStart = await point('c');
 await page.mouse.move(stackStart.x, stackStart.y); await page.mouse.down();
 await page.mouse.move(top.x + 30, tall.y + top.height + 16 + 30, {steps:12});
 const stackedPreview = await tile('c').boundingBox();
 for (let i=0; i<12; i++) await page.mouse.move(top.x + 30 + i%2, tall.y + top.height + 16 + 30);
 assert.deepEqual(await tile('c').boundingBox(), stackedPreview, 'a stationary drop target must not oscillate');
 await page.mouse.up();
 await page.waitForFunction(() => document.querySelector('[role=status]').textContent.includes('saved.'));
 assert.equal((await tile('b').boundingBox()).x, (await tile('c').boundingBox()).x, 'two small tiles share a column');
 assert.equal((await tile('a').boundingBox()).y, (await tile('b').boundingBox()).y);
 assert.ok((await tile('c').boundingBox()).y > (await tile('b').boundingBox()).y);
 assert.ok((await tile('c').boundingBox()).y < tall.y + tall.height, 'bottom tile fits beside the tall tile');
 await load();
 const reloadedStack = await tile('c').boundingBox();
 assert.equal(reloadedStack.x, stackedPreview.x); assert.ok(Math.abs(reloadedStack.y - stackedPreview.y) < 1, 'stacked cell survives reload');
 await tile('b').locator('.dashboard-resize').focus(); await page.keyboard.press('ArrowDown');
 await page.waitForFunction(() => document.querySelector('[role=status]').textContent === 'Tile size saved.');
 const expanded = await tile('b').boundingBox(), displaced = await tile('c').boundingBox();
 assert.ok(displaced.y >= expanded.y + expanded.height, 'resizing a stacked tile pushes the next tile clear');
 await page.screenshot({path:'/tmp/dashboard-stacked-tiles.png'});
 await context.close();
 const mobile = await browser.newContext({ viewport:{width:320,height:640}, isMobile:true, hasTouch:true });
 const touch = await mobile.newPage(); touch.on('pageerror', error => errors.push(error.message));
 await touch.goto(base); await touch.waitForFunction(() => window.ready);
 assert.equal(await touch.evaluate(() => document.documentElement.scrollWidth <= innerWidth),true);
 assert.equal(await touch.locator('.dashboard-row-drop').count(),0);
 const session = await mobile.newCDPSession(touch);
 const send = (type,x,y) => session.send('Input.dispatchTouchEvent',{type,touchPoints:type==='touchEnd'||type==='touchCancel'?[]:[{x,y}]});
 const handle = await touch.locator('[data-tile=a] .dashboard-drag-handle').boundingBox();
 await send('touchStart',handle.x+20,handle.y+18);
 await send('touchMove',handle.x+20,handle.y+28);
 assert.equal(await touch.locator('.dashboard-drag-ghost').count(),1);
 await send('touchMove',60,600);
 await touch.waitForFunction(() => document.querySelector('.dashboard-landing').scrollTop > 150);
 await send('touchEnd');
 await touch.waitForFunction(() => window.calls.length === 1);
 await touch.waitForFunction(() => document.querySelector('[role=status]').textContent.includes('saved.'));
 assert.notEqual(await touch.locator('.dashboard-actions > article').first().getAttribute('data-tile'),'a');
 assert.equal(await touch.locator('.dashboard-drag-ghost').count(),0);
 await touch.screenshot({path:'/tmp/dashboard-mobile-layout.png'});
 await touch.locator('.dashboard-actions > article').first().evaluate(n => { n.className = 'dashboard-tile tile-large'; n.dataset.tileSize = 'large'; });
 assert.equal(await touch.evaluate(() => document.documentElement.scrollWidth <= innerWidth),true, 'large tiles fit a single mobile column');
 const mobileTile = touch.locator('.dashboard-actions > article').first();
 const mobileResize = mobileTile.locator('.dashboard-resize');
 await mobileResize.scrollIntoViewIfNeeded();
 const callsBeforeTap = await touch.evaluate(() => window.calls.length);
 await mobileResize.tap();
 assert.equal(await touch.evaluate(() => window.calls.length),callsBeforeTap, 'tapping a mobile resize handle preserves desktop width');
 const touchGrip = await mobileResize.boundingBox();
 await send('touchStart',touchGrip.x+18,touchGrip.y+18);
 const mobileRowHeight = await touch.locator('.dashboard-actions').evaluate(n => parseFloat(getComputedStyle(n).gridAutoRows));
 await send('touchMove',touchGrip.x+18,touchGrip.y+18-mobileRowHeight*.7);
 await send('touchEnd');
 await touch.waitForFunction(() => document.querySelector('[role=status]').textContent === 'Tile size saved.');
 assert.equal(await mobileTile.getAttribute('data-tile-size'),'wide', 'changing height on mobile preserves the saved two-column width');
 await mobile.close();
 assert.deepEqual(errors,[]);
 console.log('PASS: Chromium mouse/touch drag and resize, vertical move then resize, actual saved dimensions, mobile sizing, keyboard, cancellation, save failure and reload.');
} finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }
