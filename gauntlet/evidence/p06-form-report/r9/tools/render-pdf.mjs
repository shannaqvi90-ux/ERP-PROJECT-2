// Critic p06 r9: renders one page of a PDF to JPEG through pdf.js (cdnjs) in Chromium, and prints
// the page count and the text of the chosen pages. Usage: node render-pdf.mjs in.pdf out.jpg|- page [textPages,comma]
import { launch } from '/home/shan/critic/p06-form-report-r9-demo/gauntlet/compare/lib/browser.mjs';
import fs from 'node:fs';
const [,, inp, out, pageNo = '1', textPages = ''] = process.argv;
const b64 = fs.readFileSync(inp).toString('base64');
const browser = await launch();
const page = await browser.newPage({ viewport: { width: 900, height: 1200 } });
await page.setContent(`<html><body style="margin:0;background:#fff"><canvas id=c></canvas>
<script src="https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf.min.js"></script></body></html>`);
await page.waitForFunction(() => window.pdfjsLib);
const info = await page.evaluate(async ({ b64, n, tp }) => {
  pdfjsLib.GlobalWorkerOptions.workerSrc = 'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf.worker.min.js';
  const data = Uint8Array.from(atob(b64), c => c.charCodeAt(0));
  const doc = await pdfjsLib.getDocument({ data }).promise;
  const pick = x => x === 'last' ? doc.numPages : Number(x);
  const texts = {};
  for (const t of tp.split(',').filter(Boolean)) { const pg = await doc.getPage(pick(t)); const tc = await pg.getTextContent(); texts[pick(t)] = tc.items.map(i => i.str).join(' | ').slice(0, 1500); }
  const p = await doc.getPage(pick(n));
  const vp = p.getViewport({ scale: 1.4 });
  const c = document.getElementById('c'); c.width = vp.width; c.height = vp.height;
  await p.render({ canvasContext: c.getContext('2d'), viewport: vp }).promise;
  return { pages: doc.numPages, w: vp.width, h: vp.height, texts };
}, { b64, n: pageNo, tp: textPages });
if (out !== '-') { await page.setViewportSize({ width: Math.ceil(info.w), height: Math.ceil(info.h) }); await page.locator('#c').screenshot({ path: out, type: 'jpeg', quality: 60 }); }
console.log(inp, JSON.stringify({ pages: info.pages, w: info.w, h: info.h }));
for (const [k, v] of Object.entries(info.texts)) console.log(`--- page ${k}: ${v}`);
await browser.close();
