import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';

function setup(manualFrames = false) {
  const frames = [];
  const scripts = [], created = [];
  const host = { classList: {add() {}, remove() {}}, setAttribute(name,value) { this[name]=value; }, removeAttribute(name) { delete this[name]; }, children: [], appendChild(child) { this.children.push(child); }, contains(child) { return this.children.includes(child); } };
  const document = {
    activeElement: null,
    getElementById: id => id === 'editor' ? host : null,
    createElement: () => ({ dataset: {}, style: {}, setAttribute(name, value) { this[name] = value; },
      focus() {}, remove() { host.children = host.children.filter(child => child !== this); } }),
    head: { appendChild(script) { scripts.push(script); } },
  };
  const monaco = {
    Uri: { parse: value => value },
    languages: { registerCompletionItemProvider() {} },
    editor: {
      colorize: async () => '',
      colorizeModelLine() {},
      createModel(value, language, uri) {
        const model = { getValue: () => value, getLanguageId: () => language, getPositionAt: n => ({lineNumber:1,column:n+1}),
          getFullModelRange: () => ({ startLineNumber: 1, startColumn: 1, endLineNumber: 1, endColumn: value.length + 1 }), dispose() {} };
        created.push({value,language,uri});
        return model;
      },
      create(el, {model}) {
        let value = model.getValue();
        return {
          layout() {}, render() {},
          getVisibleRanges: () => [{ endLineNumber: 1 }],
          getModel: () => model,
          getValue: () => value,
          setValue(next) { value = next; },
          getSelection: () => ({ startLineNumber: 1, startColumn: 1, endLineNumber: 1, endColumn: 1 }),
          executeEdits(_source, edits) { value = (value || '') + edits[0].text; },
          setSelection() {},
          focus() {},
          dispose() {},
        };
      },
    },
  };
  const window = { require(dependencies, loaded) { window.monaco = monaco; loaded(); } };
  window.require.config = () => {};
  runInNewContext(readFileSync(new URL('../../src/PlaceContext.Host/wwwroot/pcmonaco.js', import.meta.url), 'utf8'), {
    window, document, console: { warn() {} }, setTimeout, clearTimeout, requestAnimationFrame: callback => manualFrames ? frames.push(callback) : setTimeout(callback, 0),
  });
  return { window, document, host, scripts, created, frames, monaco };
}

test('skeleton covers loading while preserving values and the current tab when Monaco arrives', async () => {
  const {window, document, host, scripts, created} = setup();
  const loading = window.pcmonaco.init('editor', 'original', 'sql', null, 'first.sql');
  assert.ok(window.pcmonaco.isMounted('editor'));
  assert.equal(window.pcmonaco.getValue('editor'), 'original');
  assert.equal(host.children[0].hidden, true);
  assert.equal(host['aria-busy'], 'true');
  assert.equal(host.children[1].className, 'pcmonaco-loading');
  window.pcmonaco.openFile('editor', 'second.sql', 'second', 'sql');
  window.pcmonaco.setValue('editor', 'updated while loading', 'sql');
  host.children[0].selectionStart = host.children[0].selectionEnd = 5;
  document.activeElement = host.children[0];
  scripts[0].onload();
  assert.equal(await loading, true);
  assert.equal(window.pcmonaco.getValue('editor'), 'updated while loading');
  assert.equal(created[0].uri, 'file:///editor/second.sql');
  assert.equal(host['aria-busy'], undefined);
  assert(!host.children.some(n=>n.className==='pcmonaco-loading'));
});

test('a failed CDN keeps the working editor and unsaved text', async () => {
  const {window, host, scripts} = setup();
  const loading = window.pcmonaco.init('editor', 'original', 'sql');
  host.children[0].value = 'unsaved';
  scripts[0].onerror();
  assert.equal(await loading, true);
  assert.ok(window.pcmonaco.isMounted('editor'));
  assert.equal(window.pcmonaco.getValue('editor'), 'unsaved');
  assert.equal(host.children[0].hidden, false);
  assert.equal(host['aria-busy'], undefined);
});

test('editor stays covered until its language tokenizer is ready', async () => {
  const {window, host, scripts, created, monaco} = setup();
  let ready;
  monaco.editor.colorize = () => new Promise(resolve => { ready = resolve; });
  const loading = window.pcmonaco.init('editor', 'version: 1', 'yaml');
  scripts[0].onload();
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(host['aria-busy'], 'true');
  assert.equal(created.length, 1);
  window.pcmonaco.setValue('editor', 'version: 2', 'yaml');
  ready('');
  await loading;
  assert.equal(window.pcmonaco.getValue('editor'), 'version: 2');
  assert.equal(host['aria-busy'], undefined);
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

test('insertText edits the plain fallback at the caret while Monaco loads', async () => {
  const {window, host, scripts} = setup();
  const loading = window.pcmonaco.init('editor', 'SELECT  FROM listings', 'sql');
  host.children[0].selectionStart = host.children[0].selectionEnd = 7;
  assert.equal(window.pcmonaco.insertText('editor', '*'), 'SELECT * FROM listings');
  assert.equal(window.pcmonaco.getValue('editor'), 'SELECT * FROM listings');
  scripts[0].onload();
  await loading;
  assert.equal(window.pcmonaco.getValue('editor'), 'SELECT * FROM listings');
});

test('insertText edits a mounted Monaco editor and returns the new text', async () => {
  const {window, scripts} = setup();
  const loading = window.pcmonaco.init('editor', 'SELECT ', 'sql');
  scripts[0].onload();
  await loading;
  assert.equal(window.pcmonaco.insertText('editor', '*'), 'SELECT *');
  assert.equal(window.pcmonaco.getValue('editor'), 'SELECT *');
});

 test('skeleton remains through Monaco creation until its first rendered frame', async () => {
  const {window, host, scripts, frames}=setup(true);
  const loading=window.pcmonaco.init('editor','const ready = true;','javascript');
  scripts[0].onload();await new Promise(resolve=>setImmediate(resolve));
  assert.equal(host['aria-busy'],'true');
  frames.shift()();assert.equal(host['aria-busy'],'true');
  frames.shift()();await loading;
  assert.equal(host['aria-busy'],undefined);
 });

test('closing after Monaco creation clears the skeleton and prevents a late mount', async () => {
 const {window,host,scripts,frames}=setup(true);
 const loading=window.pcmonaco.init('editor','original','sql');
 scripts[0].onload();await new Promise(resolve=>setImmediate(resolve));
 window.pcmonaco.destroy('editor');
 assert.equal(host['aria-busy'],undefined);
 frames.shift()();frames.shift()();await loading;
 assert.equal(window.pcmonaco.getValue('editor'),null);
 assert(!host.children.some(n=>n.className==='pcmonaco-loading'));
});
