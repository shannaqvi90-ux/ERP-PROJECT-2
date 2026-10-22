import { launch, signIn, shot, save, OUT } from './pw.mjs';
const b = await launch();
const res = {};
const ctx = await b.newContext({ locale: 'ar-AE', viewport: { width: 1440, height: 900 } });
const p = await ctx.newPage();
await signIn(p, 'admin.ar@alnoor.example');
await p.keyboard.press('g'); await p.keyboard.press('u');
await p.waitForURL('**/identity/users');
await p.locator('.list-row').first().waitFor();
await p.keyboard.press('ArrowDown'); // into rows
await p.keyboard.press('ArrowDown');
await p.keyboard.press('Space'); // select row 2
await p.keyboard.press('ArrowDown');
await p.waitForTimeout(300);
res.active = await p.evaluate(() => document.activeElement?.className);
const rowStyles = async () => p.evaluate(() => [...document.querySelectorAll('.list-row')].slice(0, 4).map(r => ({ cls: r.className, bg: getComputedStyle(r).backgroundColor, sh: getComputedStyle(r).boxShadow })));
res.screenRows = await rowStyles();
await p.emulateMedia({ media: 'print' });
res.printRows = await rowStyles();
res.printVisible = await p.evaluate(() => ({ letterhead: !!document.querySelector('.print-letterhead') && getComputedStyle(document.querySelector('.print-letterhead')).display, topbar: getComputedStyle(document.querySelector('header.topbar')).display, nav: getComputedStyle(document.querySelector('nav.navpane')).display, status: getComputedStyle(document.querySelector('footer.statusbar')).display, checkboxes: [...document.querySelectorAll('.list-row input[type=checkbox]')].slice(0,3).map(c=>getComputedStyle(c).display) }));
await p.screenshot({ path: `${OUT}/06-print-media-users-ar.jpg`, type: 'jpeg', quality: 70 });
await p.pdf({ path: `${OUT}/print-users-ar-keyboard.pdf`, format: 'A4' });
await p.emulateMedia({ media: 'screen' });
// open record and print it
await p.keyboard.press('Enter');
await p.waitForTimeout(1200);
await p.emulateMedia({ media: 'print' });
await p.pdf({ path: `${OUT}/print-user-record-ar.pdf`, format: 'A4' });
await p.screenshot({ path: `${OUT}/07-print-media-record-ar.jpg`, type: 'jpeg', quality: 70 });
save('print2.json', res);
await b.close();
