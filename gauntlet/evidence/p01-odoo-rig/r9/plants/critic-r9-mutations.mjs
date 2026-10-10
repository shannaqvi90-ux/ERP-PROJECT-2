// Critic p01 r9: own instrument mutations (not in scripts/mutations.mjs), each run against the whole
// non-live harness suite in a scratch copy. A mutation is caught when a test that passed unmutated fails.
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { tapResult } from './mutations.mjs';
const HARNESS = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const REPO = path.resolve(HARNESS, '..', '..');
export const CRITIC = [
  ['X1', 'wrong timer: clock does not run on while the product still answers a save', 'lib/runner.mjs', '      const end = tracker ? await op.settle(tracker, { timeout }) : null;', '      const end = null;', 'test/page-script.test.mjs test/operator.test.mjs'],
  ['X2', 'KLM: typing always continues the step before (no M)', 'lib/klm.mjs', "    case 'type': return prev.kind === 'key' || (isClick(prev) && step.same_field === true);", "    case 'type': return true;", 'test/klm.test.mjs test/operator.test.mjs test/baselines.test.mjs'],
  ['X3', 'uppercase letters typed without Shift', 'lib/klm.mjs', 'const SHIFTED = /[A-Z~!@#$%^&*()_+{}|:"<>?]/;', 'const SHIFTED = /[~!@#$%^&*()_+{}|:"<>?]/;', 'test/klm.test.mjs test/operator.test.mjs test/baselines.test.mjs'],
  ['X4', 'uncounted: a paste of text copied before the clock allowed', 'lib/operator.mjs', '    if (isPaste(chord) && !this.#copied) {', '    if (false) {', 'test/operator.test.mjs test/guard.test.mjs'],
  ['X5', 'uncounted: control characters inside op.type allowed', 'lib/operator.mjs', '    if (control) throw new UncountedAction(`op.type() with the control character', '    if (false) throw new UncountedAction(`op.type() with the control character', 'test/operator.test.mjs test/guard.test.mjs'],
  ['X6', 'blindness: page title not neutralised', 'lib/blind.mjs', "    document.title = 'Product';", '    void 0;', 'test/blind.test.mjs test/operator.test.mjs'],
  ['X7', 'blindness: favicon kept', 'lib/blind.mjs', `    for (const l of document.querySelectorAll('link[rel~="icon"], link[rel="shortcut icon"], link[rel="apple-touch-icon"]')) l.remove();`, '    void 0;', 'test/blind.test.mjs test/operator.test.mjs'],
  ['X8', 'blindness: screenshot file times not neutralised', 'lib/runner.mjs', '    try { fs.utimesSync(path.join(out.shotsDir, s.file), NEUTRAL_FILE_TIME, NEUTRAL_FILE_TIME); } catch { /* removed variant shot */ }', '    void 0;', 'test/blind.test.mjs test/cli.test.mjs test/compare.test.mjs'],
  ['X9', 'rig volume bar lowered to 1,000 rows', 'lib/rig-volume.mjs', 'export const MIN_ROWS_PER_MAIN_LIST = 100_000;', 'export const MIN_ROWS_PER_MAIN_LIST = 1_000;', 'test/rig-volume.test.mjs test/ratchet.test.mjs test/cli.test.mjs'],
  ['X10', 'a refusal the driver swallowed no longer invalidates the run', 'lib/runner.mjs', "    if (violations.length && run.status !== 'not_built') {", '    if (false) {', 'test/guard.test.mjs test/sandbox.test.mjs test/before-clock.test.mjs'],
  ['X11', 'wrong timer: a POST counted as a read, so settle never waits for a save', 'lib/guard.mjs', "  if (m === 'GET' || m === 'HEAD' || m === 'OPTIONS') return false;", "  if (m === 'GET' || m === 'HEAD' || m === 'OPTIONS' || m === 'POST') return false;", 'test/guard.test.mjs test/page-script.test.mjs'],
  ['X12', 'set-up local storage carried into a signed-in start', 'lib/runner.mjs', "  const storageState = kind === 'sign-in' ? saved : { cookies: saved.cookies, origins: [] };", '  const storageState = saved;', 'test/guard.test.mjs test/operator.test.mjs test/before-clock.test.mjs'],
  ['X13', 'KLM: no H when the hand moves between mouse and keyboard', 'lib/klm.mjs', '  if (prevDevice && prevDevice !== device) ops.H += 1;', '  void 0;', 'test/klm.test.mjs test/operator.test.mjs test/baselines.test.mjs'],
  ['X14', 'set-up fetch calls not waited for before the start', 'lib/runner.mjs', '    run.set_up_calls_waited = await session.drain(timeout);', '    run.set_up_calls_waited = 0;', 'test/sandbox.test.mjs test/guard.test.mjs'],
  ['X15', 'unfair list/home start (typed text in a field) accepted', 'lib/start.mjs', '  if (problems.length) throw new ActionOutsideClock(`unfair start state:', '  if (false) throw new ActionOutsideClock(`unfair start state:', 'test/guard.test.mjs test/before-clock.test.mjs'],
  ['X16', 'clipboard not emptied at the start', 'lib/runner.mjs', '  await resetClipboard(context);', '  void 0;', 'test/operator.test.mjs test/guard.test.mjs'],
  ['X17', 'a failed reference run compared as usable', 'lib/runner.mjs', "  const usable = r => r && r.status === 'verified';", "  const usable = r => r && (r.status === 'verified' || r.status === 'failed');", 'test/compare.test.mjs test/cli.test.mjs'],
  ['X18', 'moment shots taken while measured are not checked against the task', 'lib/runner.mjs', '    if (missing.length) throw new RefusedClaim(', '    if (false) throw new RefusedClaim(', 'test/operator.test.mjs test/guard.test.mjs test/tasks.test.mjs'],
];
const only = process.argv.slice(2);
const chosen = CRITIC.filter(([id]) => !only.length || only.includes(id));
const root = fs.mkdtempSync(path.join(os.tmpdir(), 'critic-r9-mut-'));
const dest = path.join(root, 'gauntlet', 'compare');
fs.mkdirSync(path.dirname(dest), { recursive: true });
fs.cpSync(HARNESS, dest, { recursive: true, filter: s => !s.includes(`${path.sep}data${path.sep}out`) });
fs.cpSync(path.join(REPO, 'gauntlet', 'ratchet.json'), path.join(root, 'gauntlet', 'ratchet.json'));
fs.cpSync(path.join(REPO, 'gauntlet', 'reference'), path.join(root, 'gauntlet', 'reference'), { recursive: true });
if (fs.existsSync(path.join(HARNESS, 'data', 'out'))) fs.symlinkSync(path.join(HARNESS, 'data', 'out'), path.join(dest, 'data', 'out'));
const files = fs.readdirSync(path.join(dest, 'test')).filter(f => f.endsWith('.test.mjs') && !/live-odoo|zz-critic/.test(f)).map(f => `test/${f}`);
function suite(fs_ = files) {
  const t = Date.now();
  const r = spawnSync(process.execPath, ['--test', '--test-reporter=tap', '--test-concurrency=1', ...fs_], { cwd: dest, encoding: 'utf8', timeout: 40 * 60_000, maxBuffer: 1 << 28 });
  const res = new Map();
  for (const line of (r.stdout || '').split('\n')) { const x = tapResult(line); if (x) res.set(x.name, res.has(x.name) ? res.get(x.name) && x.passed : x.passed); }
  return { res, secs: Math.round((Date.now() - t) / 1000) };
}
const need = [...new Set(chosen.flatMap(c => c[5].split(' ')))];
const control = suite(need);
const cfail = [...control.res].filter(([, v]) => !v).map(([k]) => k);
console.log(`control (${need.join(' ')}): ${control.res.size} tests, ${cfail.length} failing (${cfail.slice(0, 5).join('; ')}), ${control.secs} s`);
for (const [id, what, file, text, repl, tf] of chosen) {
  const p = path.join(dest, file);
  const orig = fs.readFileSync(p, 'utf8');
  if (!orig.includes(text)) { console.log(`${id} ${what}: TEXT NOT FOUND`); continue; }
  fs.writeFileSync(p, orig.replace(text, repl));
  let m;
  try { m = suite(tf.split(' ')); } finally { fs.writeFileSync(p, orig); }
  const caughtBy = [...m.res].filter(([k, v]) => !v && control.res.get(k) === true).map(([k]) => k);
  console.log(`${id} ${what}: ${caughtBy.length ? `caught by ${caughtBy.length} (${caughtBy.slice(0, 3).join(' | ')})` : 'MISSED'} [${m.res.size} tests, ${m.secs} s]`);
}
fs.rmSync(root, { recursive: true, force: true });
