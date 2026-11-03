import { test } from 'node:test';
import assert from 'node:assert/strict';
import { supportsSpeech, startSpeech, stopSpeech, mountChat, unmountChat, mountWorkspaceResize, setComposerMenu } from '../../src/PlaceContext.Host/wwwroot/build-chat.js';

test('workspace divider supports bounded dragging, keyboard resizing and cleanup', () => {
    const divider = new EventTarget();
    const attributes = new Map([['aria-valuenow', '42']]);
    let captured = null, size;
    Object.assign(divider, {
        getAttribute: name => attributes.get(name), setAttribute: (name, value) => attributes.set(name, value),
        setPointerCapture: id => captured = id, hasPointerCapture: id => captured === id,
        releasePointerCapture: () => captured = null, focus: () => {}
    });
    const classes = new Set();
    const root = {
        querySelector: name => name === '.build-divider' ? divider : { getBoundingClientRect: () => ({ left: 200, width: 1000 }) },
        style: { setProperty: (_, value) => size = value },
        classList: { add: name => classes.add(name), remove: name => classes.delete(name) }
    };
    const unmount = mountWorkspaceResize(root);
    const send = (name, values) => { const event = new Event(name, { cancelable: true }); Object.assign(event, values); divider.dispatchEvent(event); return event; };
    send('pointerdown', { pointerId: 1, button: 0 });
    send('pointermove', { pointerId: 2, clientX: 800 });
    assert.equal(size, undefined);
    send('pointermove', { pointerId: 1, clientX: 800 });
    assert.equal(size, '60%');
    send('pointermove', { pointerId: 1, clientX: 2000 });
    assert.equal(size, '70%');
    send('pointercancel', { pointerId: 1 });
    assert.equal(captured, null);
    assert.equal(classes.size, 0);
    assert.equal(send('keydown', { key: 'ArrowLeft' }).defaultPrevented, true);
    assert.equal(size, '68%');
    send('keydown', { key: 'Home' });
    assert.equal(size, '30%');
    assert.equal(attributes.get('aria-valuenow'), '30');
    unmount();
    send('keydown', { key: 'End' });
    assert.equal(size, '30%');
});

test('MCP modal hides and restores its own composer menu', () => {
    const source = {};
    let visible = true;
    const menu = { hidePopover: () => visible = false, showPopover: options => { assert.equal(options.source, source); visible = true; } };
    globalThis.document = { getElementById: id => { assert.equal(id, 'build-test'); return { querySelector: selector => selector === '.composer-menu' ? menu : source }; } };
    setComposerMenu('build-test', false);
    assert.equal(visible, false);
    setComposerMenu('build-test', true);
    assert.equal(visible, true);
});

test('dictation checks support, accepts final text, and releases the microphone', async () => {
    let active;
    class Speech {
        constructor() { active = this; }
        start() { this.started = true; }
        abort() { this.aborted = true; }
    }
    globalThis.window = { isSecureContext: false, SpeechRecognition: Speech };
    assert.equal(supportsSpeech(), false);
    window.isSecureContext = true;
    assert.equal(supportsSpeech(), true);
    const calls = [];
    startSpeech({ invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve(); } });
    assert.equal(active.started, true);
    active.onresult({ resultIndex: 0, results: [Object.assign([{ transcript: 'draft' }], { isFinal: false }), Object.assign([{ transcript: 'build a job' }], { isFinal: true })] });
    assert.deepEqual(calls, [['SpeechText', 'build a job']]);
    active.onerror({ error: 'not-allowed' });
    assert.deepEqual(calls[1], ['SpeechEnded', 'Microphone permission was denied.']);
    assert.equal(active.aborted, true);
    assert.equal(active.onresult, null);
    stopSpeech();
});

test('composer sends Enter, preserves Shift+Enter and composition, and respects disabled send', () => {
    const input = new EventTarget();
    const messages = new EventTarget();
    Object.assign(messages, { scrollHeight: 1000, scrollTop: 0, clientHeight: 200 });
    let submits = 0, enabled = true, changed, disconnected = false;
    input.form = { querySelector: () => enabled, requestSubmit: () => submits++ };
    globalThis.document = { querySelector: selector => selector === '.chat-input' ? input : selector === '.chat-messages' ? messages : null };
    globalThis.MutationObserver = class { constructor(callback) { changed = callback; } observe() {} disconnect() { disconnected = true; } };
    mountChat();
    const press = options => { const event = new Event('keydown', { cancelable: true }); Object.assign(event, { key: 'Enter', ...options }); input.dispatchEvent(event); return event; };
    assert.equal(press({}).defaultPrevented, true);
    assert.equal(submits, 1);
    assert.equal(press({ shiftKey: true }).defaultPrevented, false);
    assert.equal(press({ isComposing: true }).defaultPrevented, false);
    enabled = false;
    press({});
    assert.equal(submits, 1);
    changed([{ target: { nodeType: 3, parentElement: { closest: () => ({}) } } }]);
    assert.equal(messages.scrollTop, 0, 'Monaco redraws must not move the chat');
    const contentChange = [{ target: { nodeType: 1, closest: () => null } }];
    changed(contentChange);
    assert.equal(messages.scrollTop, 1000);
    messages.scrollTop = 20; messages.dispatchEvent(new Event('scroll')); changed(contentChange);
    assert.equal(messages.scrollTop, 20);
    unmountChat();
    assert.equal(disconnected, true);
});

test('a Build blade mounts and unmounts without taking over the page composer', () => {
    const inputs = [new EventTarget(), new EventTarget()];
    const messages = [new EventTarget(), new EventTarget()];
    const submits = [0, 0];
    for (let i = 0; i < 2; i++) inputs[i].form = { querySelector: () => true, requestSubmit: () => submits[i]++ };
    globalThis.document = { getElementById: id => ({ querySelector: selector => selector === '.chat-input' ? inputs[Number(id)] : selector === '.chat-messages' ? messages[Number(id)] : null }) };
    globalThis.MutationObserver = class { observe() {} disconnect() {} };
    mountChat('0'); mountChat('1');
    const enter = input => { const event = new Event('keydown', { cancelable: true }); Object.assign(event, { key: 'Enter' }); input.dispatchEvent(event); };
    enter(inputs[0]); enter(inputs[1]);
    assert.deepEqual(submits, [1, 1]);
    unmountChat('1');
    enter(inputs[0]); enter(inputs[1]);
    assert.deepEqual(submits, [2, 1]);
    unmountChat('0');
});

test('MCP completion finds the token at the caret without treating emails or completed quotes as mentions', async () => {
    const { activeMcpMention } = await import('../../src/PlaceContext.Host/wwwroot/build-chat.js');
    assert.deepEqual(activeMcpMention('Ask @Ware for orders', 9), { start: 4, end: 9 });
    assert.deepEqual(activeMcpMention('@', 1), { start: 0, end: 1 });
    assert.deepEqual(activeMcpMention('@"Commerce M', 12), { start: 0, end: 12 });
    assert.deepEqual(activeMcpMention('user@example.test', 12), { start: -1, end: -1 });
    assert.deepEqual(activeMcpMention('@"Commerce MCP" ', 16), { start: -1, end: -1 });
});

test('typing an MCP mention opens suggestions and Enter selects without submitting the message', async () => {
    const input = new EventTarget(), messages = new EventTarget();
    Object.assign(input, { value: 'Ask @Ware', selectionStart: 9 });
    let submits = 0, selected = 0, visible = true;
    const calls = [];
    const menu = { matches: () => visible, hidePopover: () => visible = false,
        querySelectorAll: () => [{ click: () => selected++, focus() {} }] };
    input.form = { querySelector: () => true, requestSubmit: () => submits++ };
    const root = { querySelector: selector => ({ '.chat-input': input, '.chat-messages': messages, '.composer-menu': menu })[selector] };
    globalThis.document = { getElementById: () => root };
    globalThis.MutationObserver = class { observe() {} disconnect() {} };
    mountChat('mentions', { invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve(); } });
    input.dispatchEvent(new Event('input'));
    await Promise.resolve();
    assert.deepEqual(calls[0], ['UpdateMcpMention', 'Ask @Ware', 4, 9]);
    const press = key => { const event = new Event('keydown', { cancelable: true }); Object.assign(event, { key }); input.dispatchEvent(event); return event; };
    assert.equal(press('Enter').defaultPrevented, true);
    assert.equal(selected, 1);
    assert.equal(submits, 0);
    press('Escape');
    assert.equal(visible, false);
    unmountChat('mentions');
    input.dispatchEvent(new Event('input'));
    assert.equal(calls.length, 2);
});
