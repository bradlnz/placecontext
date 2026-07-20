import { chromium } from 'playwright';

const browser = await chromium.launch({ headless: true });
const page = await browser.newPage();

const errors = [];
const consoleMessages = [];
page.on('console', msg => {
    consoleMessages.push({ type: msg.type(), text: msg.text() });
});
page.on('pageerror', err => errors.push(err.message));
page.on('response', resp => {
    if (resp.status() >= 400) {
        consoleMessages.push({ type: 'response-error', text: `${resp.status()} ${resp.url()}` });
    }
});

try {
    await page.goto('http://100.81.205.22/login', { waitUntil: 'networkidle', timeout: 15000 });
} catch (e) {
    console.log('Navigation error:', e.message);
}

await page.waitForTimeout(3000);

await page.screenshot({ path: '/home/brad/code/devcontext/scratchpad/login-screenshot.png', fullPage: true });

console.log('\n=== Console Messages ===');
for (const m of consoleMessages) {
    console.log(`[${m.type}] ${m.text}`);
}
console.log('\n=== Page Errors ===');
for (const e of errors) {
    console.log(e);
}

const content = await page.content();
console.log('\n=== Page HTML (first 3000 chars) ===');
console.log(content.substring(0, 3000));

const url = page.url();
console.log('\n=== Final URL ===');
console.log(url);

await browser.close();
