// Critic helpers (p18 r3): sign in and measure the screens of ours with playwright-core.
import { chromium } from '/home/shan/critic/p18-visual-design-r3/gauntlet/compare/node_modules/playwright-core/index.mjs';
export const BASE = process.env.OURS || 'http://localhost:21850';
export const OUT = '/home/shan/evidence-staging/p18-visual-design/r3';
export const EXE = '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome';
export async function browser() { return chromium.launch({ executablePath: EXE, headless: true }); }
export async function signIn(page, email, password = 'Demo-Pass-2026') {
  await page.goto(BASE + '/');
  await page.waitForSelector('input[name=email]', { timeout: 30000 });
  await page.fill('input[name=email]', email);
  await page.keyboard.press('Enter');
  await page.waitForSelector('input[name=password]', { timeout: 15000 });
  await page.fill('input[name=password]', password);
  await page.keyboard.press('Enter');
  await page.waitForFunction(() => !document.querySelector('input[name=password]'), null, { timeout: 30000 });
  await page.waitForTimeout(1500);
}
export async function shot(page, name) { await page.screenshot({ path: `${OUT}/${name}.jpg`, type: 'jpeg', quality: 70 }); }
// Text cut by its own box or a container, with no title/tooltip on it or an ancestor within 2 levels
export const CUT = () => {
  const out = [];
  for (const el of document.querySelectorAll('td,th,button,a,[role=tab],label,span,h1,h2,h3,dt,dd,li,input,div.badge,.badge')) {
    const r = el.getBoundingClientRect();
    if (!r.width || r.bottom < 0 || r.top > innerHeight || r.right < 0 || r.left > innerWidth) continue;
    const cs = getComputedStyle(el);
    if (cs.visibility === 'hidden' || cs.display === 'none') continue;
    let cut = false;
    if (el.tagName === 'INPUT') { if (!el.placeholder && !el.value) continue; cut = el.scrollWidth > el.clientWidth + 1; }
    else cut = el.scrollWidth > el.clientWidth + 1 && el.children.length === 0 && (cs.overflow !== 'visible' || cs.textOverflow === 'ellipsis');
    if (!cut) continue;
    const titled = el.title || el.closest('[title]') || el.getAttribute('aria-describedby');
    out.push({ tag: el.tagName, text: (el.value || el.textContent || el.placeholder || '').trim().slice(0, 60), titled: !!titled, title: (el.title || el.closest('[title]')?.title || '').slice(0, 80) });
  }
  return out;
};
