import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';

const source = readFileSync(new URL('../../src/PlaceContext.Host/wwwroot/pcchart.js', import.meta.url), 'utf8');
const spec = { type: 'bar', title: 'Counts', labels: ['A', 'B'], series: [{ name: 'Count', values: [3, 7] }] };

function setup() {
  class Element {
    constructor(tag) {
      this.tag = tag;
      this.attributes = {};
      this.children = [];
      this.clientWidth = 500;
      this.clientHeight = 280;
    }
    setAttribute(name, value) { this.attributes[name] = String(value); }
    getAttribute(name) { return this.attributes[name]; }
    appendChild(child) { child.parentElement = this; this.children.push(child); return child; }
    replaceChildren(...children) {
      for (const child of this.children) {
        child.parentElement = null;
      }
      this.children = [];
      for (const child of children) {
        this.appendChild(child);
      }
    }
    remove() {
      if (this.parentElement) {
        this.parentElement.children = this.parentElement.children.filter(child => child !== this);
        this.parentElement = null;
      }
    }
    find(tag) { return this.children.flatMap(child => [...(child.tag === tag ? [child] : []), ...child.find(tag)]); }
  }
  const elements = new Map();
  const shell = new Element('div');
  shell.setAttribute('data-theme', 'light');
  elements.set('dcshell', shell);
  let mutation, resize;
  const observed = new Set();
  const context = {
    console, queueMicrotask, Intl,
    document: {
      documentElement: { dataset: {} },
      getElementById: id => elements.get(id),
      createElementNS: (_, tag) => new Element(tag),
    },
    MutationObserver: class {
      constructor(callback) { mutation = callback; }
      observe() {}
    },
    ResizeObserver: class {
      constructor(callback) { resize = callback; }
      observe(element) { observed.add(element); }
      unobserve(element) { observed.delete(element); }
    },
  };
  context.window = context;
  runInNewContext(source, context);
  return {
    api: context.pcchart, observed,
    host(id, width = 500) {
      const element = new Element('div');
      Object.assign(element, { id, clientWidth: width });
      elements.set(id, element);
      mutation();
      return element;
    },
    rename(element, id) { elements.delete(element.id); element.id = id; elements.set(id, element); mutation(); },
    resize(element, width) { element.clientWidth = width; resize([{ target: element }]); },
    theme(value) { shell.setAttribute('data-theme', value); mutation(); },
  };
}

const flush = () => new Promise(resolve => queueMicrotask(resolve));

test('renders real SVG bars with labels and accessible values, without a chart library', () => {
  const h = setup(), host = h.host('chart');
  h.api.render('chart', JSON.stringify(spec));
  assert.equal(host.find('svg').length, 1);
  assert.equal(host.find('rect').length, 2);
  assert.equal(host.find('rect')[1].getAttribute('aria-label'), 'Count · B: 7');
  assert.ok(Number(host.find('rect')[1].getAttribute('height')) > Number(host.find('rect')[0].getAttribute('height')));
});

test('late containers and replacements render without another interop call', async () => {
  const h = setup();
  h.api.render('chart', spec);
  const old = h.host('chart');
  await flush();
  assert.equal(old.find('rect').length, 2);
  const replacement = h.host('chart');
  await flush();
  assert.equal(old.find('svg').length, 0);
  assert.equal(replacement.find('rect').length, 2);
  assert.equal(h.observed.has(old), false);
});

test('reused hosts release the old chart before rendering the whole batch', async () => {
  const h = setup(), first = h.host('a'), second = h.host('b');
  h.api.renderAll(['a', 'b'].map(id => ({ id, spec })));
  h.rename(first, 'c');
  h.rename(second, 'd');
  h.api.renderAll(['c', 'd'].map(id => ({ id, spec: { ...spec, type: 'line' } })));
  await flush();
  for (const host of [first, second]) {
    assert.equal(host.find('svg').length, 1);
    assert.equal(host.find('polyline').length, 1);
  }
});

test('hidden charts recover their dimensions on reveal and colors on theme changes', async () => {
  const h = setup(), host = h.host('chart', 0);
  h.api.render('chart', spec);
  const before = host.find('svg')[0];
  const color = host.find('rect')[0].getAttribute('fill');
  h.resize(host, 360);
  assert.match(host.find('svg')[0].getAttribute('viewBox'), /^0 0 360 /);
  assert.notEqual(host.find('svg')[0], before);
  h.theme('dark');
  await flush();
  assert.notEqual(host.find('rect')[0].getAttribute('fill'), color);
});

test('single-point lines, zero bars and negative values retain visible marks', () => {
  const h = setup(), host = h.host('chart');
  h.api.render('chart', { type: 'line', labels: ['A'], series: [{ name: 'Count', values: [7] }] });
  assert.equal(host.find('circle').length, 1);
  h.api.render('chart', { ...spec, labels: ['negative', 'zero', 'positive'], series: [{ name: 'Value', values: [-3, 0, 7] }] });
  const bars = host.find('rect');
  assert.equal(bars.length, 3);
  assert.ok(Number(bars[0].getAttribute('y')) > Number(bars[2].getAttribute('y')));
  assert.ok(Number(bars[1].getAttribute('height')) > 0);
});

test('pie charts support slices, a single full circle and empty values', () => {
  const h = setup(), host = h.host('chart');
  h.api.render('chart', { ...spec, type: 'pie' });
  assert.equal(host.find('path').length, 2);
  h.api.render('chart', { type: 'pie', labels: ['All'], series: [{ name: 'Total', values: [7] }] });
  assert.equal(host.find('circle').filter(circle => circle.getAttribute('role') === 'img').length, 1);
  h.api.render('chart', { ...spec, type: 'pie', series: [{ name: 'Total', values: [0, 0] }] });
  assert.ok(host.find('text').some(text => text.textContent === 'No non-zero values'));
});

test('invalid data is visible and does not prevent later charts rendering', () => {
  const h = setup(), invalid = h.host('invalid'), valid = h.host('valid');
  h.api.renderAll([{ id: 'invalid', spec: '{' }, { id: 'valid', spec }]);
  assert.ok(invalid.find('text').some(text => text.textContent.includes('invalid')));
  assert.equal(valid.find('rect').length, 2);
  h.api.render('invalid', { ...spec, series: [{ name: 'Bad', values: [1, Infinity] }] });
  assert.equal(invalid.find('rect').length, 0);
});

test('untrusted labels are text and extreme finite values produce finite coordinates', () => {
  const h = setup(), host = h.host('chart');
  h.api.render('chart', { ...spec, labels: ['<script>alert(1)</script>', 'B'], series: [{ name: 'Large', values: [-1e308, 1e308] }] });
  assert.equal(host.find('script').length, 0);
  assert.match(host.find('desc')[0].textContent, /<script>/);
  for (const bar of host.find('rect')) {
    assert.ok(Number.isFinite(Number(bar.getAttribute('y'))));
    assert.ok(Number.isFinite(Number(bar.getAttribute('height'))));
  }
});

test('destroy clears the output, observer and pending render', async () => {
  const h = setup(), host = h.host('chart');
  h.api.render('chart', spec);
  h.api.destroy('chart');
  assert.equal(host.find('svg').length, 0);
  assert.equal(h.observed.has(host), false);
  h.api.render('pending', spec);
  h.api.destroy('pending');
  const pending = h.host('pending');
  await flush();
  assert.equal(pending.find('svg').length, 0);
});
