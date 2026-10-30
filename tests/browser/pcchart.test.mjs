import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';

const source = readFileSync(new URL('../../src/PlaceContext.Host/wwwroot/pcchart.js', import.meta.url), 'utf8');
const spec = { type: 'bar', labels: ['A', 'B'], series: [{ name: 'count', values: [3, 7] }] };

function setup({ lazy = false } = {}) {
  const elements = new Map(), instances = new Map(), timers = new Map();
  const observers = [], scripts = [], visible = new Set();
  let intersect, nextTimer = 0;
  class Chart {
    constructor(canvas, config) {
      if (instances.has(canvas)) throw new Error('Canvas is already in use');
      this.canvas = canvas;
      this.config = config;
      instances.set(canvas, this);
    }
    destroy() {
      instances.delete(this.canvas);
      this.canvas = null;
    }
  }
  const context = {
    console, queueMicrotask,
    getComputedStyle: el => ({ getPropertyValue: name => el.tokens?.[name] ?? '' }),
    setTimeout(callback) { timers.set(++nextTimer, callback); return nextTimer; },
    clearTimeout(id) { timers.delete(id); },
    IntersectionObserver: class {
      constructor(callback) { intersect = callback; }
      observe(el) { visible.add(el); }
      unobserve(el) { visible.delete(el); }
    },
    MutationObserver: class {
      constructor(callback) { this.callback = callback; }
      observe(target, options) { observers.push({ callback: this.callback, options }); }
      disconnect() { const i = observers.findIndex(observer => observer.callback === this.callback); if (i >= 0) observers.splice(i, 1); }
    },
    document: {
      documentElement: {},
      currentScript: { dataset: { chartSrc: 'chart.fingerprint.js' } },
      getElementById: id => elements.get(id),
      createElement: () => ({}),
      head: { appendChild(script) { scripts.push(script); } },
    },
  };
  context.window = context;
  if (!lazy) context.Chart = Chart;
  runInNewContext(source, context);
  function mutate(type, attributeName) {
    for (const { callback, options } of observers)
      if (options[type] && (type !== 'attributes' || options.attributeFilter.includes(attributeName)))
        queueMicrotask(callback);
  }
  return {
    api: context.pcchart, instances, scripts,
    canvas(id) { const el = { id }; elements.set(id, el); mutate('childList'); return el; },
    shell(theme, tokens) {
      const shell = { theme, tokens, getAttribute: () => shell.theme };
      elements.set('dcshell', shell); mutate('childList'); return shell;
    },
    theme(shell, theme, tokens) { shell.theme = theme; shell.tokens = tokens; mutate('attributes', 'data-theme'); },
    rename(el, id) {
      elements.delete(el.id);
      el.id = id;
      elements.set(id, el);
      mutate('attributes', 'id');
    },
    show() { intersect([...visible].map(target => ({ target, isIntersecting: true }))); },
    load() { context.Chart = Chart; scripts.at(-1).onload(); },
  };
}

test('reused canvases release old charts before drawing new slots and the rest of the batch', async () => {
  const h = setup();
  const a = h.canvas('a'), b = h.canvas('b'), extra = h.canvas('extra');
  await h.api.renderAll([{ id: 'a', spec }, { id: 'b', spec }]);
  h.show();
  const previous = [...h.instances.values()];

  h.rename(a, 'c');
  h.rename(b, 'd');
  const updated = { ...spec, labels: ['New A', 'New B'] };
  await h.api.renderAll(['c', 'd', 'extra'].map(id => ({ id, spec: updated })));
  h.show();

  assert.ok(previous.every(chart => chart.canvas === null));
  assert.equal(h.instances.size, 3);
  for (const el of [a, b, extra])
    assert.deepEqual(h.instances.get(el).config.data.labels, updated.labels);
});

test('a replacement canvas renders without another interop call', async () => {
  const h = setup();
  const old = h.canvas('chart');
  await h.api.render('chart', JSON.stringify(spec));
  h.show();
  const replacement = h.canvas('chart');
  await new Promise(resolve => queueMicrotask(resolve));
  h.show();
  assert.equal(h.instances.has(old), false);
  assert.ok(h.instances.has(replacement));
});

test('a single-value line has a visible point', async () => {
  const h = setup();
  const single = h.canvas('single'), multiple = h.canvas('multiple');
  await h.api.renderAll([
    { id: 'single', spec: { type: 'line', labels: ['A'], series: [{ name: 'count', values: [7] }] } },
    { id: 'multiple', spec: { ...spec, type: 'line' } },
  ]);
  h.show();
  assert.ok(h.instances.get(single).config.data.datasets[0].pointRadius > 0);
  assert.equal(h.instances.get(multiple).config.data.datasets[0].pointRadius, 0);
});

test('concurrent renders share the fingerprinted library load and destroy cancels a pending chart', async () => {
  const h = setup({ lazy: true });
  const kept = h.canvas('kept'), removed = h.canvas('removed');
  const first = h.api.render('kept', spec);
  const second = h.api.render('removed', spec);
  h.api.destroy('removed');
  assert.equal(h.scripts.length, 1);
  assert.equal(h.scripts[0].src, 'chart.fingerprint.js');
  h.load();
  await Promise.all([first, second]);
  h.show();
  assert.ok(h.instances.has(kept));
  assert.equal(h.instances.has(removed), false);
});

test('theme changes redraw charts when the interactive shell mounts after the renderer loads', async () => {
  const h = setup();
  const shell = h.shell('dark', { '--text-3': '#9ba6bc', '--font-sans': 'Inter, system-ui, sans-serif' });
  const canvas = h.canvas('chart');
  await h.api.render('chart', spec);
  h.show();
  assert.equal(h.instances.get(canvas).config.options.scales.x.ticks.color, '#9ba6bc');

  h.theme(shell, 'light', { '--text-3': '#626c82', '--font-sans': 'Inter, system-ui, sans-serif' });
  await new Promise(resolve => queueMicrotask(resolve));
  const ticks = h.instances.get(canvas).config.options.scales.x.ticks;
  assert.equal(ticks.color, '#626c82');
  assert.equal(ticks.font.family, 'Inter, system-ui, sans-serif');
  assert.equal(h.instances.size, 1);
});
