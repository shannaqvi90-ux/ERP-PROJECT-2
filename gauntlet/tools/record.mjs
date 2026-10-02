#!/usr/bin/env node
// Gauntlet recorder.
//   node gauntlet/tools/record.mjs <verdict.json>   store a verdict, append it to the ledger, rebuild progress.html
//   node gauntlet/tools/record.mjs --render         rebuild progress.html only
// Verdicts are the source of truth. The ledger is append-only: an entry is written once per verdict id.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const G = path.resolve(here, '..');
const VERDICTS = path.join(G, 'verdicts');
const LEDGER = path.join(G, 'ledger.md');
const PROGRESS = path.join(G, 'progress.html');
const PIECES = JSON.parse(fs.readFileSync(path.join(G, 'pieces.json'), 'utf8'));

const VERDICT_KINDS = ['BLOCKED', 'LOSS', 'WIN'];

function fail(msg) { console.error(`record: ${msg}`); process.exit(1); }

function validate(v) {
  for (const k of ['piece', 'round', 'commit', 'judged_at', 'verdict', 'biggest_gap']) {
    if (v[k] === undefined || v[k] === null || v[k] === '') fail(`verdict missing "${k}"`);
  }
  if (!VERDICT_KINDS.includes(v.verdict)) fail(`verdict must be one of ${VERDICT_KINDS.join(', ')}`);
  if (!Number.isInteger(v.round) || v.round < 1) fail('round must be a positive integer');
  if (!v.biggest_gap.title) fail('biggest_gap.title is required');
}

function verdictId(v) { return `${v.piece}--r${String(v.round).padStart(3, '0')}--${String(v.commit).slice(0, 10)}`; }

function loadVerdicts() {
  if (!fs.existsSync(VERDICTS)) return [];
  return fs.readdirSync(VERDICTS).filter(f => f.endsWith('.json')).sort().map(f => {
    const v = JSON.parse(fs.readFileSync(path.join(VERDICTS, f), 'utf8'));
    v._file = f;
    return v;
  }).sort((a, b) => String(a.judged_at).localeCompare(String(b.judged_at)) || a.round - b.round);
}

function store(file) {
  const v = JSON.parse(fs.readFileSync(file, 'utf8'));
  validate(v);
  fs.mkdirSync(VERDICTS, { recursive: true });
  const id = verdictId(v);
  const dest = path.join(VERDICTS, `${id}.json`);
  if (fs.existsSync(dest)) {
    const prev = fs.readFileSync(dest, 'utf8');
    if (prev.trim() !== JSON.stringify(v, null, 2).trim()) fail(`a different verdict is already stored as ${id}`);
  } else {
    fs.writeFileSync(dest, JSON.stringify(v, null, 2) + '\n');
  }
  appendLedger(v, id);
  return id;
}

const mdEsc = s => String(s ?? '').replace(/\|/g, '\\|').replace(/\r?\n/g, ' ');

function appendLedger(v, id) {
  const ledger = fs.existsSync(LEDGER) ? fs.readFileSync(LEDGER, 'utf8') : '# Gauntlet ledger\n\n';
  if (ledger.includes(`<!-- verdict:${id} -->`)) return;
  const L = [];
  L.push(`<!-- verdict:${id} -->`);
  L.push(`## ${v.judged_at} · ${v.piece} · round ${v.round} · ${v.verdict}`);
  L.push('');
  L.push(`- Commit judged: \`${v.commit}\``);
  if (v.critic) L.push(`- Critic: ${v.critic}`);
  L.push(`- Biggest gap: **${mdEsc(v.biggest_gap.title)}** — ${mdEsc(v.biggest_gap.detail)}`);
  if (v.biggest_gap.evidence) L.push(`- Gap evidence: ${mdEsc(v.biggest_gap.evidence)}`);
  if (Array.isArray(v.hard_gates) && v.hard_gates.length) {
    L.push('- Hard gates:');
    for (const g of v.hard_gates) L.push(`  - ${g.gate}: **${String(g.status).toUpperCase()}** — ${mdEsc(g.evidence)}`);
  }
  if (v.tests) {
    const t = v.tests;
    L.push(`- Tests: ${t.passed ?? '?'} passed, ${t.failed ?? '?'} failed, ${t.skipped ?? 0} skipped of ${t.total ?? '?'}` +
      (t.command ? ` (\`${mdEsc(t.command)}\`` + (t.duration_s != null ? `, ${t.duration_s}s)` : ')') : ''));
  }
  if (v.clean_clone) L.push(`- Clean clone to running demo: ${String(v.clean_clone.status).toUpperCase()}` + (v.clean_clone.seconds != null ? ` in ${v.clean_clone.seconds}s` : '') + (v.clean_clone.evidence ? ` — ${mdEsc(v.clean_clone.evidence)}` : ''));
  if (Array.isArray(v.odoo) && v.odoo.length) {
    L.push('- Odoo side by side:');
    L.push('');
    L.push('  | Task | Ours steps / keys / s / human s | Odoo steps / keys / s / human s | Winner | Notes |');
    L.push('  |---|---|---|---|---|');
    const f = m => m ? `${m.steps ?? '–'} / ${m.keystrokes ?? '–'} / ${m.seconds ?? '–'} / ${m.klm_seconds ?? '–'}` : '–';
    for (const o of v.odoo) L.push(`  | ${mdEsc(o.task)} | ${f(o.ours)} | ${f(o.odoo)} | ${mdEsc(o.winner)} | ${mdEsc(o.notes)} |`);
    L.push('');
  }
  if (Array.isArray(v.other_findings) && v.other_findings.length) {
    L.push('- Other findings:');
    for (const x of v.other_findings) L.push(`  - ${mdEsc(x)}`);
  }
  if (Array.isArray(v.screenshots) && v.screenshots.length) {
    L.push(`- Screenshots: ${v.screenshots.map(s => `[${path.basename(s)}](${path.relative(G, path.resolve(G, '..', s))})`).join(', ')}`);
  }
  if (v.evidence_dir) L.push(`- Evidence folder: \`${v.evidence_dir}\``);
  L.push('');
  fs.writeFileSync(LEDGER, ledger.replace(/\s*$/, '\n\n') + L.join('\n') + '\n');
}

const h = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

function rel(p) {
  if (!p) return '';
  const abs = path.isAbsolute(p) ? p : path.resolve(G, '..', p);
  return path.relative(G, abs).split(path.sep).join('/');
}

function render() {
  const all = loadVerdicts();
  const byPiece = new Map();
  for (const v of all) {
    if (!byPiece.has(v.piece)) byPiece.set(v.piece, []);
    byPiece.get(v.piece).push(v);
  }
  const known = new Set(PIECES.map(p => p.id));
  const extra = [...byPiece.keys()].filter(k => !known.has(k)).map(id => ({ id, title: id, wave: '–' }));
  const rows = [...PIECES, ...extra];

  const counts = { WIN: 0, LOSS: 0, BLOCKED: 0, none: 0 };
  for (const p of rows) {
    const vs = byPiece.get(p.id);
    counts[vs ? vs[vs.length - 1].verdict : 'none']++;
  }
  const lastJudged = all.length ? all[all.length - 1].judged_at : '—';

  const cards = rows.map(p => {
    const vs = byPiece.get(p.id) || [];
    const v = vs[vs.length - 1];
    const status = v ? v.verdict : 'NOT JUDGED';
    const cls = v ? v.verdict.toLowerCase() : 'none';
    const history = vs.map(x => `<span class="chip ${x.verdict.toLowerCase()}" title="round ${x.round} · ${h(x.judged_at)} · ${h(x.biggest_gap.title)}">${x.round}</span>`).join('');
    let body = '<p class="muted">Waiting for its first critic.</p>';
    if (v) {
      const gates = (v.hard_gates || []).map(g => `<li><span class="dot ${h(String(g.status).toLowerCase())}"></span><b>${h(g.gate)}</b> ${h(String(g.status).toUpperCase())}<span class="muted"> — ${h(g.evidence)}</span></li>`).join('');
      const t = v.tests || {};
      const odoo = (v.odoo || []).map(o => {
        const m = x => x ? `${x.steps ?? '–'} · ${x.keystrokes ?? '–'} · ${x.seconds ?? '–'}s · ${x.klm_seconds ?? '–'}s` : '–';
        return `<tr><td>${h(o.task)}</td><td class="num">${m(o.ours)}</td><td class="num">${m(o.odoo)}</td><td><span class="win ${h(String(o.winner).toLowerCase())}">${h(o.winner)}</span></td></tr>`;
      }).join('');
      const shots = (v.screenshots || []).slice(0, 8).map(s => `<a href="${h(rel(s))}"><img loading="lazy" src="${h(rel(s))}" alt="${h(path.basename(s))}"></a>`).join('');
      body = `
        <div class="gap"><div class="label">Biggest gap</div><b>${h(v.biggest_gap.title)}</b><p>${h(v.biggest_gap.detail)}</p></div>
        <div class="grid2">
          <div><div class="label">Hard gates</div><ul class="gates">${gates || '<li class="muted">not reported</li>'}</ul></div>
          <div><div class="label">Tests</div><p class="big">${h(t.passed ?? '–')}<span class="muted"> / ${h(t.total ?? '–')} passed</span></p>
            <p class="muted">${h(t.failed ?? 0)} failed · ${h(t.skipped ?? 0)} skipped${t.duration_s != null ? ` · ${h(t.duration_s)}s` : ''}</p>
            ${v.clean_clone ? `<p class="muted">Clean clone → demo: ${h(String(v.clean_clone.status).toUpperCase())}${v.clean_clone.seconds != null ? ` in ${h(v.clean_clone.seconds)}s` : ''}</p>` : ''}</div>
        </div>
        ${odoo ? `<div class="label">Against Odoo <span class="muted">(steps · keystrokes · machine s · modelled human s)</span></div><div class="tablewrap"><table><thead><tr><th>Task</th><th>Ours</th><th>Odoo</th><th>Winner</th></tr></thead><tbody>${odoo}</tbody></table></div>` : ''}
        ${shots ? `<div class="label">Screenshots</div><div class="shots">${shots}</div>` : ''}
        <p class="muted small">Round ${v.round} · judged ${h(v.judged_at)} · commit <code>${h(String(v.commit).slice(0, 10))}</code></p>`;
    }
    return `<section class="card">
      <header><div><span class="pid">${h(p.id)}</span><h2>${h(p.title)}</h2><span class="muted small">Wave ${h(p.wave)} · ${vs.length} round${vs.length === 1 ? '' : 's'} judged</span></div>
      <span class="badge ${cls}">${h(status)}</span></header>
      ${history ? `<div class="history">${history}</div>` : ''}
      ${body}
    </section>`;
  }).join('\n');

  const html = `<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Gauntlet Progress</title>
<style>
:root{--bg:#f6f7f9;--card:#fff;--fg:#1b1f24;--muted:#5d6670;--line:#dfe3e8;--win:#1f7a4a;--loss:#a15c00;--blocked:#b42318;--none:#7a838c;--code:#eef1f4}
@media (prefers-color-scheme: dark){:root:not([data-theme="light"]){--bg:#111418;--card:#1a1f25;--fg:#e7eaee;--muted:#9aa4ae;--line:#2c333b;--win:#4cc38a;--loss:#f0a742;--blocked:#ff6b5e;--none:#8a939c;--code:#232a31}}
:root[data-theme="dark"]{--bg:#111418;--card:#1a1f25;--fg:#e7eaee;--muted:#9aa4ae;--line:#2c333b;--win:#4cc38a;--loss:#f0a742;--blocked:#ff6b5e;--none:#8a939c;--code:#232a31}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.45 system-ui,-apple-system,"Segoe UI",Roboto,"Noto Sans Arabic",sans-serif}
main{max-width:1180px;margin:0 auto;padding:24px 16px 64px}h1{font-size:22px;margin:0 0 4px}h2{font-size:16px;margin:2px 0}
.muted{color:var(--muted)}.small{font-size:12px}code{background:var(--code);padding:1px 4px;border-radius:4px}
.summary{display:flex;flex-wrap:wrap;gap:12px;margin:16px 0 24px}.stat{background:var(--card);border:1px solid var(--line);border-radius:8px;padding:10px 14px;min-width:120px}.stat b{display:block;font-size:20px}
.cards{display:grid;grid-template-columns:repeat(auto-fill,minmax(min(100%,540px),1fr));gap:14px}
.card{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:14px 16px;min-width:0}
.card header{display:flex;justify-content:space-between;gap:12px;align-items:flex-start}.pid{font:12px ui-monospace,monospace;color:var(--muted)}
.badge{font-size:12px;font-weight:700;padding:3px 8px;border-radius:999px;border:1px solid currentColor;white-space:nowrap}
.badge.win,.chip.win,.win.ours{color:var(--win)}.badge.loss,.chip.loss,.win.odoo,.win.tie{color:var(--loss)}.badge.blocked,.chip.blocked{color:var(--blocked)}.badge.none{color:var(--none)}
.history{display:flex;flex-wrap:wrap;gap:4px;margin:8px 0}.chip{font-size:11px;border:1px solid currentColor;border-radius:4px;padding:0 5px}
.label{font-size:11px;text-transform:uppercase;letter-spacing:.04em;color:var(--muted);margin:12px 0 4px}
.gap{border-inline-start:3px solid var(--loss);padding:4px 10px;margin:10px 0;background:var(--bg);border-radius:4px}.gap p{margin:4px 0 0}
.grid2{display:grid;grid-template-columns:1fr 1fr;gap:12px}@media (max-width:560px){.grid2{grid-template-columns:1fr}}
.gates{list-style:none;padding:0;margin:0}.gates li{margin:3px 0}.dot{display:inline-block;width:8px;height:8px;border-radius:50%;margin-inline-end:6px;background:var(--none)}.dot.pass{background:var(--win)}.dot.fail{background:var(--blocked)}
.big{font-size:20px;font-weight:700;margin:0}
.tablewrap{overflow-x:auto}table{border-collapse:collapse;width:100%;font-size:12px}th,td{text-align:start;padding:4px 6px;border-bottom:1px solid var(--line)}td.num{font-variant-numeric:tabular-nums;white-space:nowrap}
.shots{display:flex;flex-wrap:wrap;gap:6px}.shots img{width:120px;height:76px;object-fit:cover;border:1px solid var(--line);border-radius:4px}
</style></head>
<body><main>
<h1>Gauntlet progress</h1>
<p class="muted">Platform core, judged piece by piece against the owner's bar and Odoo Community. Rebuilt from <code>gauntlet/verdicts/</code> by <code>gauntlet/tools/record.mjs</code>. Full evidence in <a href="ledger.md">ledger.md</a>; owner items in <a href="needs-human.md">needs-human.md</a>.</p>
<div class="summary">
<div class="stat"><b>${all.length}</b><span class="muted">verdicts</span></div>
<div class="stat"><b style="color:var(--win)">${counts.WIN}</b><span class="muted">pieces winning</span></div>
<div class="stat"><b style="color:var(--loss)">${counts.LOSS}</b><span class="muted">pieces losing</span></div>
<div class="stat"><b style="color:var(--blocked)">${counts.BLOCKED}</b><span class="muted">pieces blocked</span></div>
<div class="stat"><b>${counts.none}</b><span class="muted">not judged yet</span></div>
<div class="stat"><b class="small">${h(lastJudged)}</b><span class="muted">last verdict</span></div>
</div>
<div class="cards">
${cards}
</div>
</main></body></html>
`;
  fs.writeFileSync(PROGRESS, html);
}

const args = process.argv.slice(2);
if (args[0] === '--render') { render(); console.log('progress.html rebuilt'); }
else if (args[0]) { const id = store(args[0]); render(); console.log(`recorded ${id}`); }
else fail('usage: record.mjs <verdict.json> | --render');
