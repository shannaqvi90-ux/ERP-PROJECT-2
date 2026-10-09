// Critic r8: faults planted in the measuring instrument, beyond scripts/mutations.mjs M1-M20.
// Each mutation runs in a scratch copy of gauntlet/compare; a mutation is caught when any test file
// that passes unmutated fails mutated.
//   node critic-mutations.mjs <harness dir> <stage: quick|heavy|all> [ids...]
import { spawnSync, spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

export const MUTATIONS = [
  ['A1', 'a chord counted as one keystroke', 'lib/operator.mjs', "return this.#record('key', label || chord, keystrokesForChord(chord), t,", "return this.#record('key', label || chord, 1, t,"],
  ['A2', 'fill counts its click uncounted (one step instead of two)', 'lib/operator.mjs', "    await this.click(target, { label: `focus ${label || String(target)}` });", "    await this.#locate(target).click({ timeout: this.defaultTimeout }); this.#lastClick = this.#locate(target);"],
  ['A3', 'Shift not counted when typing', 'lib/klm.mjs', 'n += SHIFTED.test(ch) ? 2 : 1;', 'n += 1;'],
  ['A4', 'no H when the hand moves between mouse and keyboard', 'lib/klm.mjs', '  if (prevDevice && prevDevice !== device) ops.H += 1;', ''],
  ['A5', 'KLM continuation across a new screen (round 5 rule removed)', 'lib/klm.mjs', '  if (prev.screen != null && step.screen != null && prev.screen !== step.screen) return false;', ''],
  ['A6', 'operator M changed', 'lib/klm.mjs', 'M: 1.35 }', 'M: 1.0 }'],
  ['A7', 'clock stops at run() return, product answer not settled', 'lib/runner.mjs', '      const end = tracker ? await op.settle(tracker, { timeout }) : null;', '      const end = null;'],
  ['A8', 'machine clock runs at half speed (wrong timer)', 'lib/operator.mjs', '    return (end - this.#t0) / 1000;', '    return (end - this.#t0) / 2000;'],
  ['A9', 'waits inside the clock not recorded as system wait', 'lib/operator.mjs', '    if (this.measuring) this.#waits.push(w);', ''],
  ['A10', 'paste of set-up clipboard allowed', 'lib/operator.mjs', '    if (isPaste(chord) && !this.#copied) {', '    if (false) {'],
  ['A11', 'control characters allowed in op.type', 'lib/operator.mjs', '    if (control) throw new UncountedAction(', '    if (false) throw new UncountedAction('],
  ['A12', 'task already done before the clock accepted', 'lib/runner.mjs', "      if (before?.verified === true) throw new ActionOutsideClock(", "      if (false) throw new ActionOutsideClock("],
  ['A13', 'declared moments need not be shot', 'lib/runner.mjs', '    if (missing.length) throw new RefusedClaim(', '    if (false) throw new RefusedClaim('],
  ['A14', 'verify() waiting meter off', 'lib/runner.mjs', "      if (waited) throw new ActionOutsideClock(", "      if (false) throw new ActionOutsideClock("],
  ['A15', 'a failed ours run is usable in a comparison', 'lib/runner.mjs', "  const usable = r => r && r.status === 'verified';", "  const usable = r => r && r.status !== 'not_built';"],
  ['A16', "reference measured on its worst path per metric", 'lib/runner.mjs', '      const best = verified.reduce((b, e) => (b === null || e.counts[m] < b.counts[m] ? e : b), null);', '      const best = verified.reduce((b, e) => (b === null || e.counts[m] > b.counts[m] ? e : b), null);'],
  ['A17', 'median of repeats replaced by the fastest run', 'lib/runner.mjs', "const med = xs => { const s = [...xs].sort((a, b) => a - b); const m = s.length >> 1; return s.length % 2 ? s[m] : round((s[m - 1] + s[m]) / 2); };", "const med = xs => Math.min(...xs);"],
  ['A18', 'request body not counted as typed', 'lib/operator.mjs', "keystrokesForText(typed) + 1, t, { text: typed,", "keystrokesForText(`${verb} ${urlPath}`) + 1, t, { text: typed,"],
  ['A19', 'zero rule leaves out a count when only the reference is 0', 'lib/runner.mjs', '} else if (COUNT_METRICS.includes(m) && a === 0 && b === 0) {', '} else if (COUNT_METRICS.includes(m) && b === 0) {'],
  ['A20', 'start screen may hold typed text', 'lib/start.mjs', "    for (const f of state.filled) problems.push(`the start screen already holds typed text", "    for (const f of []) problems.push(`the start screen already holds typed text"],
  ['A21', 'rig volume bar lowered to 1,000', 'lib/rig-volume.mjs', 'export const MIN_ROWS_PER_MAIN_LIST = 100_000;', 'export const MIN_ROWS_PER_MAIN_LIST = 1_000;'],
  ['A22', 'blind shot title/favicon not neutralised', 'lib/blind.mjs', null, null],
  ['A23', 'page.click allowed while measured (guard read-only off)', 'lib/guard.mjs', '        if (readOnly() && !ALLOWED_WHILE_MEASURED[cls]?.has(prop)) {', '        if (false) {'],
  ['A24', 'requests after the clock not aborted', 'lib/runner.mjs', "      await context.route('**/*', r => { tracker.afterClock++; r.abort('blockedbyclient').catch(() => {}); });", ''],
  ['A25', 'identity words not masked at all (blindness leak)', 'lib/blind.mjs', '  if (branding.identityWords?.length) locs.push(page.getByText(identityWordPattern(branding.identityWords)));', ''],
  ['A26', 'scroll counted as no step', 'lib/operator.mjs', "    return this.#record('scroll', label || `scroll to ${String(target)}`, 0, t);", "    return { kind: 'scroll' };"],
  ['A27', 'file pick dialog click uncounted', 'lib/operator.mjs', "    this.#record('click', `open file dialog: ${label || String(opener)}`, 0, t);", ''],
  ['H1', 'the address object reachable from a page function', 'lib/page-script.mjs', "        if (n.name === 'location') {", "        if (false) {"],
  ['H3', 'Locator.press allowed while measured (uncounted key)', 'lib/guard.mjs', "'getAttribute', 'allTextContents', 'allInnerTexts', 'boundingBox', 'waitFor',", "'getAttribute', 'allTextContents', 'allInnerTexts', 'boundingBox', 'waitFor', 'press', 'fill',"],
  ['H9', 'Playwright internals reachable from a driver', 'lib/guard.mjs', "      if (typeof prop === 'string' && prop.startsWith('_')) throw new UncountedAction(", "      if (false) throw new UncountedAction("],
  ['H10', 'verify() reads not clamped to 0.5 s', 'lib/guard.mjs', "          return phase === 'verifying' ? clampTimeouts(raw) : raw;", "          return raw;"],
];

const QUICK = ['licences', 'klm', 'ratchet', 'compare', 'drivers-lint', 'baselines', 'tasks', 'rig-volume', 'blind', 'cli', 'dataset', 'operator', 'failure-capture', 'sign-in-limit'].map(n => `test/${n}.test.mjs`);
const HEAVY = ['sandbox', 'page-script', 'guard'].map(n => `test/${n}.test.mjs`);

function copyHarness(harness) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'critic-r8-mut-'));
  const dest = path.join(root, 'gauntlet', 'compare');
  fs.mkdirSync(path.dirname(dest), { recursive: true });
  fs.cpSync(harness, dest, { recursive: true, filter: s => !s.includes(`${path.sep}data${path.sep}out`) });
  const repo = path.resolve(harness, '..', '..');
  fs.cpSync(path.join(repo, 'gauntlet', 'ratchet.json'), path.join(root, 'gauntlet', 'ratchet.json'));
  fs.cpSync(path.join(repo, 'gauntlet', 'reference'), path.join(root, 'gauntlet', 'reference'), { recursive: true });
  const data = path.join(harness, 'data', 'out');
  if (fs.existsSync(data)) fs.symlinkSync(data, path.join(dest, 'data', 'out'));
  return { root, dest };
}

function runFile(dest, file) {
  return new Promise(res => {
    const p = spawn(process.execPath, ['--test', '--test-reporter=tap', file], { cwd: dest });
    let out = '';
    p.stdout.on('data', d => { out += d; });
    p.stderr.on('data', d => { out += d; });
    const timer = setTimeout(() => p.kill('SIGKILL'), 15 * 60_000);
    p.on('close', code => { clearTimeout(timer); const fails = out.split('\n').filter(l => /^\s*not ok \d+ - /.test(l)).map(l => l.trim()); res({ file, code, fails }); });
  });
}

async function runFiles(dest, files, par = 3) {
  const results = [];
  const queue = [...files];
  await Promise.all(Array.from({ length: par }, async () => { while (queue.length) results.push(await runFile(dest, queue.shift())); }));
  return results;
}

const [harness, stage = 'quick', ...ids] = process.argv.slice(2);
const files = stage === 'quick' ? QUICK : stage === 'heavy' ? HEAVY : [...QUICK, ...HEAVY];
const chosen = MUTATIONS.filter(m => (!ids.length || ids.includes(m[0])) && m[3]);
// Control: the same files unmutated in a scratch copy; a test failing there never counts as a catch.
const ctl = copyHarness(path.resolve(harness));
const control = new Set((await runFiles(ctl.dest, files)).flatMap(r => r.fails));
fs.rmSync(ctl.root, { recursive: true, force: true });
console.log(`control: ${control.size} test(s) fail unmutated in the scratch copy: ${[...control].join(' | ')}`);
for (const [id, what, file, text, replacement] of chosen) {
  const { root, dest } = copyHarness(path.resolve(harness));
  const p = path.join(dest, file);
  const src = fs.readFileSync(p, 'utf8');
  if (!src.includes(text)) { console.log(`${id} ${what}: TEXT NOT FOUND`); fs.rmSync(root, { recursive: true, force: true }); continue; }
  fs.writeFileSync(p, src.replace(text, replacement));
  const t = Date.now();
  const rs = await runFiles(dest, files);
  const caught = rs.map(r => ({ file: r.file, fails: r.fails.filter(f => !control.has(f)) })).filter(r => r.fails.length);
  console.log(`${id} ${what}: ${caught.length ? 'CAUGHT' : 'MISSED'} [${stage}] in ${Math.round((Date.now() - t) / 1000)} s` + (caught.length ? ` by ${caught.map(r => `${r.file} (${r.fails.slice(0, 2).join(' | ')})`).join('; ')}` : ''));
  fs.rmSync(root, { recursive: true, force: true });
}
