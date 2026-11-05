import {readFileSync} from 'node:fs';
import {runInNewContext} from 'node:vm';
import assert from 'node:assert/strict';
import {test} from 'node:test';

test('agent terminal follows new output, pauses on scrollback, resumes, and cleans up', () => {
  const listeners = {}, observers = [];
  let top = 0;
  const element = {
    scrollHeight: 1000, clientHeight: 200,
    get scrollTop() { return top; },
    set scrollTop(value) { top = Math.max(0, Math.min(value, this.scrollHeight - this.clientHeight)); },
    addEventListener(name, callback) { listeners[name] = callback; },
    removeEventListener(name) { delete listeners[name]; }
  };
  class Observer {
    constructor(callback) { this.callback = callback; observers.push(this); }
    observe() {}
    disconnect() { this.disconnected = true; }
  }
  const context = {MutationObserver: Observer, ResizeObserver: Observer};
  runInNewContext(readFileSync(new URL('../../src/PlaceContext.Host/Components/Shared/WorkflowAgentActivity.razor.js', import.meta.url), 'utf8')
    .replaceAll('export function', 'function') + '\nthis.api = {attach, detach, jumpToLatest};', context);
  context.api.attach(element);
  context.api.attach(element);
  assert.equal(observers.length, 2);
  assert.equal(top, 800);
  element.scrollTop = 100;
  listeners.scroll();
  element.scrollHeight = 1200;
  observers[0].callback();
  assert.equal(top, 100);
  context.api.jumpToLatest(element);
  assert.equal(top, 1000);
  element.scrollHeight = 1300;
  observers[0].callback();
  assert.equal(top, 1100);
  element.clientHeight = 300;
  observers[1].callback();
  assert.equal(top, 1000);
  context.api.detach(element);
  assert.ok(observers.every(observer => observer.disconnected));
  assert.equal(listeners.scroll, undefined);
});
