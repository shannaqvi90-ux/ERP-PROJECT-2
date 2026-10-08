import fs from 'node:fs';
import { open, B, OUT, shot } from './common.mjs';
const log = []; const L = (...a) => { const s = a.join(' '); log.push(s); console.log(s); };
const { browser, page, writes } = await open('admin@alnoor.example');
const CID = '018dca08-f085-7542-af56-a574b8de3616';
await page.goto(B + '/tenancy/companies/' + CID);
const f = n => page.locator(`[data-field="${n}"] input`);
await f('phone').waitFor();
// restore data
await f('legalNameEn').click(); await page.keyboard.type('Al Noor General Trading FZE');
await f('taxRegistrationNumber').click(); await page.keyboard.press('Control+a'); await page.keyboard.type('100123456800003');
await page.keyboard.press('Control+s'); await page.waitForTimeout(1200);
L('restored notice', await page.locator('.record-form .notice').innerText().catch(() => 'none'));
// server validation: bad website and TRN letters
await f('website').click(); await page.keyboard.press('Control+a'); await page.keyboard.type('not a url');
await f('taxRegistrationNumber').click(); await page.keyboard.press('Control+a'); await page.keyboard.type('TRN-ABC');
const resp = page.waitForResponse(r => r.request().method() === 'PUT');
await page.keyboard.press('Control+s');
const r = await resp; L('PUT status', r.status(), (await r.text()).slice(0, 400));
await page.waitForTimeout(600);
L('invalid fields', JSON.stringify(await page.locator('[aria-invalid=true]').evaluateAll(es => es.map(e => e.closest('[data-field]')?.getAttribute('data-field') + ' :: ' + (document.getElementById(e.getAttribute('aria-describedby')?.split(' ')[0] || '')?.textContent || '')))));
L('focused', await page.evaluate(() => document.activeElement?.closest('[data-field]')?.getAttribute('data-field')));
L('form alerts', JSON.stringify(await page.locator('.record-form [role=alert]').allInnerTexts()));
await shot(page, '02-form-en-server-validation');
await page.keyboard.press('Alt+z'); await page.waitForTimeout(300);
L('after discard website', await f('website').inputValue(), 'invalid left', await page.locator('[aria-invalid=true]').count());
// next / previous: click button and keys from inside a field
await f('phone').click();
await page.keyboard.press('Alt+PageDown'); await page.waitForTimeout(1000);
L('Alt+PageDown from field ->', page.url(), (await page.locator('.record-header').innerText()).replace(/\n/g, ' | '));
await page.locator('body').click({ position: { x: 600, y: 850 } });
await page.keyboard.press('Alt+PageDown'); await page.waitForTimeout(1000);
L('Alt+PageDown from body ->', page.url(), (await page.locator('.record-header').innerText()).replace(/\n/g, ' | '));
const next = page.getByRole('button', { name: 'Next' });
L('next button count/disabled', await next.count(), await next.isDisabled().catch(() => 'n/a'));
await next.click().catch(e => L('next click err', e.message.split('\n')[0])); await page.waitForTimeout(1000);
L('after Next click ->', page.url(), (await page.locator('.record-header').innerText()).replace(/\n/g, ' | '));
// shortcut sheet
await page.locator('body').click({ position: { x: 600, y: 850 } });
await page.keyboard.press('?'); await page.waitForTimeout(500);
L('shortcut sheet', (await page.locator('[role=dialog]').innerText().catch(() => 'none')).replace(/\n/g, ' | ').slice(0, 1500));
await page.keyboard.press('Escape');
L('writes', JSON.stringify(writes));
fs.writeFileSync(`${OUT}/browser/explore-form-en-2.log`, log.join('\n'));
await browser.close();
