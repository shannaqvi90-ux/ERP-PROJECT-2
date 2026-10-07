// Instrument mutations (round 7): each one removes or weakens one defence of the measuring
// instrument, in a scratch copy of the harness, and runs the self-tests that must catch it. A
// mutation the tests miss is a defence nothing checks (the round 6 critic found four: no greyscale,
// no click refusal, no Socket#connect lock, no freeze).
//
//   node scripts/mutations.mjs            every mutation
//   node scripts/mutations.mjs M5 M7      only those
//
// Exit code 1 when any mutation is missed. Takes some minutes (each runs a browser).
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HARNESS = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const REPO = path.resolve(HARNESS, '..', '..');

/** [id, what, file, text, replacement, test file, test name pattern] */
export const MUTATIONS = [
  ['M1', 'the keystroke operator K changed', 'lib/klm.mjs', 'K: 0.28', 'K: 0.2', 'test/klm.test.mjs', ''],
  ['M2', 'no greyscale in the blind shots', 'lib/blind.mjs', 'html { filter: grayscale(100%) !important; }', 'html { }', 'test/operator.test.mjs', 'blind screenshots paint'],
  ['M3', 'a click allowed in the read world', 'lib/page-script.mjs', '|item|namedItem|', '|item|click|namedItem|', 'test/page-script.test.mjs', 'refuses and reports a click'],
  ['M4', 'no Socket#connect lock', 'lib/sandbox/lockdown.mjs', "lock(net.Socket.prototype, 'connect', refuse('opening a network connection (net.Socket#connect)'));", '', 'test/sandbox.test.mjs', 'every way to open a socket'],
  ['M5', 'no freeze at all when the clock stops', 'lib/runner.mjs', '      thaw = await freezePages(context, { beforeAbort: async () => { atClock = await PageWorld.of(page).fingerprint().catch(() => null); } });',
    '      atClock = await PageWorld.of(page).fingerprint().catch(() => null);', 'test/page-script.test.mjs', 'live ticker|plant T3|plant T4'],
  ['M6', "the demo tenant's code not masked", 'lib/blind.mjs', "identityWords: ['alnoor', ", 'identityWords: [', 'test/blind.test.mjs', 'identity codes are masked'],
  ['M7', 'no abort of what is still loading', 'lib/runner.mjs', "  for (const cdp of sessions) await cdp.send('Page.stopLoading').catch(() => {});", '', 'test/page-script.test.mjs', 'the freeze holds|plant T3'],
  ['M8', 'no script freeze (abort only)', 'lib/runner.mjs', "      await cdp.send('Emulation.setScriptExecutionDisabled', { value: true });\n      sessions.push(cdp);", '      sessions.push(cdp);',
    'test/page-script.test.mjs', 'the freeze holds|live ticker|plant T4'],
  ['M9', 'no source check of page functions', 'lib/guard.mjs', '    try { checkPageScript(text); } catch (e) {', '    try { } catch (e) {', 'test/page-script.test.mjs', 'an async function \\(S1\\)'],
  ['M10', 'the read world not armed', 'lib/page-script.mjs', "  for (const name of ownKeys(g)) {\n    if (typeof name !== 'string' || setHas(ES, name)) continue;",
    "  for (const name of []) {\n    if (typeof name !== 'string' || setHas(ES, name)) continue;", 'test/page-script.test.mjs', 'stays armed after a call returns'],
  ['M11', 'no screen check after verify()', 'lib/runner.mjs', '      if (change) throw new ActionOutsideClock(', '      if (false) throw new ActionOutsideClock(', 'test/page-script.test.mjs', 'plant T6'],
  ['M12', "a variant's own hooks ignored", 'lib/runner.mjs', '  if (own?.hooks) driver = ', '  if (false) driver = ', 'test/page-script.test.mjs', 'own set-up and sign-in'],
  ['M13', 'no document settle', 'lib/runner.mjs', '      const loaded = tracker ? await op.settleDocument({ timeout }) : null;', '      const loaded = null;', 'test/page-script.test.mjs', 'still loading when run'],
  ['M14', "a product's shots mask only its own names", 'lib/blind.mjs', "identity: unionOf('identity'),", 'identity: [...(b.identity || [])],', 'test/blind.test.mjs', 'every product'],
  ['M15', 'the lint ignores page functions', 'test/drivers-lint.test.mjs', '  problems.push(...lintParsed(src));', '', 'test/drivers-lint.test.mjs', 'catches planted escapes'],
];

function copyHarness() {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'compare-mutations-'));
  const dest = path.join(root, 'gauntlet', 'compare');
  fs.mkdirSync(path.dirname(dest), { recursive: true });
  fs.cpSync(HARNESS, dest, { recursive: true, filter: src => !src.includes(`${path.sep}data${path.sep}out`) });
  fs.cpSync(path.join(REPO, 'gauntlet', 'ratchet.json'), path.join(root, 'gauntlet', 'ratchet.json'));
  fs.cpSync(path.join(REPO, 'gauntlet', 'reference'), path.join(root, 'gauntlet', 'reference'), { recursive: true });
  // The generated dataset is shared read-only (it is large and the same everywhere).
  const data = path.join(HARNESS, 'data', 'out');
  if (fs.existsSync(data)) fs.symlinkSync(data, path.join(dest, 'data', 'out'));
  return { root, dest };
}

const only = process.argv.slice(2);
const { root, dest } = copyHarness();
let missed = 0;
try {
  for (const [id, what, file, text, replacement, testFile, pattern] of MUTATIONS) {
    if (only.length && !only.includes(id)) continue;
    const p = path.join(dest, file);
    const original = fs.readFileSync(p, 'utf8');
    if (!original.includes(text)) { console.log(`${id} ${what}: the text to mutate is gone (update scripts/mutations.mjs)`); missed++; continue; }
    fs.writeFileSync(p, original.replace(text, replacement));
    try {
      const args = ['--test', '--test-concurrency=1', ...(pattern ? [`--test-name-pattern=${pattern}`] : []), testFile];
      const r = spawnSync(process.execPath, args, { cwd: dest, encoding: 'utf8', timeout: 20 * 60_000 });
      const failing = (r.stdout.match(/^not ok/gm) || []).length;
      const passing = (r.stdout.match(/^ok/gm) || []).length;
      if (!failing) missed++;
      console.log(`${id} ${what}: ${failing ? 'caught' : 'MISSED'} (${failing} failing, ${passing} passing)`);
    } finally {
      fs.writeFileSync(p, original);
    }
  }
} finally {
  fs.rmSync(root, { recursive: true, force: true });
}
process.exit(missed ? 1 : 0);
