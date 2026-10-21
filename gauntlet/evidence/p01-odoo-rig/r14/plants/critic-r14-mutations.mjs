// Critic p01 r14: own instrument mutations (counting, timers, blindness, start checks), each in a
// scratch copy, judged against a control run of the same test files. A test counts as catching a
// mutation only if it passed unmutated. Usage: CRITIC_REPO=<clone> node scripts/critic-r13-mutations.mjs [ids]
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { tapResult } from './mutations.mjs';

const HARNESS = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const REPO = process.env.CRITIC_REPO;
const FILES = (process.env.CRITIC_FILES || 'test/klm.test.mjs test/compare.test.mjs test/blind.test.mjs test/operator.test.mjs test/screen-causes.test.mjs test/before-clock.test.mjs test/measured-part.test.mjs').split(' ');

export const MINE = [
  ['K1', 'the screen read for the found name skips field values (a default search in the box)', 'lib/operator.mjs', 'for (const el of document.querySelectorAll("input, textarea, select"))', 'for (const el of [])'],
  ['K2', 'set-up writes checked only for the whole name, not its words', 'lib/runner.mjs', '.filter(w => w.length >= 4 && /\\p{L}/u.test(w))]);', '.filter(w => w.length >= 99 && /\\p{L}/u.test(w))]);'],
  ['K3', 'JSON \\u escapes in a set-up write not decoded', 'lib/runner.mjs', "  t = t.replace(/\\\\u([0-9a-fA-F]{4})/g, (_, h) => String.fromCharCode(parseInt(h, 16)));", ''],
  ['K4', 'the start gap made predictable (exactly 1.1 s)', 'lib/runner.mjs', 'export const START_GAP_SPREAD_MS = 1000;', 'export const START_GAP_SPREAD_MS = 1;'],
  ['K5', 'one key of every typed text uncounted', 'lib/klm.mjs', '  for (const ch of String(text)) n += SHIFTED.test(ch) ? 2 : 1;\n  return n;', '  for (const ch of String(text)) n += SHIFTED.test(ch) ? 2 : 1;\n  return Math.max(0, n - 1);'],
  ['K6', 'machine seconds read 10% short (wrong timer)', 'lib/operator.mjs', '  get machineSeconds() { return round(this.now()); }', '  get machineSeconds() { return round(this.now() * 0.9); }'],
  ['K7', 'vendor words not masked in blind shots (blindness leak)', 'lib/blind.mjs', '  if (branding.words.length) {\n    const re = new RegExp(branding.words.map(escapeRe).join(\'|\'), \'i\');\n    locs.push(page.getByText(re));\n  }', ''],
  ['K8', 'the found name read on screen as the whole text only (any surrounding text hides it)', 'lib/operator.mjs', 'return norm(parts.join("\\\\n")).includes(norm(v)); }', 'return parts.some(p => norm(p) === norm(v)); }'],
  ['K9', 'only the first screen change judged', 'lib/causation.mjs', "animatedAtStart = 0, limit = 2000 } = {}) {\n  const model = trace.model || analyseTrace(trace.events);\n  const slackUs", "animatedAtStart = 0, limit = 1 } = {}) {\n  const model = trace.model || analyseTrace(trace.events);\n  const slackUs"],
  ['K10', 'a click step recorded without the click (uncounted action)', 'lib/operator.mjs', "    const step = this.#record('click', label || String(target), 0, t, {}, undefined, began);", "    const step = this.#steps.length ? this.#record('click', label || String(target), 0, t, {}, undefined, began) : null;"],
];

function copy() {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'critic-r14-mut-'));
  const dest = path.join(root, 'gauntlet', 'compare');
  fs.mkdirSync(path.dirname(dest), { recursive: true });
  fs.cpSync(HARNESS, dest, { recursive: true, filter: s => !s.includes(`${path.sep}data${path.sep}out`) && !/zz-critic-r1\d/.test(s) });
  fs.cpSync(path.join(REPO, 'gauntlet', 'ratchet.json'), path.join(root, 'gauntlet', 'ratchet.json'));
  fs.cpSync(path.join(REPO, 'gauntlet', 'reference'), path.join(root, 'gauntlet', 'reference'), { recursive: true });
  for (const d of ['web', 'docs', 'src']) fs.symlinkSync(path.join(REPO, d), path.join(root, d));
  fs.symlinkSync(path.join(HARNESS, 'data', 'out'), path.join(dest, 'data', 'out'));
  return { root, dest };
}
function run(dest) {
  const r = spawnSync(process.execPath, ['--test', '--test-reporter=tap', '--test-concurrency=1', ...FILES], { cwd: dest, encoding: 'utf8', timeout: 40 * 60_000 });
  const res = new Map();
  for (const line of (r.stdout || '').split('\n')) { const t = tapResult(line); if (t && !line.startsWith('    ')) res.set(t.name, res.has(t.name) ? res.get(t.name) && t.passed : t.passed); }
  return res;
}
const only = process.argv.slice(2);
const { root, dest } = copy();
try {
  const control = run(dest);
  const failingControl = [...control].filter(([, v]) => !v).map(([k]) => k);
  console.log(`control: ${control.size} tests, ${failingControl.length} failing unmutated${failingControl.length ? ': ' + failingControl.join('; ') : ''}`);
  for (const [id, what, file, text, repl] of MINE.filter(m => !only.length || only.includes(m[0]))) {
    const p = path.join(dest, file);
    const orig = fs.readFileSync(p, 'utf8');
    if (!orig.includes(text)) { console.log(`${id} ${what}: TEXT NOT FOUND`); continue; }
    fs.writeFileSync(p, orig.replace(text, repl));
    let res; try { res = run(dest); } finally { fs.writeFileSync(p, orig); }
    const caught = [...res].filter(([k, v]) => !v && control.get(k) === true).map(([k]) => k);
    console.log(`${id} ${what}: ${caught.length ? 'caught' : 'MISSED'} (${caught.length} failing that passed unmutated${caught.length ? ': ' + caught.slice(0, 2).join(' | ') : ''})`);
  }
} finally { fs.rmSync(root, { recursive: true, force: true }); }
