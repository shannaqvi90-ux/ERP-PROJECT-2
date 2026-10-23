import { launch, signIn, save } from './pw.mjs';
import fs from 'node:fs';
const axe = fs.readFileSync('/tmp/claude-1000/-home-shan-ERP-PROJECT-2/cf74e302-10e7-40a3-8d55-b932b1d7e117/scratchpad/axe13/node_modules/axe-core/axe.min.js', 'utf8');
const b = await launch();
const res = {};
for (const [lang, email] of [['en', 'admin@alnoor.example'], ['ar', 'admin.ar@alnoor.example']]) {
  const ctx = await b.newContext({ locale: lang === 'ar' ? 'ar-AE' : 'en-US', bypassCSP: true, viewport: { width: 1440, height: 900 } });
  const p = await ctx.newPage();
  const run = async (name) => { await p.addScriptTag({ content: axe }); const r = await p.evaluate(async () => { const o = await axe.run(document, { runOnly: ['wcag2a', 'wcag2aa'] }); return o.violations.map(v => ({ id: v.id, impact: v.impact, n: v.nodes.length, sample: v.nodes.slice(0, 3).map(n => n.target.join(' ') + ' :: ' + (n.failureSummary || '').slice(0, 200)) })); }); res[`${lang}-${name}`] = r; };
  await p.goto('http://localhost:20450/'); await p.locator('input[name=email]').waitFor(); await run('signin');
  await signIn(p, email); await run('home');
  await p.keyboard.press('g'); await p.keyboard.press('u'); await p.waitForURL('**/identity/users'); await p.locator('.list-row').first().waitFor(); await run('users');
  await p.keyboard.press('ArrowDown'); await p.keyboard.press('Enter'); await p.waitForTimeout(1200); await run('user-record');
  await p.keyboard.press('Control+k'); await p.waitForTimeout(400); await p.keyboard.type('rol'); await p.waitForTimeout(500); await run('palette');
  await p.keyboard.press('Escape'); await p.keyboard.press('Control+/'); await p.waitForTimeout(400); await run('help');
  await ctx.close();
}
save('axe.json', res);
await b.close();
