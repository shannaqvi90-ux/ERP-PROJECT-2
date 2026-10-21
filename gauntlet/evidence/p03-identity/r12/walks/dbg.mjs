import { open, BASE, focused } from './h.mjs';
const { browser, page } = await open();
await page.goto(BASE + '/'); await page.waitForTimeout(1500);
console.log((await page.locator('body').innerText()).slice(0,600));
console.log(await page.locator('input').evaluateAll(es=>es.map(e=>e.outerHTML.slice(0,150))));
console.log(await focused(page));
await browser.close();
