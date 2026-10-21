import { chromium } from 'playwright-core';
import fs from 'node:fs';
const root = process.env.HOME + '/.cache/ms-playwright';
const dir = fs.readdirSync(root).filter(d => /^chromium-\d+$/.test(d)).sort().pop();
export const BASE = process.env.BASE || 'http://localhost:20350';
export const SHOTS = '/home/shan/evidence-staging/p03-identity/r13';
export async function launch() { return chromium.launch({ executablePath: `${root}/${dir}/chrome-linux64/chrome`, headless: true }); }
export function logger(name) { const out = []; const L = (s) => { out.push(String(s)); console.log(String(s)); }; L.save = () => fs.writeFileSync(new URL(`./${name}.txt`, import.meta.url), out.join('\n')); return L; }
export async function signIn(page, email, password = 'Demo-Pass-2026') {
  await page.goto(BASE + '/');
  await page.getByLabel(/e-mail|البريد/i).first().fill(email);
  await page.getByLabel(/password|كلمة المرور/i).first().fill(password);
  await page.keyboard.press('Enter');
}
export async function shot(page, file) { await page.screenshot({ path: `${SHOTS}/${file}`, type: 'jpeg', quality: 70 }); }
