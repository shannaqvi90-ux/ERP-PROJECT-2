import {start} from './lib.mjs';
import { chromium } from 'playwright';
const E='/home/shan/evidence-staging/p18-visual-design/r1';
const shot=(p,n)=>p.screenshot({path:E+'/'+n, type:'jpeg', quality:70});
const browser = await chromium.launch();
// 1 EN light contacts with contact open
let {ctx,page} = await start({browser, scheme:'light'});
await shot(page,'01-home-en-light.jpg');
await page.keyboard.press('g'); await page.keyboard.press('o'); await page.waitForTimeout(3500);
const cell = page.locator('td').filter({hasText:/LLC|FZE|FZCO/}).first();
await cell.hover(); await page.waitForTimeout(1500);
await shot(page,'02-contacts-en-light-hover-truncated-no-tooltip.jpg');
await page.keyboard.press('Escape'); await page.locator('table[role=grid]').focus();
await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter'); await page.waitForTimeout(2500);
await shot(page,'03-contact-open-en-light-clipped-tab.jpg');
await page.keyboard.press('Control+k'); await page.waitForTimeout(500); await page.keyboard.type('>'); await page.waitForTimeout(1000);
await shot(page,'04-palette-commands-prefix-nothing.jpg');
await page.keyboard.press('Escape');
await ctx.close();
// AR dark
({ctx,page} = await start({browser, scheme:'dark', user:'admin.ar@alnoor.example'}));
await shot(page,'05-home-ar-dark.jpg');
await page.keyboard.press('g'); await page.keyboard.press('o'); await page.waitForTimeout(3500);
await page.locator('table[role=grid]').focus(); await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter'); await page.waitForTimeout(2500);
await shot(page,'06-contact-open-ar-dark.jpg');
await ctx.close();
// HC EN settings
({ctx,page} = await start({browser, scheme:'light'}));
await page.getByRole('button',{name:'High contrast'}).click(); await page.waitForTimeout(300);
await page.keyboard.press('g'); await page.keyboard.press('c'); await page.waitForTimeout(3000);
await page.locator('table[role=grid]').focus(); await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter'); await page.waitForTimeout(2500);
await shot(page,'07-company-form-en-hc-native-selects.jpg');
await page.getByRole('button',{name:'Light'}).click(); await page.waitForTimeout(300);
await page.getByRole('button',{name:/Sign out/}).click(); await page.waitForTimeout(1500);
await shot(page,'08-sign-in-en-light.jpg');
await ctx.close();
// AR sign in light
const c2 = await browser.newContext({viewport:{width:1440,height:900}, colorScheme:'light', locale:'ar'});
const p2 = await c2.newPage(); await p2.goto('http://localhost:21850/'); await p2.waitForTimeout(1000);
await p2.getByText('العربية').first().click().catch(()=>{}); await p2.waitForTimeout(800);
await shot(p2,'09-sign-in-ar-light.jpg');
await c2.close();
// reference product
const c3 = await browser.newContext({viewport:{width:1440,height:900}});
const p3 = await c3.newPage();
await p3.goto('http://localhost:8069/web/login'); await p3.fill('input[name=login]','admin'); await p3.fill('input[name=password]','admin'); await p3.keyboard.press('Enter'); await p3.waitForTimeout(4000);
await p3.goto('http://localhost:8069/odoo/contacts?view_type=list'); await p3.waitForTimeout(6000);
await p3.evaluate(()=>{for(const e of document.querySelectorAll('.o_main_navbar .o_menu_brand, .o_navbar_apps_menu')) e.style.visibility='hidden'});
await p3.screenshot({path:E+'/10-reference-contacts-list.jpg', type:'jpeg', quality:70});
await p3.locator('tr.o_data_row').first().click(); await p3.waitForTimeout(3000);
await p3.screenshot({path:E+'/11-reference-contact-form.jpg', type:'jpeg', quality:70});
await c3.close();
await browser.close();
