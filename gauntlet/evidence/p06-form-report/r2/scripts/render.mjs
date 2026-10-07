import http from 'node:http'; import fs from 'node:fs'; import path from 'node:path';
import { chromium } from 'playwright-core';
const [,, file, out, pageNo, scale] = process.argv;
const html = `<!doctype html><html><body style="margin:0;background:#888"><div id=out></div><script type="module">
import * as pdfjs from '/pdf.mjs'; pdfjs.GlobalWorkerOptions.workerSrc = '/pdf.worker.mjs';
const doc = await pdfjs.getDocument({ url: '/file.pdf' }).promise; const page = await doc.getPage(${Number(pageNo || 1)});
const vp = page.getViewport({ scale: ${Number(scale || 1.4)} }); const c = document.createElement('canvas'); c.width = vp.width; c.height = vp.height; document.getElementById('out').appendChild(c);
await page.render({ canvasContext: c.getContext('2d'), viewport: vp }).promise; document.title = 'done ' + doc.numPages;</script></body></html>`;
const dist = path.resolve('node_modules/pdfjs-dist/build');
const srv = http.createServer((q, r) => {
  if (q.url === '/') { r.setHeader('content-type', 'text/html'); return r.end(html); }
  if (q.url === '/file.pdf') { r.setHeader('content-type', 'application/pdf'); return r.end(fs.readFileSync(file)); }
  const f = path.join(dist, q.url.slice(1)); if (!fs.existsSync(f)) { r.statusCode = 404; return r.end(); } r.setHeader('content-type', 'text/javascript'); r.end(fs.readFileSync(f));
}).listen(20699);
const b = await chromium.launch({ executablePath: '/home/shan/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const p = await b.newPage({ viewport: { width: 900, height: 1300 } });
await p.goto('http://127.0.0.1:20699/'); await p.waitForFunction(() => document.title.startsWith('done'), null, { timeout: 30000 });
console.log(await p.title()); await p.locator('canvas').screenshot({ path: out, type: 'jpeg', quality: 70 });
await b.close(); srv.close();
