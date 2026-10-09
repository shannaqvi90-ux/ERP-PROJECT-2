// A shared device where someone once used a passkey (erp.passkeyOffer=1), and a second person
// without one on it arrives at the plain sign-in address.
import { chromium } from '/home/shan/critic/p00-foundation-r8/gauntlet/compare/node_modules/playwright-core/index.mjs';
import fs from 'node:fs';
const BASE = 'http://localhost:20050';
const b = await chromium.launch({ executablePath: '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const notes = [];
const note = (...a) => { const s = a.join(' '); notes.push(s); console.log(s); };
const ctx = await b.newContext({ locale: 'en-US', storageState: { cookies: [], origins: [{ origin: BASE, localStorage: [{ name: 'erp.passkeyOffer', value: '1' }] }] } });
const page = await ctx.newPage();
const cdp = await ctx.newCDPSession(page);
await cdp.send('WebAuthn.enable');
// The platform authenticator holds no passkey of this person; it never confirms (the person would cancel the system prompt).
await cdp.send('WebAuthn.addVirtualAuthenticator', { options: { protocol: 'ctap2', transport: 'internal', hasResidentKey: true, hasUserVerification: true, isUserVerified: false, automaticPresenceSimulation: false } });
await page.goto(BASE + '/');
await page.waitForSelector('input[name="email"]');
await page.waitForTimeout(1500);
const state = async () => page.evaluate(() => ({ asking: !!document.querySelector('#passkey-asking'), askingText: document.querySelector('#passkey-asking')?.textContent?.trim().slice(0, 120), focus: document.activeElement?.getAttribute('name') ?? document.activeElement?.tagName, emailDisabled: document.querySelector('input[name="email"]')?.disabled }));
note('arrival (1.5 s):', JSON.stringify(await state()));
// Can the person just type their e-mail while the device is being asked?
await page.keyboard.type('admin@alnoor.example');
note('after typing e-mail while asking:', JSON.stringify(await state()), 'email value:', await page.locator('input[name="email"]').inputValue());
await page.keyboard.press('Enter');
await page.keyboard.type('Demo-Pass-2026');
await page.keyboard.press('Enter');
const ok = await page.waitForSelector('#navpane', { state: 'attached', timeout: 15000 }).then(() => true).catch(() => false);
note('signed in with e-mail, Enter, password, Enter while the passkey request was open:', ok);
await page.screenshot({ path: '/home/shan/evidence-staging/p00-foundation/r8/attack/shared-device.png' });
fs.writeFileSync('/home/shan/evidence-staging/p00-foundation/r8/attack/shared-device.txt', notes.join('\n') + '\n');
await b.close();
