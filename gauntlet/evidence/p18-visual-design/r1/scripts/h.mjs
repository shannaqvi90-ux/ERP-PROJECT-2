import {start,out} from './lib.mjs';
const {browser,page} = await start({scheme:'light'});
await page.keyboard.press('Control+k'); await page.waitForTimeout(600);
await page.keyboard.type('dark'); await page.waitForTimeout(1200);
console.log('dark:', (await page.locator('[role=dialog], [role=listbox]').first().innerText()).slice(0,500).replace(/\n/g,' | '));
await page.keyboard.press('Enter'); await page.waitForTimeout(500);
console.log('theme now', await page.evaluate(()=>document.documentElement.dataset.theme));
await page.keyboard.press('Control+k'); await page.waitForTimeout(500);
await page.keyboard.type('>'); await page.waitForTimeout(1000);
console.log('> :', (await page.locator('[role=dialog]').first().innerText()).slice(0,600).replace(/\n/g,' | '));
await page.keyboard.press('Control+a'); await page.keyboard.type('#'); await page.waitForTimeout(1000);
console.log('# :', (await page.locator('[role=dialog]').first().innerText()).slice(0,300).replace(/\n/g,' | '));
await page.keyboard.press('Control+a'); await page.keyboard.type('Al Falasi'); await page.waitForTimeout(2500);
console.log('rec:', (await page.locator('[role=dialog]').first().innerText()).slice(0,400).replace(/\n/g,' | '));
await page.keyboard.press('Tab'); await page.waitForTimeout(800);
console.log('tab:', (await page.locator('[role=dialog]').first().innerText()).slice(0,400).replace(/\n/g,' | '));
await page.screenshot({path:out+'/palette-dark.png'});
await page.keyboard.press('Escape');
// density
await page.getByRole('button',{name:'Comfortable'}).click(); await page.waitForTimeout(300);
console.log('density', await page.evaluate(()=>[document.documentElement.dataset.density, localStorage.getItem('erp.density')]));
// sign out and sign in as other user in same context: theme kept?
await page.getByRole('button',{name:/Sign out/}).click(); await page.waitForTimeout(1500);
console.log('after signout', await page.evaluate(()=>[document.documentElement.dataset.theme, localStorage.getItem('erp.theme')]));
await browser.close();
