import fs from 'node:fs';
import * as pdfjs from 'pdfjs-dist/legacy/build/pdf.mjs';
const [,, file, outPrefix, pagesArg] = process.argv;
const data = new Uint8Array(fs.readFileSync(file));
const doc = await pdfjs.getDocument({ data, verbosity: 1 }).promise;
console.log('pages', doc.numPages);
const n = Math.min(doc.numPages, Number(pagesArg || 1));
let text = '';
for (let i = 1; i <= doc.numPages; i++) {
  const p = await doc.getPage(i);
  const tc = await p.getTextContent();
  text += `--- page ${i}\n` + tc.items.map(t => t.str + (t.hasEOL ? '\n' : ' ')).join('') + '\n';
  if (i <= n && outPrefix) {
    const vp = p.getViewport({ scale: 1.6 });
    const cf = doc.canvasFactory;
    const { canvas, context } = cf.create(vp.width, vp.height);
    await p.render({ canvasContext: context, viewport: vp, canvas }).promise;
    fs.writeFileSync(`${outPrefix}-p${i}.png`, canvas.toBuffer('image/png'));
  }
}
if (outPrefix) fs.writeFileSync(`${outPrefix}.txt`, text);
else console.log(text.slice(0, 3000));
