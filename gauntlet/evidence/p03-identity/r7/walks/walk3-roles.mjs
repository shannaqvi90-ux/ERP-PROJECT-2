// Critic p03 r7 walk 3: roles: new role from the keyboard, matrix search and bulk toggles, copy, delete.
import { open, signIn, BASE, shot, focused } from './pw.mjs';
const T = Math.random().toString(16).slice(2, 8);
const { browser, page, errors } = await open(1600, 900);
await signIn(page, 'admin@alnoor.example');
await page.goto(BASE + '/identity/roles'); await page.waitForLoadState('networkidle'); await page.waitForTimeout(800);
await page.keyboard.press('Alt+n'); await page.waitForTimeout(1000);
console.log('focused after Alt+N:', await focused(page));
await page.keyboard.type(`Clerk ${T}`); await page.keyboard.press('Tab'); await page.keyboard.type(`كاتب ${T}`);
const panel = page.locator('aside, [role=complementary], [role=dialog]').last();
const txt = await panel.innerText(); console.log('NEW ROLE PANEL:\n' + txt.slice(0, 1600));
const search = panel.getByRole('searchbox').or(panel.getByPlaceholder(/permission/i)).first();
console.log('permission search count', await search.count());
await search.fill('view'); await page.waitForTimeout(600);
const buttons = await panel.getByRole('button').allInnerTexts(); console.log('panel buttons:', JSON.stringify(buttons));
const selAll = panel.getByRole('button', { name: /select all shown|all shown|tick all|select all/i }).first();
if (await selAll.count()) await selAll.click();
await page.waitForTimeout(500);
const checked = await panel.locator('input[type=checkbox]:checked').evaluateAll(els => els.map(e => e.getAttribute('aria-label') || e.closest('label')?.textContent?.trim() || e.name || e.value));
console.log('checked after view + select all:', checked.length, JSON.stringify(checked));
await shot(page, '05-new-role-matrix-view-selected-en');
const clippedHeads = await page.evaluate(() => [...document.querySelectorAll('aside th, [role=dialog] th, aside caption')].filter(e => e.scrollWidth > e.clientWidth + 1).map(e => e.textContent.trim().slice(0, 30)));
console.log('clipped matrix headers:', JSON.stringify(clippedHeads));
await page.keyboard.press('Control+Enter'); await page.waitForTimeout(1500);
console.log('after save:\n' + (await panel.innerText()).slice(0, 300));
// copy and delete from the list
await page.goto(BASE + '/identity/roles'); await page.waitForLoadState('networkidle'); await page.waitForTimeout(800);
await page.keyboard.type(`Clerk ${T}`); await page.waitForTimeout(1000); await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter'); await page.waitForTimeout(1200);
const p = page.locator('aside, [role=complementary], [role=dialog]').last();
console.log('role panel buttons:', JSON.stringify(await p.getByRole('button').allInnerTexts()));
const copy = p.getByRole('button', { name: /copy/i }).first();
if (await copy.count()) { await copy.click(); await page.waitForTimeout(1000); console.log('copy dialog:\n' + (await page.locator('[role=dialog], aside').last().innerText()).slice(0, 500)); await page.keyboard.press('Control+Enter'); await page.waitForTimeout(1200); console.log('after copy:', (await page.locator('body').innerText()).match(/Copy of[^\n]*|نسخة[^\n]*/)?.[0]); }
await page.goto(BASE + '/identity/roles'); await page.waitForLoadState('networkidle'); await page.waitForTimeout(800);
await page.keyboard.type(`Clerk ${T}`); await page.waitForTimeout(1000);
console.log('roles matching after copy:\n' + (await page.locator('main').innerText()).slice(0, 600));
await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter'); await page.waitForTimeout(1200);
const del = page.locator('aside, [role=complementary], [role=dialog]').last().getByRole('button', { name: /delete/i }).first();
if (await del.count()) { await del.click(); await page.waitForTimeout(700); console.log('delete confirm:\n' + (await page.locator('[role=dialog], [role=alertdialog]').last().innerText().catch(() => 'none')).slice(0, 300)); await page.keyboard.press('Enter'); await page.waitForTimeout(1200); }
await page.goto(BASE + '/identity/roles'); await page.waitForLoadState('networkidle'); await page.waitForTimeout(800);
await page.keyboard.type(`Clerk ${T}`); await page.waitForTimeout(1000);
console.log('roles matching after delete:\n' + (await page.locator('main').innerText()).slice(0, 500));
console.log('errors', errors);
await browser.close();
