// Critic p01 r12: own instrument mutations (counting, timer, blindness), each in a scratch copy,
// judged against a control run of the same test files. A test counts only if it passed unmutated.
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { tapResult } from './mutations.mjs';

const HARNESS = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const REPO = process.env.CRITIC_REPO; // the critic clone (for gauntlet/reference and ratchet.json)
const FILES = (process.env.CRITIC_FILES || 'test/klm.test.mjs test/compare.test.mjs test/baselines.test.mjs test/blind.test.mjs test/operator.test.mjs test/tasks.test.mjs test/cli.test.mjs').split(' ');

export const MINE = [
  ['K1', 'a double click modelled as a single click (P+BB)', 'lib/klm.mjs', "case 'double-click': case 'file-pick': ops.P += 1; ops.B += 4; break;", "case 'double-click': case 'file-pick': ops.P += 1; ops.B += 2; break;"],
  ['K2', 'no homing H when the first step is on the keyboard', 'lib/klm.mjs', "  if (!prevDevice && device !== 'mouse') ops.H += 1;\n", '\n'],
  ['K3', 'typing after ANY click continues it (no M), not only into the clicked field', 'lib/klm.mjs', "(isClick(prev) && step.same_field === true)", 'isClick(prev)'],
  ['K4', 'Enter continues any step (no M)', 'lib/klm.mjs', "if (step.chord === 'Enter') return prev.kind === 'type' ||", "if (step.chord === 'Enter') return true ||"],
  ['K5', 'an API request: the Enter that sends it uncounted', 'lib/operator.mjs', "keystrokesForText(typed) + 1, t,", 'keystrokesForText(typed), t,'],
  ['K6', 'a browser key (F5, Alt+Left) counts no keystroke', 'lib/operator.mjs', "    await action(this.#page);\n    return this.#record('key', label || chord, keystrokesForChord(chord), t, { chord });", "    await action(this.#page);\n    return this.#record('key', label || chord, 0, t, { chord });"],
  ['K7', 'machine seconds read 0.25 s short (wrong timer)', 'lib/operator.mjs', 'get machineSeconds() { return round(this.now()); }', 'get machineSeconds() { return round(Math.max(0, this.now() - 0.25)); }'],
  ['K8', 'human+wait leaves out the system wait', 'lib/operator.mjs', 'human_plus_wait_seconds: round(klm.human_seconds + wait),', 'human_plus_wait_seconds: round(klm.human_seconds),'],
  ['K9', 'blind letters fixed: ours is always A (blindness leak)', 'lib/review.mjs', "const letters = assignLetters(['ours', 'odoo'], random);", "const letters = { ours: 'A', odoo: 'B' };"],
  ['K10', 'shift not counted in typed text', 'lib/klm.mjs', 'n += SHIFTED.test(ch) ? 2 : 1;', 'n += 1;'],
  ['K11', 'a step\'s keystrokes ignored in the summary for type steps', 'lib/operator.mjs', "keystrokes: steps.reduce((s, x) => s + x.keystrokes, 0),", "keystrokes: steps.reduce((s, x) => s + (x.kind === 'type' ? 0 : x.keystrokes), 0),"],
  ['K12', 'blind shot name carries the product (blindness leak)', 'lib/operator.mjs', "const file = blindName(this.shotFormat === 'png' ? 'png' : 'jpg');", "const file = `${(this.branding.words[0] || 'x')}-` + blindName(this.shotFormat === 'png' ? 'png' : 'jpg');"],
];

function copy() {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'critic-r12-mut-'));
  const dest = path.join(root, 'gauntlet', 'compare');
  fs.mkdirSync(path.dirname(dest), { recursive: true });
  fs.cpSync(HARNESS, dest, { recursive: true, filter: s => !s.includes(`${path.sep}data${path.sep}out`) });
  fs.cpSync(path.join(REPO, 'gauntlet', 'ratchet.json'), path.join(root, 'gauntlet', 'ratchet.json'));
  fs.cpSync(path.join(REPO, 'gauntlet', 'reference'), path.join(root, 'gauntlet', 'reference'), { recursive: true });
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
