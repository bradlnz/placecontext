import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';

function setup() {
  const scripts = [];
  const window = {};
  runInNewContext(readFileSync(new URL('../../src/PlaceContext.Host/wwwroot/pcmodules.js', import.meta.url), 'utf8'), {
    window,
    document: {
      currentScript: { dataset: { pcmonaco: 'pcmonaco.hash.js', pcgraph: 'pcgraph.hash.js' } },
      createElement: () => ({ remove() { this.removed = true; } }),
      head: { appendChild(script) { scripts.push(script); } },
    },
  });
  return { window, scripts };
}

test('optional interop stays unloaded until used and shares concurrent loads across namespaces', async () => {
  const { window, scripts } = setup();
  assert.equal(scripts.length, 0);
  const cachedGetValue = window.pcmonaco.getValue;
  const value = cachedGetValue('editor');
  const download = window.pcdata.download('output.csv', 'data:output');
  assert.equal(scripts.length, 1);
  assert.equal(scripts[0].src, 'pcmonaco.hash.js');
  window.pcmonaco = { getValue(id) { assert.equal(this, window.pcmonaco); return id + '-value'; } };
  window.pcdata = { download: (...args) => args.join(':') };
  scripts[0].onload();
  assert.equal(await value, 'editor-value');
  assert.equal(await download, 'output.csv:data:output');
  // Blazor caches resolved interop functions, so the original wrapper must remain usable.
  assert.equal(await cachedGetValue('next'), 'next-value');
  assert.equal(scripts.length, 1);
});

test('failed optional downloads reject callers and can be retried', async () => {
  const { window, scripts } = setup();
  const first = window.pcgraph.init('canvas');
  scripts[0].onerror();
  await assert.rejects(first, /pcgraph failed to load/);
  assert.ok(scripts[0].removed);
  const retry = window.pcgraph.init('canvas');
  assert.equal(scripts.length, 2);
  window.pcgraph = { init: id => id };
  scripts[1].onload();
  assert.equal(await retry, 'canvas');
});

test('a script that fails to register its API rejects instead of recursing forever', async () => {
  const { window, scripts } = setup();
  const first = window.pcgraph.init('canvas');
  scripts[0].onload();
  await assert.rejects(first, /pcgraph.init was not registered/);
});
