import { test } from 'node:test';
import assert from 'node:assert/strict';
import { supportsSpeech, startSpeech, stopSpeech, mountChat, unmountChat } from '../../src/PlaceContext.Host/wwwroot/build-chat.js';

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
    globalThis.document = { querySelector: selector => selector === '.chat-input' ? input : messages };
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
    changed();
    assert.equal(messages.scrollTop, 1000);
    messages.scrollTop = 20; messages.dispatchEvent(new Event('scroll')); changed();
    assert.equal(messages.scrollTop, 20);
    unmountChat();
    assert.equal(disconnected, true);
});

test('a Build blade mounts and unmounts without taking over the page composer', () => {
    const inputs = [new EventTarget(), new EventTarget()];
    const messages = [new EventTarget(), new EventTarget()];
    const submits = [0, 0];
    for (let i = 0; i < 2; i++) inputs[i].form = { querySelector: () => true, requestSubmit: () => submits[i]++ };
    globalThis.document = { getElementById: id => ({ querySelector: selector => selector === '.chat-input' ? inputs[Number(id)] : messages[Number(id)] }) };
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
