import { test } from 'node:test';
import assert from 'node:assert/strict';
import { supportsSpeech, startSpeech, stopSpeech, mountChat, unmountChat, mountWorkspaceResize, mountDashboardResize, dashboardPositions } from '../../src/PlaceContext.Host/wwwroot/build-chat.js';

test('tile handles preview grid sizes, save on release, cancel safely, and support keyboard resizing', async () => {
    const handlers = new Map(), calls = [], classes = new Set();
    let pointer = null;
    const tile = {
        isConnected: true, dataset: { tile: 'workflow', tileSize: 'square' }, parentElement: { getBoundingClientRect: () => ({ left:0 }) },
        classList: { add() {}, remove() {} },
        style: { removeProperty(name) { delete this[name]; } },
        getBoundingClientRect: () => ({ left:0, width: 210, height: 210 })
    };
    const handle = { closest: selector => selector === '.dashboard-resize' ? handle : tile,
        setPointerCapture: id => pointer = id, hasPointerCapture: id => pointer === id,
        releasePointerCapture: () => pointer = null, focus() {} };
    const root = { addEventListener: (name, handler) => handlers.set(name, handler), removeEventListener: name => handlers.delete(name),
        querySelector: () => null,
        classList: { contains: name => classes.has(name), add: name => classes.add(name), remove: name => classes.delete(name) } };
    globalThis.getComputedStyle = () => ({ columnGap: '16px', rowGap: '16px', gridTemplateColumns: '210px 210px 210px', gridAutoRows: '210px' });
    const unmount = mountDashboardResize(root, { invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve(); } });
    const send = (name, values = {}) => handlers.get(name)?.({ target: handle, pointerId: 1, button: 0, clientX: 200, clientY: 200, preventDefault() {}, ...values });
    send('pointerdown');
    send('pointermove', { pointerId: 2, clientX: 500 });
    assert.equal(tile.style.width, undefined);
    send('pointermove', { clientX: 440, clientY: 450 });
    assert.equal(tile.style.width, '436px');
    assert.equal(tile.style.height, '436px');
    assert.equal(calls.length, 0);
    send('pointerup');
    await Promise.resolve();
    assert.deepEqual(calls, [['ResizeDashboardTileAsync', 'workflow', 'large']]);
    assert.equal(tile.style.width, undefined);
    assert.equal(pointer, null);
    send('pointerdown'); send('pointermove', { clientX: 5000 }); send('pointercancel');
    assert.equal(calls.length, 1);
    assert.equal(tile.style.width, undefined);
    send('keydown', { key: 'ArrowDown' });
    await Promise.resolve();
    assert.deepEqual(calls[1], ['ResizeDashboardTileAsync', 'workflow', 'tall']);
    send('pointerdown'); send('pointermove', { clientX: 500 }); unmount();
    assert.equal(tile.style.width, undefined);
    assert.equal(handlers.size, 0);
    assert.equal(classes.size, 0);
    delete globalThis.getComputedStyle;
});

test('workspace divider supports bounded dragging, keyboard resizing and cleanup', () => {
    const divider = new EventTarget();
    const attributes = new Map([['aria-valuenow', '50']]);
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
    input.value = '@Warehouse ask user@example.test';
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

test('dashboard cells stack beside a tall tile and resolve collisions without overlap', () => {
    const items = [
        { key: 'tall', width: 1, height: 2 },
        { key: 'top', width: 1, height: 1 },
        { key: 'bottom', width: 1, height: 1 },
    ];
    const cells = dashboardPositions(items, 2, { key: 'bottom', column: 2, row: 2 });
    assert.deepEqual(cells.get('tall'), { column: 1, row: 1 });
    assert.deepEqual(cells.get('top'), { column: 2, row: 1 });
    assert.deepEqual(cells.get('bottom'), { column: 2, row: 2 });
    const resized = dashboardPositions(items.map(item => ({ ...item, ...cells.get(item.key), width: item.key === 'top' ? 2 : 1 })), 2);
    assert.deepEqual(resized.get('top'), { column: 1, row: 3 });
    const mobile = dashboardPositions(items.map(item => ({ ...item, ...cells.get(item.key) })), 1);
    assert.equal(new Set([...mobile.values()].map(p => p.row)).size, 3);
    assert.ok([...mobile.values()].every(p => p.column === 1));
});
