import { browser, signedIn } from './lib.mjs';
const b = await browser(); const p = await signedIn(b, 'admin@alnoor.example');
console.log(p.url());
console.log((await p.locator('a').evaluateAll(es => es.map(e => `${e.getAttribute('href')} | ${e.textContent.trim()}`))).join('\n'));
console.log((await p.locator('header button, [role=banner] button').evaluateAll(es => es.map(e => `${e.getAttribute('aria-label')} | ${e.textContent.trim()} | ${e.getAttribute('aria-keyshortcuts')}`))).join('\n'));
await p.screenshot({ path: 'x-home.jpg', type: 'jpeg', quality: 60 });
await b.close();
