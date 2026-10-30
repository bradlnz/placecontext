import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';

function setup() {
  const scripts = [], created = [];
  const host = { children: [], appendChild(child) { this.children.push(child); }, contains(child) { return this.children.includes(child); } };
  const document = {
    activeElement: null,
    getElementById: id => id === 'editor' ? host : null,
    createElement: () => ({ dataset: {}, style: {}, setAttribute(name, value) { this[name] = value; },
      remove() { host.children = host.children.filter(child => child !== this); } }),
    head: { appendChild(script) { scripts.push(script); } },
  };
  const monaco = {
    Uri: { parse: value => value },
    languages: { registerCompletionItemProvider() {} },
    editor: {
      createModel(value, language, uri) {
        const model = { getValue: () => value, getLanguageId: () => language, getPositionAt: n => ({lineNumber:1,column:n+1}), dispose() {} };
        created.push({value,language,uri});
        return model;
      },
      create(el, {model}) { return { getModel: () => model, getValue: model.getValue, setSelection() {}, focus() {}, dispose() {} }; },
    },
  };
  const window = { require(dependencies, loaded) { window.monaco = monaco; loaded(); } };
  window.require.config = () => {};
  runInNewContext(readFileSync(new URL('../../src/PlaceContext.Host/wwwroot/pcmonaco.js', import.meta.url), 'utf8'), {
    window, document, console: { warn() {} }, setTimeout, clearTimeout,
  });
  return { window, document, host, scripts, created };
}

test('editor is usable immediately and keeps typed text and the current tab when Monaco arrives', async () => {
  const {window, document, host, scripts, created} = setup();
  const loading = window.pcmonaco.init('editor', 'original', 'sql', null, 'first.sql');
  assert.ok(window.pcmonaco.isMounted('editor'));
  assert.equal(window.pcmonaco.getValue('editor'), 'original');
  window.pcmonaco.openFile('editor', 'second.sql', 'second', 'sql');
  host.children[0].value = 'typed while loading';
  host.children[0].selectionStart = host.children[0].selectionEnd = 5;
  document.activeElement = host.children[0];
  scripts[0].onload();
  assert.equal(await loading, true);
  assert.equal(window.pcmonaco.getValue('editor'), 'typed while loading');
  assert.equal(created[0].uri, 'file:///editor/second.sql');
});

test('a failed CDN keeps the working editor and unsaved text', async () => {
  const {window, host, scripts} = setup();
  const loading = window.pcmonaco.init('editor', 'original', 'sql');
  host.children[0].value = 'unsaved';
  scripts[0].onerror();
  assert.equal(await loading, true);
  assert.ok(window.pcmonaco.isMounted('editor'));
  assert.equal(window.pcmonaco.getValue('editor'), 'unsaved');
});

test('destroying an editor during loading prevents a late rich-editor mount', async () => {
  const {window, scripts, created} = setup();
  const loading = window.pcmonaco.init('editor', 'original', 'sql');
  window.pcmonaco.destroy('editor');
  scripts[0].onload();
  await loading;
  assert.equal(created.length, 0);
  assert.equal(window.pcmonaco.getValue('editor'), null);
});
