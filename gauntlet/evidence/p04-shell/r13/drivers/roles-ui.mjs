import { launch, signIn, save } from './pw.mjs';
const b = await launch();
const res = {}; const r0 = {}; res.r0 = r0;
for (const email of ['viewer@alnoor.example', 'noaccess@alnoor.example']) {
  const ctx = await b.newContext({ locale: 'en-US', viewport: { width: 1440, height: 900 } });
  const p = await ctx.newPage();
  const writes = []; p.on('request', r => { if (!['GET', 'HEAD'].includes(r.method())) writes.push(r.method() + ' ' + new URL(r.url()).pathname); });
  await p.goto('http://localhost:20450/'); await p.locator('input[name=email]').fill(email); await p.keyboard.press('Tab'); await p.keyboard.type('Demo-Pass-2026'); await p.keyboard.press('Enter'); await p.waitForTimeout(3000); r0[email] = await p.evaluate(() => document.body.innerText.slice(0, 300));
  const r = {};
  r.nav = await p.evaluate(() => [...document.querySelectorAll('nav.navpane a')].map(a => a.textContent.trim()));
  await p.keyboard.press('Control+k'); await p.waitForTimeout(300); await p.keyboard.type('new'); await p.waitForTimeout(800);
  r.paletteNew = await p.evaluate(() => [...document.querySelectorAll('[role=option]')].map(o => o.textContent.trim()));
  await p.keyboard.press('Escape');
  await p.keyboard.press('Alt+l'); await p.waitForTimeout(1200);
  r.dirAfterAltL = await p.evaluate(() => document.documentElement.dir);
  r.status = await p.evaluate(() => [...document.querySelectorAll('[role=status]')].map(e => e.textContent.trim()).filter(Boolean));
  await p.keyboard.press('Alt+l'); await p.waitForTimeout(800);
  await p.goto('http://localhost:20450/identity/users/new'); await p.waitForTimeout(1500);
  r.deepLinkNew = await p.evaluate(() => document.querySelector('main').innerText.slice(0, 200));
  r.writes = writes;
  res[email] = r;
  await ctx.close();
}
save('roles-ui.json', res);
await b.close();
