import fs from 'node:fs';
import { loadImage, createCanvas } from '@napi-rs/canvas';
const [, , src, dst] = process.argv;
const img = await loadImage(fs.readFileSync(src));
const c = createCanvas(img.width, img.height); const ctx = c.getContext('2d');
ctx.fillStyle = '#fff'; ctx.fillRect(0, 0, img.width, img.height); ctx.drawImage(img, 0, 0);
fs.writeFileSync(dst, c.toBuffer('image/jpeg', 72));
