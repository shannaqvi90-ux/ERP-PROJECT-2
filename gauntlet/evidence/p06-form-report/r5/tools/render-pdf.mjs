// Renders page 1 of a PDF to a JPEG with pdf.js inside Chromium; also prints the page text.
import { chromium } from '/home/shan/critic/p06-form-report-r5/gauntlet/compare/node_modules/playwright-core/index.mjs';
import { readFileSync } from 'node:fs';
import http from 'node:http';
const [pdfPath, out] = process.argv.slice(2);
const dir = new URL('./node_modules/pdfjs-dist/build/', import.meta.url).pathname;
const server = http.createServer((q, r) => {
  if (q.url === '/doc.pdf') { r.writeHead(200, { 'content-type': 'application/pdf' }); return r.end(readFileSync(pdfPath)); }
  if (q.url.startsWith('/b/')) { r.writeHead(200, { 'content-type': 'text/javascript' }); return r.end(readFileSync(dir + q.url.slice(3))); }
  r.writeHead(200, { 'content-type': 'text/html' });
  r.end(`<canvas id=c></canvas><script type=module>
import * as p from '/b/pdf.mjs'; p.GlobalWorkerOptions.workerSrc='/b/pdf.worker.mjs';
const d=await p.getDocument('/doc.pdf').promise; const pg=await d.getPage(1); const v=pg.getViewport({scale:1.4});
const c=document.getElementById('c'); c.width=v.width; c.height=v.height; await pg.render({canvasContext:c.getContext('2d'),viewport:v}).promise;
const t=await pg.getTextContent(); window.text=t.items.map(i=>i.str).join(' | '); window.pages=d.numPages; window.done=true;</script>`);
}).listen(0);
const port = server.address().port;
const b = await chromium.launch({ executablePath: process.env.HOME + '/.cache/ms-playwright/chromium-1243/chrome-linux64/chrome' });
const page = await b.newPage({ viewport: { width: 900, height: 1300 } });
await page.goto(`http://localhost:${port}/`); await page.waitForFunction(() => window.done, null, { timeout: 30000 });
await page.locator('#c').screenshot({ path: out, type: 'jpeg', quality: 60 });
console.log('pages', await page.evaluate(() => window.pages)); console.log((await page.evaluate(() => window.text)).slice(0, 1500));
await b.close(); server.close();
