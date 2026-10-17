// Instrument mutations (round 7): each one removes or weakens one defence of the measuring
// instrument, in a scratch copy of the harness, and runs the self-tests that must catch it. A
// mutation the tests miss is a defence nothing checks (the round 6 critic found four: no greyscale,
// no click refusal, no Socket#connect lock, no freeze).
//
//   node scripts/mutations.mjs            every mutation
//   node scripts/mutations.mjs M5 M7      only those
//
// Each mutation's self-tests run unmutated first and must pass (the control). The controls of one
// test file run together, once (round 8: one control per mutation cost a browser start each and put
// ./erp verify over its processor-time maximum); each test is judged by name, so a mutation is
// caught only when a test that passed unmutated fails mutated. Exit code 1 when any mutation is
// missed or its control fails. Takes a few minutes (most runs start a browser).
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

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
  ['M16', "a name cut short by its cell painted with its whole box", 'lib/blind.mjs', '      return ar.height <= 2 * r.height + 1 ? depth : 0;', '      return 0;', 'test/blind.test.mjs', 'cut short by its list cell'],
  // Round 8: the owner's zero rule (needs-human #11) and whole paths (p00 critic, sign-in).
  ['M17', 'the zero rule leaves out a time metric too', 'lib/runner.mjs', '} else if (COUNT_METRICS.includes(m) && a === 0 && b === 0) {', '} else if (a === 0 && b === 0) {', 'test/compare.test.mjs', 'zero rule: count metrics only'],
  ['M18', 'any tie on a count metric left out, not only both at 0', 'lib/runner.mjs', '} else if (COUNT_METRICS.includes(m) && a === 0 && b === 0) {', '} else if (COUNT_METRICS.includes(m) && a === b) {', 'test/compare.test.mjs', 'zero rule: any other tie'],
  ['M19', 'a tie counted as a win', 'lib/runner.mjs', "outcome: a < b ? 'win' : a === b ?", "outcome: a <= b ? 'win' : a === b ?", 'test/compare.test.mjs', 'zero rule: when every metric ties'],
  ['M20', "ours judged on the best of each metric across its paths", 'lib/runner.mjs', '  if (variants.length) return variants.map(', '  if (false) return variants.map(', 'test/compare.test.mjs', 'whole paths: ours wins only'],
  // Round 8 (p00): the person's passkey device.
  ['M21', 'the device answers without the person once the clock runs', 'lib/device.mjs', "    if (currentPhase() === 'free') return Promise.resolve(true);", '    return Promise.resolve(true);', 'test/operator.test.mjs', 'passkey: set-up makes'],
  ['M22', 'a confirmation on the device modelled as free', 'lib/klm.mjs', "    case 'device': ops.K += 1; break;", "    case 'device': break;", 'test/operator.test.mjs', 'passkey: set-up makes'],
  ['M23', 'the shim holds only the container, not its prototype', 'lib/device.mjs', '    Object.defineProperty(proto, kind, { value: held, writable: false, configurable: false });',
    '    Object.defineProperty(navigator.credentials, kind, { value: held, writable: false, configurable: false });', 'test/operator.test.mjs', 'cannot answer for the person'],
  // Round 9 (critic p01 r8, biggest gap): set-up cannot do the task off the clock.
  ['M24', 'a verify() process keeps its clocks', 'lib/sandbox/lockdown.mjs', "if (process.env.COMPARE_DRIVER_ROLE === 'verify') freezeClocks(", 'if (false) freezeClocks(', 'test/sandbox.test.mjs', 'a verify\\(\\) process'],
  ['M25', 'a verify() process reads anything (the run\'s scratch folder too)', 'lib/sandbox/bridge.mjs', "const read = reads ? [...new Set(reads)].map(p => `--allow-fs-read=${p}`) : ['--allow-fs-read=*'];",
    "const read = ['--allow-fs-read=*'];", 'test/before-clock.test.mjs', 'plant P4'],
  ['M26', 'a measured part with no counted step accepted', 'lib/runner.mjs', '    if (op.steps.length === 0) throw new RefusedClaim(', '    if (false) throw new RefusedClaim(', 'test/before-clock.test.mjs', 'plant P1 \\(|plant P2'],
  ['M27', 'no saved-state check', 'lib/runner.mjs', '      if (task.saves && run.verification?.verified) {', '      if (false) {', 'test/before-clock.test.mjs', 'plant P6b'],
  ['M28', 'a saved change need not hold what the person entered', 'lib/runner.mjs', '  else if (!gained.length) problem =', '  else if (false) problem =', 'test/before-clock.test.mjs', 'plant P7'],
  ['M29', 'the two passes after the clock back to back', 'lib/runner.mjs', 'export const VERIFY_PASS_GAP_MS = 1100;', 'export const VERIFY_PASS_GAP_MS = 0;', 'test/before-clock.test.mjs', 'honest path of a task that saves'],
  ['M30', 'an entered value on the start screen accepted', 'lib/runner.mjs', '      if (shown) throw new ActionOutsideClock(`unfair start state: the start screen already shows', '      if (false) throw new ActionOutsideClock(`unfair start state: the start screen already shows',
    'test/before-clock.test.mjs', 'plant P8'],
  ['M31', 'a keyboard-only task not judged by the harness', 'lib/runner.mjs', '  if (task.keyboardOnly) {', '  if (false) {', 'test/before-clock.test.mjs', 'keyboard-only task fails'],
  ['M32', 'the product\'s Date header reaches verify()', 'lib/sandbox/bridge.mjs', "const clockHeaders = this.host.role === 'verify' ? CLOCK_HEADERS : new Set();", 'const clockHeaders = new Set();', 'test/sandbox.test.mjs', 'a verify\\(\\) process'],
  ['M33', 'a verify() process may write (a file\'s time is a clock)', 'lib/sandbox/bridge.mjs', 'const write = reads ? [] : [`--allow-fs-write=${scratch}`];', 'const write = [`--allow-fs-write=${scratch}`];', 'test/sandbox.test.mjs', 'a verify\\(\\) process'],
  // Round 9: the critic's own mutations that no self-test caught (A1, A16, A26, A24, H10).
  ['M34', 'a key chord counted as one keystroke (critic A1)', 'lib/operator.mjs', "keystrokesForChord(chord), t, { chord, ...(copied", "1, t, { chord, ...(copied", 'test/operator.test.mjs', 'a key chord counts each'],
  ['M35', 'a scroll records no step (critic A26)', 'lib/operator.mjs', "    return this.#record('scroll', label || `scroll to ${String(target)}`, 0, t, {}, undefined, began);", "    return { kind: 'scroll' };", 'test/operator.test.mjs', 'a key chord counts each'],
  ['M36', 'the reference measured on its worst path per metric (critic A16)', 'lib/runner.mjs', '(b === null || e.counts[m] < b.counts[m] ? e : b)', '(b === null || e.counts[m] > b.counts[m] ? e : b)', 'test/compare.test.mjs', 'held at its best path'],
  ['M37', 'requests after the clock not refused (critic A24)', 'lib/runner.mjs', "      await context.route('**/*', r => { tracker.afterClock++; r.abort('blockedbyclient').catch(() => {}); });", '', 'test/page-script.test.mjs', 'mutation A24'],
  ['M38', 'reads in verify() not cut to half a second (critic H10)', 'lib/guard.mjs', "          return phase === 'verifying' ? clampTimeouts(raw) : raw;", '          return raw;', 'test/page-script.test.mjs', 'mutation H10'],
  ['M41', "run()'s own word taken for the end state when a driver has no verify()", 'lib/runner.mjs', "      throw new RefusedClaim('the driver has no verify():", "      if (false) throw new RefusedClaim('the driver has no verify():", 'test/before-clock.test.mjs', 'plant P10'],
  // Round 9: blindness (critic p01 r8: names inside form fields; an unbuilt product's column).
  ['M39', 'demo names inside form fields not masked', 'lib/blind.mjs', '  else values.forEach((v, i) => { if (revealsIdentity(v, branding)) out.push(fields.nth(i)); });', '  else values.forEach(() => {});', 'test/blind.test.mjs', 'inside a form field'],
  ['M40', "an unbuilt product's column shown", 'lib/review.mjs', "    if (Object.values(runs).some(r => r?.status === 'not_built')) {", '    if (false) {', 'test/blind.test.mjs', 'cannot run yet'],
  // Round 10 (critic p01 r9, biggest gap): a task that saves is saved by the measured part, in its declared end state.
  ['M42', 'any back-end change accepted as the saved state (critic Q1)', 'lib/runner.mjs', '  else if (!endChanged.length) problem =', '  else if (false) problem =', 'test/before-clock.test.mjs', 'saved state \\(round 10\\)'],
  ['M43', "any part of the end-state read accepted (not only its declared parts)", 'lib/runner.mjs', '    const ps = parts.get(k).filter(p => specs.some(x => isEndStatePart(x, p)));', '    const ps = specs.length ? parts.get(k) : [];', 'test/before-clock.test.mjs', 'saved state \\(round 10\\)'],
  ['M44', 'an entered value need not arrive in the end state', 'lib/runner.mjs', '  else if (values.length && !gainedValues.length) problem =', '  else if (false) problem =', 'test/before-clock.test.mjs', 'saved state \\(round 10\\)'],
  ['M45', 'a measured part that writes nothing accepted (critic Q2)', 'lib/runner.mjs', '  if (!sent.length && !apiWrites.length) {', '  if (false) {', 'test/before-clock.test.mjs', 'measured part \\(round 10\\)'],
  ['M46', "the task's declared writes not required", 'lib/runner.mjs', '  if (writes) {', '  if (false) {', 'test/before-clock.test.mjs', 'measured part \\(round 10\\)'],
  ['M47', 'an entered value the measured part never entered accepted', 'lib/runner.mjs', '  if (missing.length) {', '  if (false) {', 'test/before-clock.test.mjs', 'measured part \\(round 10\\)'],
  ['M48', "set-up's browser writes not waited for before the start", 'lib/runner.mjs', '  if (setUpWrites) await setUpWrites.settle(timeout);', '', 'test/set-up-off-clock.test.mjs', "control: set-up's own save"],
  ['M49', "a write set-up's browser abandoned accepted", 'lib/runner.mjs', '      if (abandoned.length) return', '      if (false) return', 'test/set-up-off-clock.test.mjs', 'plant Q2b'],
  ['M50', "a write abandoned by set-up's page moving on not seen", 'lib/runner.mjs', "      p.on('framenavigated', f => { if (!docs.isNewDocument(f)) return; for (const r of [...inflight.keys()]) if (frameOf(r) === f && !r.isNavigationRequest()) abandon(r, 'its page moved on'); });", '', 'test/set-up-off-clock.test.mjs', 'plant Q2 \\('],
  ['M51', 'a browser context the driver opened in set-up not watched', 'lib/runner.mjs', '    attach(c);\n    return c;', '    return c;', 'test/set-up-off-clock.test.mjs', 'plant Q2c'],
  ['M52', "set-up's local storage carried into a signed-in start (critic X12)", 'lib/runner.mjs', "  const storageState = kind === 'sign-in' ? saved : { cookies: saved.cookies, origins: [] };", '  const storageState = saved;', 'test/set-up-off-clock.test.mjs', 'X12'],
  ['M53', 'the clipboard not emptied at the start (critic X16)', 'lib/runner.mjs', '    await resetClipboard(scratch);', '', 'test/set-up-off-clock.test.mjs', 'X16'],
  // Round 11 (critic p01 r10, biggest gap): the page's own requests are not the person's doing. Round 12:
  // the plants are refused by the browser's record as well, so the timing layer's own mutations (M56,
  // M57) are judged by its unit test.
  ['M54', 'a value in a request the page composed counts as entered (critic R1)', 'lib/runner.mjs', "    ...steps.filter(s => s.kind === 'request').map(s => String(s.text ?? ''))];",
    "    ...steps.filter(s => s.kind === 'request').map(s => String(s.text ?? '')), ...sent.map(r => `${r.url ?? ''}\\n${r.body ?? ''}`)];", 'test/measured-part.test.mjs', 'plant R1 '],
  ['M55', 'a write the page sent on its own counts as the measured part\'s (critic R2)', 'lib/runner.mjs', '  const caused = causes.filter(c => c.caused).map(c => c.write);', '  const caused = causes.map(c => c.write);', 'test/measured-part.test.mjs', 'plant R2 '],
  ['M56', 'no end to the time a step may take to cause a write', 'lib/runner.mjs', 'export const REACTION_SECONDS = 1;', 'export const REACTION_SECONDS = 60;', 'test/before-clock.test.mjs', 'caused by a step \\(round 11\\)'],
  ['M57', 'any answered request links a write to a step (a poll chains on)', 'lib/runner.mjs', '  const links = timeline.filter(q => typeof q.at === \'number\' && typeof q.answered === \'number\' && during(q.at)).map(q => ({ q, step: during(q.at) }));',
    '  const links = timeline.filter(q => typeof q.at === \'number\' && typeof q.answered === \'number\').map(q => ({ q, step: during(q.at) || windows[0] }));', 'test/before-clock.test.mjs', 'caused by a step \\(round 11\\)'],
  ['M62', "a declared write the page sent on its own accepted beside a decoy a step caused", 'lib/runner.mjs', '    if (stray.length) return', '    if (false) return', 'test/measured-part.test.mjs', 'plant R2d'],
  // Round 11: set-up's own writes (critic X1; routed from p00 r10 and p02 r10).
  ['M58', "a write set-up's page aborted not seen as abandoned (critic X1)", 'lib/runner.mjs', " context.on('requestfinished', finished); context.on('requestfailed', failed);", " context.on('requestfinished', finished);", 'test/measured-part.test.mjs', 'critic X1'],
  ['M59', "an address change of set-up's page taken for a new document", 'lib/runner.mjs', "      p.on('framenavigated', f => { if (!docs.isNewDocument(f)) return; for (const r of [...inflight.keys()])", "      p.on('framenavigated', f => { for (const r of [...inflight.keys()])",
    'test/measured-part.test.mjs', 'only changes its address'],
  ['M60', 'an address change inside the clock drops a save still under way', 'lib/runner.mjs', '  const navigated = f => { if (!docs.isNewDocument(f)) return; for (const r of [...inflight, ...loading])', '  const navigated = f => { for (const r of [...inflight, ...loading])', 'test/measured-part.test.mjs', 'control C4'],
  ['M61', 'a request taken for a long-lived channel by its address (critic r10)', 'lib/runner.mjs', "  try { return ['websocket', 'eventsource'].includes(r.resourceType()); } catch { return false; }",
    "  try { return ['websocket', 'eventsource'].includes(r.resourceType()) || /websocket|longpolling|\\/bus\\//i.test(r.url()); } catch { return false; }", 'test/measured-part.test.mjs', 'names a long-lived channel'],
  // Round 12 (critic p01 r11, biggest gap): the browser's record of which task sent each write (lib/causation.mjs).
  ['M63', 'an input task before every step credited to a step', 'lib/causation.mjs', "return s ? yes(s.n, [`input of step ${s.n}`]) : no('an input event before the first counted step');", "return yes(s?.n ?? 1, ['input']);", 'test/causation.test.mjs', 'an input task is the step'],
  ['M64', "the harness's DevTools commands outside every step credited", 'lib/causation.mjs', "return s ? yes(s.n, [`a DevTools command of step ${s.n}`]) : no(", "return true ? yes(s?.n ?? 1, ['a DevTools command']) : no(", 'test/causation.test.mjs', 'a DevTools command inside'],
  ['M65', 'a repeating timer (a poll) accepted', 'lib/causation.mjs', '  if (i.singleShot === false) return no(', '  if (false) return no(', 'test/causation.test.mjs', 'a timer counts only'],
  ['M66', 'no end to the delay a step may arm (critic R2b)', 'lib/causation.mjs', '  if ((i.timeout || 0) > ctx.reaction * 1000) return no(', '  if (false) return no(', 'test/causation.test.mjs', 'a timer counts only'],
  ['M67', 'a timer armed before the clock credited (critic S1)', 'lib/causation.mjs', "  if (!install) return no('a timer armed before the clock started');", '  if (!install) return yes(1, []);', 'test/causation.test.mjs', 'a timer counts only'],
  ['M68', 'an animation frame asked for by no step credited', 'lib/causation.mjs', "  return c.caused ? yes(c.step, [...c.chain, 'an animation frame']) : noFrom(", "  return true ? yes(c.step ?? 1, [...c.chain, 'an animation frame']) : noFrom(", 'test/causation.test.mjs', 'an animation frame counts'],
  ['M69', 'no end to how long an answer may hold a write back (critic S2)', 'lib/causation.mjs', '    if (held > ctx.link) return no(', '    if (false) return no(', 'test/causation.test.mjs', 'an answer links'],
  ['M70', "a poll's answer links a write to a step (critic S3)", 'lib/causation.mjs', '    if (!c.caused) return noFrom(`it followed the answer to', '    if (false) return noFrom(`it followed the answer to', 'test/causation.test.mjs', 'an answer links'],
  ['M71', 'one caused answer enough in a task that delivered several', 'lib/causation.mjs', '  for (const id of ids) {', '  for (const id of ids.slice(0, 1)) {', 'test/causation.test.mjs', 'an answer links'],
  ['M72', 'a message after a page task no step caused credited (plant S5)', 'lib/causation.mjs', '    if (!pc.caused) result = noFrom(', '    if (false) result = noFrom(', 'test/causation.test.mjs', 'no traceable origin'],
  ['M73', "no guard against a page task just before the step's", 'lib/causation.mjs', '      if (ec && !ec.caused) result = noFrom(', '      if (false) result = noFrom(', 'test/causation.test.mjs', 'no traceable origin'],
  ['M74', "a worker of the page running meanwhile ignored (plant S8)", 'lib/causation.mjs', '      else if (worker) result = no(', '      else if (false) result = no(', 'test/causation.test.mjs', 'no traceable origin'],
  ['M75', 'no end to how long after a step a message may follow', 'lib/causation.mjs', '    else if ((task.ts - prev.end) / 1e6 > ctx.reaction) result = no(', '    else if (false) result = no(', 'test/causation.test.mjs', 'no traceable origin'],
  ['M76', "the harness reading the page taken for the page's own task", 'lib/causation.mjs', '  const prevIdx = before.findIndex(t => t.activity || (t.protocol && stepHolding(ctx, t.ts)));', '  const prevIdx = before.findIndex(t => t.activity || t.protocol);', 'test/causation.test.mjs', 'no traceable origin'],
  ['M77', 'the clock-sync markers ignored', 'lib/causation.mjs', '.map(m => ({ offset: byId.get(m.id) - (m.before + m.after) / 2,', '.map(m => ({ offset: 0,', 'test/causation.test.mjs', 'a navigation is judged'],
  ['M78', "the browser's record not consulted (timing alone, round 11)", 'lib/runner.mjs', '    const caused = c.caused && (trace ? trace.caused : true);', '    const caused = c.caused;', 'test/causation.test.mjs', 'plant S4:'],
  ['M79', 'a click timed from when the operator began waiting for its target (critic S1)', 'lib/operator.mjs', "    const step = this.#record('click', label || String(target), 0, t, {}, undefined, began);", "    const step = this.#record('click', label || String(target), 0, began, {}, undefined, began);", 'test/causation.test.mjs', 'the clock \\(round 12'],
  ['M80', "the clock starts right after verify()'s read before it (critic S3)", 'lib/runner.mjs', '    if (task.saves) await new Promise(r => setTimeout(r, startGapMs()));', '', 'test/causation.test.mjs', 'the clock \\(round 12'],
  ['M81', "a step's label counted as typed text (critic r11, C3)", 'lib/runner.mjs', "  const texts = [typed.join(''), typed.join('\\n'),", "  const texts = [typed.join(''), typed.join('\\n'), steps.map(s => String(s.label ?? '')).join(' '),", 'test/causation.test.mjs', "a step's label never counts"],
  ['M82', 'a pointer boundary event the browser sent on its own taken for input (plant S11)', 'lib/causation.mjs', '(fromInputQueue ? INPUT_EVENTS : DISCRETE_INPUT).test(', 'INPUT_EVENTS.test(', 'test/causation.test.mjs', 'an input task is the step'],
  ['M83', 'no guard before a frame (which jumps ahead of a message the page posted)', 'lib/causation.mjs', '        : frame ? before.slice(prevIdx + 1).find(t => t.activity) : null;', '        : null;', 'test/causation.test.mjs', 'no traceable origin'],
  ['M84', "a form's submission not paired with the task that submitted it", 'lib/causation.mjs', '  if (task.formSubmission && task.submittedBy &&', '  if (false && task.submittedBy &&', 'test/causation.test.mjs', 'no traceable origin'],
  ['M85', "an answer's end recorded after the code that read it not counted", 'lib/causation.mjs', '  if (task.answers.length) return answerCause(ctx, task, task.answers, depth);', '  if (task.answers.some(a => a.ts <= point.ts)) return answerCause(ctx, task, task.answers.filter(a => a.ts <= point.ts), depth);', 'test/causation.test.mjs', 'an answer links'],
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

/** One TAP result line ("ok 3 - name", "not ok 4 - name # TODO"): passed?, name; null for a skipped test or another line. */
export function tapResult(line) {
  const m = /^\s*(not )?ok \d+ - (.*)$/.exec(line);
  if (!m) return null;
  const directive = /\s#\s*(SKIP|TODO)\b/i.exec(m[2]);
  if (directive?.[1].toUpperCase() === 'SKIP') return null;
  return { passed: !m[1], name: (directive ? m[2].slice(0, directive.index) : m[2]).trim() };
}

/** Runs the self-tests of one file (and name pattern) in the scratch copy; each test's result by name. */
function selfTests(dest, testFile, pattern) {
  // TAP named explicitly: newer Node (the toolbox's 24) prints its spec reporter by default, even to a pipe.
  const args = ['--test', '--test-reporter=tap', '--test-concurrency=1', ...(pattern ? [`--test-name-pattern=${pattern}`] : []), testFile];
  const r = spawnSync(process.execPath, args, { cwd: dest, encoding: 'utf8', timeout: 20 * 60_000 });
  const out = r.stdout || '';
  const results = new Map();
  for (const line of out.split('\n')) {
    const t = tapResult(line);
    if (t) results.set(t.name, results.has(t.name) ? results.get(t.name) && t.passed : t.passed);
  }
  const failing = [...results.values()].filter(v => !v).length;
  const passing = results.size - failing;
  const why = results.size ? '' : `no test result read (exit ${r.status}${r.error ? `, ${r.error.message}` : ''}): ${(r.stderr || out).trim().split('\n').slice(-3).join(' | ')}`;
  return { results, failing, passing, why };
}

/** The name patterns of the mutations of one test file, as one (empty when one of them is the whole file). */
export function unionPattern(patterns) {
  return patterns.some(p => !p) ? '' : patterns.map(p => `(?:${p})`).join('|');
}

/**
 * Runs the mutations named in `only` (all when empty); returns how many the self-tests missed.
 * The control: each test file's self-tests (those of its mutations' patterns, together) run once
 * on the unmutated copy, and every test a mutation runs must have passed there: a test that fails
 * anyway (no browser, a broken copy) would otherwise count as a catch.
 */
export function runMutations(only = [], log = console.log) {
  const { root, dest } = copyHarness();
  const chosen = MUTATIONS.filter(([id]) => !only.length || only.includes(id));
  const controls = new Map();
  let missed = 0;
  try {
    for (const testFile of new Set(chosen.map(m => m[5]))) {
      controls.set(testFile, selfTests(dest, testFile, unionPattern(chosen.filter(m => m[5] === testFile).map(m => m[6]))));
    }
    for (const [id, what, file, text, replacement, testFile, pattern] of chosen) {
      const p = path.join(dest, file);
      const original = fs.readFileSync(p, 'utf8');
      if (!original.includes(text)) { log(`${id} ${what}: the text to mutate is gone (update scripts/mutations.mjs)`); missed++; continue; }
      const control = controls.get(testFile);
      fs.writeFileSync(p, original.replace(text, replacement));
      let mutated;
      try {
        mutated = selfTests(dest, testFile, pattern);
      } finally {
        fs.writeFileSync(p, original);
      }
      const unjudged = [...mutated.results.keys()].filter(name => control.results.get(name) !== true);
      if (!mutated.results.size || unjudged.length) {
        missed++;
        log(`${id} ${what}: ${mutated.results.size ? `NOT JUDGED, its self-tests do not pass unmutated (${unjudged.length} of ${mutated.results.size}: ${unjudged.slice(0, 3).join('; ')})` : `MISSED (${mutated.why})`}`);
        continue;
      }
      if (!mutated.failing) missed++;
      log(`${id} ${what}: ${mutated.failing ? 'caught' : 'MISSED'} (${mutated.failing} failing, ${mutated.passing} passing)`);
    }
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
  return missed;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  const only = process.argv.slice(2);
  const missed = runMutations(only);
  const ran = only.length ? MUTATIONS.filter(m => only.includes(m[0])).length : MUTATIONS.length;
  console.log(missed ? `${missed} of ${ran} mutation(s) of the instrument MISSED by its self-tests` : `${ran} of ${MUTATIONS.length} mutations of the instrument run, every one caught`);
  process.exit(missed ? 1 : 0);
}
