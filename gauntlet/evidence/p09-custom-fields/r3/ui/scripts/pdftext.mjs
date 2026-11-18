import * as pdf from 'pdfjs-dist/legacy/build/pdf.mjs';
import fs from 'node:fs';
const doc = await pdf.getDocument({ data: new Uint8Array(fs.readFileSync(process.argv[2])) }).promise;
for (let p = 1; p <= Math.min(doc.numPages, 2); p++) { const c = await (await doc.getPage(p)).getTextContent(); console.log(c.items.map(i => i.str).join(' ')); }
