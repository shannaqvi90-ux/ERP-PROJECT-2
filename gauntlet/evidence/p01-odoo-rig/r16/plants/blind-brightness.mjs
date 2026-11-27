import { chromium } from 'playwright-core';
import fs from 'node:fs';
const dir = '/home/shan/evidence-staging/p01-odoo-rig/r16/compare';
const key = JSON.parse(fs.readFileSync(dir + '/key.json'));
const b = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const p = await b.newPage();
const out = { ours: [], odoo: [] };
for (const [f, v] of Object.entries(key.shots)) {
  const data = fs.readFileSync(dir + '/blind/shots/' + f).toString('base64');
  const lum = await p.evaluate(async (src) => { const img = new Image(); img.src = src; await img.decode(); const c = document.createElement('canvas'); c.width = 160; c.height = 90; const x = c.getContext('2d'); x.drawImage(img, 0, 0, 160, 90); const d = x.getImageData(0, 0, 160, 90).data; let s = 0; for (let i = 0; i < d.length; i += 4) s += d[i]; return Math.round(s / (d.length / 4)); }, 'data:image/jpeg;base64,' + data);
  out[v.product].push(lum);
}
for (const [k, l] of Object.entries(out)) console.log(k, l.length, 'shots; mean brightness (0-255) min', Math.min(...l), 'max', Math.max(...l));
await b.close();
