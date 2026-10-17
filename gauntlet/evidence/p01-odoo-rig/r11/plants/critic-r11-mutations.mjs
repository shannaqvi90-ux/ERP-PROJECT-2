// Critic p01 round 11: instrument mutations of my own (not in scripts/mutations.mjs). Each is applied
// to a scratch copy of the harness, then the named test files run WHOLE (no name pattern). A mutation
// is caught when a test that passes unmutated fails mutated. Usage: node scripts/critic-r11-mutations.mjs [ids] [--all-files]
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { tapResult } from './mutations.mjs';

const HARNESS = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const REPO = path.resolve(HARNESS, '..', '..');

const MUTS = [
  ['K1', 'KLM pointing operator P lowered (1.10 -> 0.80)', 'lib/klm.mjs', 'P: 1.1,', 'P: 0.8,', ['test/klm.test.mjs']],
  ['K2', 'KLM mental operator M lowered (1.35 -> 1.0)', 'lib/klm.mjs', 'M: 1.35 }', 'M: 1.0 }', ['test/klm.test.mjs']],
  ['K3', 'no H when the hand moves between mouse and keyboard', 'lib/klm.mjs', '  if (prevDevice && prevDevice !== device) ops.H += 1;', '', ['test/klm.test.mjs', 'test/operator.test.mjs']],
  ['K4', 'Shift not counted for capital letters', 'lib/klm.mjs', 'const SHIFTED = /[A-Z~!@#$%^&*()_+{}|:"<>?]/;', 'const SHIFTED = /[~!@#$%^&*()_+{}|:"<>?]/;', ['test/klm.test.mjs', 'test/operator.test.mjs']],
  ['K5', 'a click modelled as one button action (B) instead of press and release', 'lib/klm.mjs', "case 'click': case 'scroll': ops.P += 1; ops.B += 2; break;", "case 'click': case 'scroll': ops.P += 1; ops.B += 1; break;", ['test/klm.test.mjs']],
  ['K6', 'typing always continues the step before (no M before any typing)', 'lib/klm.mjs', "    case 'type': return prev.kind === 'key' || (isClick(prev) && step.same_field === true);", "    case 'type': return true;", ['test/klm.test.mjs', 'test/operator.test.mjs']],
  ['O1', 'op.fill focuses the field with an uncounted click', 'lib/operator.mjs', '    await this.click(target, { label: `focus ${label || String(target)}` });', '    await this.#locate(target).click();', ['test/operator.test.mjs']],
  ['O2', 'op.type accepts control characters (Enter/Tab inside one field entry)', 'lib/operator.mjs', '    if (control) throw new UncountedAction(`op.type() with the control character', '    if (false) throw new UncountedAction(`op.type() with the control character', ['test/operator.test.mjs', 'test/guard.test.mjs']],
  ['O3', 'a paste of text copied before the clock accepted', 'lib/operator.mjs', '    if (isPaste(chord) && !this.#copied) {', '    if (false) {', ['test/operator.test.mjs', 'test/guard.test.mjs']],
  ['O4', 'the clock starts 0.3 s late (a wrong timer)', 'lib/operator.mjs', '    this.#t0 = clockNow(); this.#t1 = null;', '    this.#t0 = clockNow() + 300; this.#t1 = null;', ['test/operator.test.mjs', 'test/page-script.test.mjs']],
  ['O5', 'the clock does not wait for a save still under way when run() returns', 'lib/runner.mjs', '      const end = tracker ? await op.settle(tracker, { timeout }) : null;', '      const end = null;', ['test/page-script.test.mjs', 'test/operator.test.mjs']],
  ['G1', 'a click allowed through ctx.page while measured', 'lib/guard.mjs', "'getAttribute', 'allTextContents',", "'getAttribute', 'click', 'allTextContents',", ['test/guard.test.mjs']],
  ['G2', 'no read-only check while measured', 'lib/guard.mjs', '        if (readOnly() && !ALLOWED_WHILE_MEASURED[cls]?.has(prop)) {', '        if (false) {', ['test/guard.test.mjs']],
  ['B1', 'the page title not neutralised in blind shots', 'lib/blind.mjs', "    document.title = 'Product';", '', ['test/blind.test.mjs', 'test/operator.test.mjs']],
  ['B2', 'the favicon not removed in blind shots', 'lib/blind.mjs', "    for (const l of document.querySelectorAll('link[rel~=\"icon\"], link[rel=\"shortcut icon\"], link[rel=\"apple-touch-icon\"]')) l.remove();", '', ['test/blind.test.mjs', 'test/operator.test.mjs']],
  ['B3', "the driver's own moment names shown on the blind page", 'lib/blind.mjs', "s.moment === 'start' || s.moment === 'done' || s.moment === 'error' ? s.moment : `moment ${++n}`", 's.moment', ['test/blind.test.mjs']],
  ['R1', 'a task already done before the clock accepted', 'lib/runner.mjs', "      if (before.value?.verified === true) throw new ActionOutsideClock('the task was already done", "      if (false) throw new ActionOutsideClock('the task was already done", ['test/before-clock.test.mjs']],
  ['R2', 'a comparison with one lost metric still a win', 'lib/runner.mjs', "  const verdict = counted.length > 0 && counted.every(x => x.outcome === 'win') ? 'win' : 'loss';", "  const verdict = counted.length > 0 && counted.filter(x => x.outcome !== 'win').length <= 1 ? 'win' : 'loss';", ['test/compare.test.mjs']],
  ['R3', 'repeats keep the fastest time, not the median', 'lib/runner.mjs', '  const med = xs => { const s = [...xs].sort((a, b) => a - b); const m = s.length >> 1; return s.length % 2 ? s[m] : round((s[m - 1] + s[m]) / 2); };', '  const med = xs => Math.min(...xs);', ['test/compare.test.mjs', 'test/baselines.test.mjs']],
  ['R4', 'the reference volume bar lowered to 10,000 rows', 'lib/rig-volume.mjs', 'export const MIN_ROWS_PER_MAIN_LIST = 100_000;', 'export const MIN_ROWS_PER_MAIN_LIST = 10_000;', ['test/rig-volume.test.mjs']],
  ['C1', 'a request sent up to 1 s after a step links writes to it', 'lib/runner.mjs', '  const during = t => latest(w => t >= w.from - EPS && t <= w.to + EPS);', '  const during = t => latest(w => t >= w.from - EPS && t <= w.to + REACTION_SECONDS + EPS);', ['test/measured-part.test.mjs']],
  ['C2', 'a write sent up to 3 s BEFORE a step credited to it', 'lib/runner.mjs', '  const after = t => latest(w => t >= w.from - EPS && t <= w.to + REACTION_SECONDS + EPS);', '  const after = t => latest(w => t >= w.from - 3 - EPS && t <= w.to + REACTION_SECONDS + EPS);', ['test/measured-part.test.mjs']],
  ['C3', "a step's label counts as typed text", 'lib/runner.mjs', "  const texts = [typed.join(''), typed.join('\\n'),", "  const texts = [typed.join(''), typed.join('\\n'), steps.map(s => String(s.label ?? '')).join(' '),", ['test/measured-part.test.mjs', 'test/before-clock.test.mjs']],
  ['C4', 'any text the person typed counts toward every entered value (letters in any order)', 'lib/runner.mjs', "  return new RegExp(`(?<![\\\\p{L}\\\\p{N}])${parts.join('[^\\\\p{L}\\\\p{N}]*')}(?![\\\\p{L}\\\\p{N}])`, 'iu').test(t);", "  return new RegExp(`${parts.join('[\\\\s\\\\S]*')}`, 'iu').test(t);", ['test/measured-part.test.mjs', 'test/before-clock.test.mjs']],
];

const args = process.argv.slice(2);
const allFiles = args.includes('--all-files');
const only = args.filter(a => !a.startsWith('--'));
const chosen = MUTS.filter(m => !only.length || only.includes(m[0]));

const root = fs.mkdtempSync(path.join(os.tmpdir(), 'critic-r11-mutations-'));
const dest = path.join(root, 'gauntlet', 'compare');
fs.mkdirSync(path.dirname(dest), { recursive: true });
fs.cpSync(HARNESS, dest, { recursive: true, filter: src => !src.includes(`${path.sep}data${path.sep}out`) && !src.endsWith('zz-critic-r11.test.mjs') });
fs.cpSync(path.join(REPO, 'gauntlet', 'ratchet.json'), path.join(root, 'gauntlet', 'ratchet.json'));
fs.cpSync(path.join(REPO, 'gauntlet', 'reference'), path.join(root, 'gauntlet', 'reference'), { recursive: true });
fs.symlinkSync(path.join(HARNESS, 'data', 'out'), path.join(dest, 'data', 'out'));
const ALL = fs.readdirSync(path.join(dest, 'test')).filter(f => f.endsWith('.test.mjs') && f !== 'live-odoo.test.mjs').map(f => `test/${f}`);

function run(files) {
  const r = spawnSync(process.execPath, ['--test', '--test-reporter=tap', '--test-concurrency=1', ...files], { cwd: dest, encoding: 'utf8', timeout: 60 * 60_000 });
  const results = new Map();
  for (const line of (r.stdout || '').split('\n')) { const t = tapResult(line); if (t) results.set(t.name, results.has(t.name) ? results.get(t.name) && t.passed : t.passed); }
  return results;
}
const controls = new Map();
const controlFor = files => { const k = files.join(' '); if (!controls.has(k)) controls.set(k, run(files)); return controls.get(k); };
let missed = 0;
for (const [id, what, file, text, repl, files0] of chosen) {
  const files = allFiles ? ALL : files0;
  const p = path.join(dest, file);
  const orig = fs.readFileSync(p, 'utf8');
  if (!orig.includes(text)) { console.log(`${id} ${what}: TEXT NOT FOUND`); continue; }
  const control = controlFor(files);
  fs.writeFileSync(p, orig.replace(text, repl));
  let mutated;
  try { mutated = run(files); } finally { fs.writeFileSync(p, orig); }
  const caughtBy = [...mutated.entries()].filter(([n, ok]) => !ok && control.get(n) === true).map(([n]) => n);
  const controlFails = [...control.values()].filter(v => !v).length;
  if (!caughtBy.length) missed++;
  console.log(`${id} ${what}: ${caughtBy.length ? `caught by ${caughtBy.length} (${caughtBy.slice(0, 2).join(' | ')})` : 'MISSED'} [files: ${allFiles ? 'all' : files.join(',')}; control tests ${control.size}, control failures ${controlFails}]`);
}
fs.rmSync(root, { recursive: true, force: true });
console.log(`${missed} of ${chosen.length} critic mutations missed`);
