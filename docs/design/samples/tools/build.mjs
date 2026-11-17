// Generates the static pages of every direction. Usage: node build.mjs [a|b|c|d|e|f|g ...]
import fs from 'node:fs';
const root = new URL('../', import.meta.url).pathname;
const want = process.argv.slice(2);
const names = { dashboard: 'dashboard', contacts: 'contacts', form: 'contact-form', settings: 'settings', signin: 'sign-in' };
for (const d of ['a', 'b', 'c', 'd', 'e', 'f', 'g']) {
  if (want.length && !want.includes(d)) continue;
  let mod;
  try { mod = await import(`./dir-${d}.mjs`); } catch (e) { if (e.code === 'ERR_MODULE_NOT_FOUND' && e.message.includes(`dir-${d}.mjs`)) { console.log('skip', d); continue; } throw e; }
  let made = [];
  for (const lang of ['en', 'ar']) {
    const p = mod.pages(lang);
    made = Object.keys(names).filter((k) => k in p);
    for (const [k, html] of Object.entries(p)) fs.writeFileSync(`${root}${d}/${names[k]}.${lang}.html`, html);
  }
  const idx = `<!doctype html><meta charset="utf-8"><title>Direction ${d.toUpperCase()}</title><link rel="stylesheet" href="tokens.css"><body style="font:14px var(--font-ui);background:var(--bg);color:var(--fg);padding:32px"><h1>Direction ${d.toUpperCase()}</h1><ul>${made.map((k) => names[k]).map((n) => `<li>${n}: <a href="${n}.en.html">English</a> · <a href="${n}.ar.html" lang="ar">العربية</a></li>`).join('')}</ul><p>Add <code>?theme=dark</code>, <code>?theme=hc</code> or <code>?density=compact</code> to any page.</p>`;
  fs.writeFileSync(`${root}${d}/index.html`, idx);
}
