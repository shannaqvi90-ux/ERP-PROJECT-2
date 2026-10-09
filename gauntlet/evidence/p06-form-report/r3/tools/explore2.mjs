import { open, B, OUT, shot } from './common.mjs';
const log = [];
const L = (...a) => { const s = a.join(' '); log.push(s); console.log(s); };
const { browser, page, writes } = await open('admin@alnoor.example');
await page.goto(B + '/tenancy/companies');
await page.locator('[role="grid"] [role="row"]').nth(1).waitFor();
// keyboard: focus grid, move down, Enter opens
await page.keyboard.press('Escape');
await page.locator('[role="grid"]').first().focus().catch(() => {});
await page.keyboard.press('ArrowDown');
await page.keyboard.press('ArrowDown');
await page.keyboard.press('Enter');
await page.waitForTimeout(1200);
L('url after Enter', page.url());
L('header', (await page.locator('.record-header').innerText().catch(() => 'no header')).replace(/\n/g, ' | '));
// next/prev
await page.keyboard.press('Alt+PageDown'); await page.waitForTimeout(800);
L('after Alt+PageDown', page.url(), (await page.locator('.record-header').innerText()).replace(/\n/g, ' | '));
await page.keyboard.press('Alt+PageUp'); await page.waitForTimeout(800);
L('after Alt+PageUp', page.url(), (await page.locator('.record-header').innerText()).replace(/\n/g, ' | '));
// sections / tabs
L('sections', JSON.stringify(await page.locator('.record-form fieldset legend, .record-form h2, .record-form h3, [role=tab]').allInnerTexts()));
L('fields', JSON.stringify(await page.locator('.record-form [data-field]').evaluateAll(es => es.map(e => e.getAttribute('data-field') + ':' + (e.querySelector('input,select,textarea')?.tagName || '') + ':' + (e.querySelector('input')?.type || '')))));
// edit phone -> dirty
const phone = page.locator('[data-field="phone"] input');
const orig = await phone.inputValue();
await phone.click(); await page.keyboard.type('+971 4 000 1111');
L('dirty marker', await page.locator('.record-dirty').count());
await page.keyboard.press('Escape'); await page.waitForTimeout(400);
L('Escape dialog', (await page.locator('[role=dialog], [role=alertdialog]').innerText().catch(() => 'none')).replace(/\n/g, ' | '));
await shot(page, '01-form-en-leave-dialog');
await page.keyboard.press('Escape'); await page.waitForTimeout(300);
L('after Escape in dialog: still on form?', page.url(), 'dialog open', await page.locator('[role=dialog], [role=alertdialog]').count());
// next while dirty
await page.keyboard.press('Alt+PageDown'); await page.waitForTimeout(500);
L('Alt+PageDown while dirty -> dialog', (await page.locator('[role=dialog], [role=alertdialog]').innerText().catch(() => 'none')).replace(/\n/g, ' | '), page.url());
if (await page.locator('[role=dialog], [role=alertdialog]').count()) { await page.getByRole('button', { name: /keep/i }).click().catch(() => {}); }
// menu link while dirty
const nw = writes.length;
await page.getByRole('link', { name: 'Branches' }).first().click().catch(e => L('menu click err', e.message.split('\n')[0]));
await page.waitForTimeout(600);
L('menu link while dirty ->', page.url(), 'dialog', (await page.locator('[role=dialog], [role=alertdialog]').innerText().catch(() => 'none')).replace(/\n/g, ' | '), 'native', JSON.stringify(writes.slice(nw)));
if (await page.locator('[role=dialog], [role=alertdialog]').count()) { await page.getByRole('button', { name: /keep/i }).click().catch(() => {}); await page.waitForTimeout(300); }
// Alt+Z discard
await page.keyboard.press('Alt+z'); await page.waitForTimeout(300);
L('after Alt+Z phone', await phone.inputValue(), 'orig', orig, 'dirty', await page.locator('.record-dirty').count());
// server validation: TRN bad
const trn = page.locator('[data-field="taxRegistrationNumber"] input');
const trnOrig = await trn.inputValue();
await trn.click(); await page.keyboard.press('Control+a'); await page.keyboard.type('12345');
await page.keyboard.press('Control+s'); await page.waitForTimeout(1200);
L('after bad TRN save: errors', JSON.stringify(await page.locator('.record-form [role=alert], .record-form .field-error, .record-form .error, [aria-invalid=true]').evaluateAll(es => es.map(e => (e.getAttribute('data-field') || e.name || '') + '=' + e.textContent.trim().slice(0, 150)))));
L('focused', await page.evaluate(() => document.activeElement?.closest('[data-field]')?.getAttribute('data-field')));
await shot(page, '02-form-en-server-validation');
L('writes', JSON.stringify(writes));
await page.keyboard.press('Alt+z'); await page.waitForTimeout(300);
// legal name required: clear and save
const nm = page.locator('[data-field="legalNameEn"] input');
await nm.click(); await page.keyboard.press('Control+a'); await page.keyboard.press('Delete');
await page.keyboard.press('Control+s'); await page.waitForTimeout(1000);
L('empty legal name errors', JSON.stringify(await page.locator('[aria-invalid=true]').evaluateAll(es => es.map(e => e.closest('[data-field]')?.getAttribute('data-field')))), (await page.locator('.record-form').innerText()).match(/required|needs|must[^\n]*/i)?.[0]);
await page.keyboard.press('Alt+z');
// save good phone by Ctrl+Enter
await phone.click(); await page.keyboard.type('+971 4 221 5501');
await page.keyboard.press('Control+Enter'); await page.waitForTimeout(1200);
L('after Ctrl+Enter save notice', (await page.locator('.record-form .notice').innerText().catch(() => 'none')), page.url());
L('writes', JSON.stringify(writes));
// restore
await page.goto(page.url().includes('/tenancy/companies/') ? page.url() : page.url()); 
await page.waitForTimeout(800);
const p2 = page.locator('[data-field="phone"] input');
if (await p2.count()) { await p2.click(); await page.keyboard.press('Control+a'); await page.keyboard.type(orig); await page.keyboard.press('Control+s'); await page.waitForTimeout(1000); L('restored', await p2.inputValue()); }
// print Alt+R
await page.keyboard.press('Alt+r'); await page.waitForTimeout(500);
L('Alt+R menu', JSON.stringify(await page.getByRole('menuitem').allInnerTexts()), JSON.stringify(await page.getByRole('menuitem').evaluateAll(es => es.map(e => e.getAttribute('href')))));
await browser.close();
import fs from 'node:fs'; fs.writeFileSync(`${OUT}/browser/explore-form-en.log`, log.join('\n'));
