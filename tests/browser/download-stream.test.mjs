import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';

test('stream downloads keep all bytes out of the DOM and release the object URL', async () => {
    const bytes = new TextEncoder().encode('x'.repeat(1_000_000) + '😀tail');
    let blob, revoke, clicked = false, removed = false;
    const link = { click() { clicked = true; }, remove() { removed = true; } };
    const window = {};
    runInNewContext(readFileSync(new URL('../../src/PlaceContext.Host/wwwroot/pcmonaco.js', import.meta.url), 'utf8'), {
        window, Blob,
        document: { createElement: () => link, body: { appendChild() {} } },
        URL: { createObjectURL(value) { blob = value; return 'blob:download'; }, revokeObjectURL(url) { assert.equal(url, 'blob:download'); } },
        setTimeout(callback) { revoke = callback; },
    });
    await window.pcdata.downloadStream('output.log', { arrayBuffer: async () => bytes.buffer });
    assert.equal(link.href, 'blob:download');
    assert.equal(link.download, 'output.log');
    assert.ok(clicked && removed);
    assert.deepEqual(new Uint8Array(await blob.arrayBuffer()), bytes);
    assert.equal(typeof revoke, 'function');
    revoke();
});
