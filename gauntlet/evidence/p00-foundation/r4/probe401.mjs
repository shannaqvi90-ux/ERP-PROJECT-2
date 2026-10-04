import { chromium } from '/home/user/critic/p00-foundation-r4/gauntlet/compare/node_modules/playwright-core/index.mjs';
const b = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
const p = await (await b.newContext()).newPage();
p.on('response', r => { if (r.status() >= 400) console.log('resp', r.status(), r.request().method(), r.url()); });
p.on('console', m => m.type() === 'error' && console.log('console', m.text()));
await p.goto('http://localhost:20050/'); await p.waitForLoadState('networkidle');
await b.close();
