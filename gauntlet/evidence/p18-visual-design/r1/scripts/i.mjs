import {start,out,B} from './lib.mjs';
let {browser,page} = await start({user:'admin@alnoor.example'});
console.log('A home:', (await page.locator('.home-cards').innerText()).replace(/\n/g,' | '));
await page.getByRole('button',{name:/Sign out/}).click(); await page.waitForTimeout(1500);
await page.keyboard.type('admin@gulfsteel.example'); await page.keyboard.press('Enter'); await page.waitForTimeout(800);
await page.keyboard.type('Demo-Pass-2026'); await page.keyboard.press('Enter'); await page.waitForTimeout(3500);
console.log('B home:', (await page.locator('.home-cards').innerText()).replace(/\n/g,' | '));
// B tries A's contact via API with B's session
const r = await page.evaluate(async()=>{const x=await fetch('/api/contacts/contacts/0188c33d-2f78-70c9-9cc0-fbadd0e7e213',{headers:{'X-Erp-Request':'1'}}); return x.status});
console.log('B fetch A contact status', r);
await browser.close();
// viewer
({browser,page} = await start({user:'viewer@alnoor.example'}));
console.log('viewer home:', (await page.locator('main').innerText()).replace(/\n/g,' | ').slice(0,600));
await browser.close();
({browser,page} = await start({user:'noaccess@alnoor.example'}));
console.log('noaccess home:', (await page.locator('main').innerText()).replace(/\n/g,' | ').slice(0,300));
await browser.close();
