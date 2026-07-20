import { chromium } from 'playwright';

const browser = await chromium.launch({ headless: true });
const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });

const allResponses = [];
page.on('response', res => {
  if (res.status() >= 400) {
    allResponses.push({ url: res.url(), status: res.status(), statusText: res.statusText() });
  }
});

page.on('console', msg => {
  if (msg.type() === 'error') console.log(`CONSOLE ERROR: ${msg.text()}`);
});
page.on('pageerror', err => console.log(`PAGE ERROR: ${err.message}`));

const response = await page.goto('http://100.81.205.22/login', { waitUntil: 'networkidle', timeout: 30000 });
console.log(`Main page status: ${response.status()}`);

await page.waitForTimeout(5000);

console.log(`\n--- Non-2xx responses ---`);
allResponses.forEach(r => console.log(`  ${r.status} ${r.url}`));

// Try fetching specific framework resources to see which 401
const fetches = [
  '/_framework/blazor.webassembly.js',
  '/_framework/blazor.boot.json',
  '/_framework/dotnet.native.wasm',
  '/_framework/blazor.web.js',
  '/_framework/blazor-server.js',
];
for (const path of fetches) {
  const resp = await page.evaluate(async (p) => {
    try {
      const r = await fetch(p);
      return { status: r.status, ok: r.ok, url: p };
    } catch (e) {
      return { error: e.message, url: p };
    }
  }, path);
  console.log(`Fetch ${path}: ${JSON.stringify(resp)}`);
}

await browser.close();
