// Blind review page: for each task, the two products' screenshots side by side as "A" and "B".
// The letters are assigned at random per task; the assignment is written to key.json, which a
// reviewer does not open until the verdict is written.
import fs from 'node:fs';
import path from 'node:path';
import { assignLetters } from './blind.mjs';

const esc = s => String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

export function writeReview(outDir, comparisons, random = Math.random) {
  const keyFile = path.join(outDir, 'key.json');
  let key = { shots: {} };
  try { key = JSON.parse(fs.readFileSync(keyFile, 'utf8')); } catch { /* new key */ }
  key.letters = key.letters || {};
  const sections = [];
  for (const { cmp, runs } of comparisons) {
    const letters = assignLetters(['ours', 'odoo'], random);
    key.letters[cmp.task] = letters;
    const cols = Object.entries(letters).sort((a, b) => a[1].localeCompare(b[1])).map(([product, letter]) => {
      const r = runs[product];
      const shots = (r?.screenshots || []).map(s => `<figure><img loading="lazy" src="shots/${esc(s.file)}" alt="${esc(letter)}: ${esc(s.moment)}"><figcaption>${esc(s.moment)}</figcaption></figure>`).join('');
      const body = r?.status === 'not_built' ? '<p class="empty">Not built yet.</p>' : shots || '<p class="empty">No screenshots.</p>';
      return `<div class="col"><h3>${letter}</h3>${body}</div>`;
    }).join('');
    sections.push(`<section><h2>${esc(cmp.task)}</h2><div class="cols">${cols}</div></section>`);
  }
  fs.writeFileSync(keyFile, JSON.stringify(key, null, 2) + '\n');
  const html = `<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Blind review</title>
<style>
  :root { --bg: #fafafa; --fg: #1b1b1b; --muted: #666; --line: #ddd; }
  @media (prefers-color-scheme: dark) { :root { --bg: #161616; --fg: #eee; --muted: #aaa; --line: #333; } }
  body { margin: 0; padding: 16px; background: var(--bg); color: var(--fg); font: 14px/1.4 system-ui, sans-serif; }
  h1 { font-size: 18px; } h2 { font-size: 16px; border-top: 1px solid var(--line); padding-top: 12px; }
  .cols { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
  @media (max-width: 800px) { .cols { grid-template-columns: 1fr; } }
  figure { margin: 0 0 12px; } img { width: 100%; border: 1px solid var(--line); }
  figcaption, .empty { color: var(--muted); }
</style></head><body>
<h1>Blind review: two products, same tasks</h1>
<p>Products are shown as A and B, assigned at random per task. Do not open key.json until the verdict is written.</p>
${sections.join('\n')}
</body></html>
`;
  const file = path.join(outDir, 'review.html');
  fs.writeFileSync(file, html);
  return file;
}
