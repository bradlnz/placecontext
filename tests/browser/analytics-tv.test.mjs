import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';

const source = readFileSync(new URL('../../src/PlaceContext.Host/wwwroot/pctv.js', import.meta.url), 'utf8');
function setup(mode = 'unsupported') {
  const listeners = new Map();
  let observer, disconnected = false, requests = 0;
  const document = {
    documentElement: {}, activeElement: null, fullscreenElement: null,
    addEventListener: (name, fn) => listeners.set(name, fn),
    removeEventListener: name => listeners.delete(name),
    exitFullscreen: async () => { document.fullscreenElement = null; },
  };
  const page = {
    dataset: {}, isConnected: true,
    querySelector: () => exit,
    querySelectorAll: () => [hidden, exit],
    contains: element => [hidden, exit].includes(element),
  };
  function button(kind, visible = true) {
    return {
      isConnected: true, disabled: false,
      focus() { document.activeElement = this; },
      getClientRects: () => visible ? [{}] : [],
      closest(selector) { return selector === '[data-tv-page]' ? page : selector === `[data-tv-${kind}]` ? this : null; },
    };
  }
  const trigger = button('enter'), hidden = button('hidden', false), exit = button('exit');
  if (mode !== 'unsupported') {
    page.requestFullscreen = () => {
      requests++;
      if (mode === 'denied') return Promise.reject(new Error('Denied'));
      document.fullscreenElement = page;
      listeners.get('fullscreenchange')();
      return Promise.resolve();
    };
  }
  runInNewContext(source, {
    document,
    MutationObserver: class {
      constructor(callback) { observer = callback; }
      observe() {}
      disconnect() { disconnected = true; }
    },
  });
  const click = target => listeners.get('click')({ target });
  return { document, page, trigger, exit, listeners, click, removed: () => observer(), get disconnected() { return disconnected; }, get requests() { return requests; } };
}

test('TV mode requests fullscreen from the click and restores layout and focus on exit', () => {
  const ui = setup('supported');
  ui.click(ui.trigger);
  assert.equal(ui.requests, 1);
  assert.equal(ui.page.dataset.tvMode, 'true');
  assert.equal(ui.document.fullscreenElement, ui.page);
  assert.equal(ui.document.activeElement, ui.exit);
  ui.click(ui.exit);
  assert.equal(ui.page.dataset.tvMode, undefined);
  assert.equal(ui.document.fullscreenElement, null);
  assert.equal(ui.document.activeElement, ui.trigger);
  assert.ok(ui.disconnected);
  assert.equal(ui.listeners.has('keydown'), false);
});

for (const mode of ['unsupported', 'denied']) {
  test(`TV presentation survives ${mode} fullscreen and Escape exits`, async () => {
    const ui = setup(mode);
    ui.click(ui.trigger);
    await Promise.resolve();
    assert.equal(ui.page.dataset.tvMode, 'true');
    let prevented = false;
    ui.listeners.get('keydown')({ key: 'Tab', preventDefault: () => { prevented = true; } });
    assert.ok(prevented);
    assert.equal(ui.document.activeElement, ui.exit);
    ui.listeners.get('keydown')({ key: 'Escape', preventDefault() {} });
    assert.equal(ui.page.dataset.tvMode, undefined);
    assert.ok(ui.disconnected);
  });
}

test('browser fullscreen exit and page navigation clean up presentation listeners', () => {
  const ui = setup('supported');
  ui.click(ui.trigger);
  ui.document.fullscreenElement = null;
  ui.listeners.get('fullscreenchange')();
  assert.equal(ui.page.dataset.tvMode, undefined);
  ui.click(ui.trigger);
  ui.page.isConnected = ui.trigger.isConnected = false;
  ui.removed();
  assert.equal(ui.page.dataset.tvMode, undefined);
  assert.equal(ui.listeners.has('fullscreenchange'), false);
  assert.ok(ui.disconnected);
});
