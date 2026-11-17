import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';

for (const stored of [null, 'dark', 'light', 'invalid', 'unavailable']) {
    test(`theme initializes and toggles with stored preference: ${stored}`, () => {
        const shell = { dataset: {} };
        const document = {
            body: { style: {} },
            documentElement: { dataset: {}, style: {}, classList: { remove() {} } },
            getElementById: () => shell,
            querySelectorAll: () => [],
            addEventListener() {},
        };
        let saved = stored;
        const window = { matchMedia: () => ({ addEventListener() {} }) };
        const expected = stored === 'light' ? 'light' : 'dark';
        const bootstrap = readFileSync(new URL('../../src/PlaceContext.Host/Components/App.razor', import.meta.url), 'utf8').match(/<script>([\s\S]*?)<\/script>/)[1];
        runInNewContext(bootstrap, {
            document,
            localStorage: { getItem() {
                if (stored === 'unavailable') throw new Error('Storage disabled');
                return saved;
            } },
        });
        assert.equal(document.documentElement.dataset.pcTheme, expected);
        runInNewContext(readFileSync(new URL('../../src/PlaceContext.Host/wwwroot/placecontext.js', import.meta.url), 'utf8'), {
            window, document,
            Element: class {},
            MutationObserver: class { observe() {} },
            localStorage: {
                getItem() {
                    if (stored === 'unavailable') throw new Error('Storage disabled');
                    return saved;
                },
                setItem(key, value) { saved = value; },
            },
        });
        assert.equal(window.placecontext.initTheme(), expected);
        assert.equal(shell.dataset.theme, expected);
        assert.equal(document.documentElement.dataset.pcTheme, expected);
        assert.equal(document.documentElement.style.colorScheme, expected);
        assert.equal(document.body.style.background, expected === 'light' ? '#ffffff' : '#000000');
        assert.equal(saved, expected);
        const toggled = expected === 'dark' ? 'light' : 'dark';
        assert.equal(window.placecontext.toggleTheme(), toggled);
        assert.equal(saved, toggled);
        if (stored !== 'unavailable') assert.equal(window.placecontext.initTheme(), toggled);
    });
}
