import { browser } from './lib.mjs';
import { readFileSync } from 'node:fs';
const pairs = JSON.parse(process.argv[2]);
const b = await browser();
const page = await b.newPage();
for (const [src, dst, clip] of pairs) {
  const data = readFileSync(src).toString('base64');
  const type = src.endsWith('.jpg') ? 'jpeg' : 'png';
  await page.setContent(`<body style="margin:0"><img id=i src="data:image/${type};base64,${data}"></body>`);
  const box = await page.locator('#i').boundingBox();
  await page.setViewportSize({ width: Math.ceil(box.width), height: Math.ceil(box.height) });
  await page.screenshot({ path: dst, type: 'jpeg', quality: 62, ...(clip ? { clip } : {}) });
}
await b.close();
