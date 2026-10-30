import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';

test('live preview stays locked until control is granted and relocks on resume', () => {
  const clients = [];
  class RFB {
    constructor(element, url) { this.url = url; this.listeners = {}; clients.push(this); }
    addEventListener(name, callback) { this.listeners[name] = callback; }
    disconnect() { this.closed = true; this.listeners.disconnect?.(); }
  }
  const attributes = new Map();
  const element = { setAttribute: (k, v) => attributes.set(k, v), removeAttribute: k => attributes.delete(k) };
  const source = readFileSync(new URL('../../src/PlaceContext.Host/wwwroot/pcdesktop.js', import.meta.url), 'utf8')
    .replace(/^import .*;$/m, '').replaceAll('export function', 'function');
  const context = { RFB, URL, document: { getElementById: () => element }, location: new URL('https://portal.test/run') };
  runInNewContext(source + '\nthis.api = {connect, disconnect};', context);
  context.api.connect('desktop', '/console');
  assert.equal(clients[0].viewOnly, true);
  assert.equal(new URL(clients[0].url).searchParams.get('viewOnly'), 'true');
  context.api.connect('desktop', '/console');
  assert.equal(clients.length, 1);
  context.api.connect('desktop', '/console', true);
  assert.equal(clients[0].closed, true);
  assert.equal(clients[1].viewOnly, false);
  assert.equal(new URL(clients[1].url).searchParams.get('viewOnly'), 'false');
  context.api.connect('desktop', '/console', false);
  assert.equal(clients[1].closed, true);
  assert.equal(clients[2].viewOnly, true);
  assert.equal(clients[2].focusOnClick, false);
  assert.throws(() => context.api.connect('desktop', 'https://another.test/console'), /application origin/);
});
