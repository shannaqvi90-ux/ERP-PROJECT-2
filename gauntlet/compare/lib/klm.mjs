// Keystroke-level model (KLM) for modelled human seconds.
//
// Operator times are the published averages of Card, Moran and Newell, "The keystroke-level
// model for user performance time with interactive systems", Communications of the ACM 23(7),
// 1980, Figure 1:
//   K  0.28 s  one keystroke or button press, average non-secretary typist (40 wpm)
//   P  1.10 s  point with the mouse to a target on the screen
//   B  0.10 s  press or release the mouse button (a click is BB = 0.20 s)
//   H  0.40 s  home the hand between keyboard and mouse
//   M  1.35 s  mental preparation before a unit of action
// System response time R is not modelled: it is measured, and reported separately as
// system_wait_seconds, so a slow product cannot hide behind a fast operator model.
//
// Placement rules (a simplified form of the paper's heuristic rules, applied identically to
// both products so the comparison stays fair):
//   1. Every step starts with one M, unless the driver marks it as a continuation of the
//      previous step (`chain: true`), such as Enter right after typing a search term, or
//      typing into a field the previous click just focused.
//   2. A click is P + BB. A double click is P + BBBB. Choosing a file in the file dialog is
//      modelled as a double click (P + BBBB).
//   3. Typing text is one K per character, plus one K per character that needs Shift.
//   4. A key chord is one K per key in the chord (Control+K is two keystrokes).
//   5. One H each time the operating hand moves between mouse and keyboard.
//   6. Scrolling a target into view with the mouse wheel is one step, modelled like a click
//      (P + BB: bring the pointer to the scroll area and turn the wheel). The paper has no
//      operator for scrolling; this keeps a path that needs a scroll from looking free.
//   7. An API request (API tasks) is typed: one K per key of the request as typed (method, path
//      and query, JSON body) plus one K for Enter to send it. It is a keyboard step.

export const OPERATORS = Object.freeze({ K: 0.28, P: 1.1, B: 0.1, H: 0.4, M: 1.35 });

export const OPERATOR_SOURCE =
  'Card, Moran & Newell (1980), The keystroke-level model for user performance time with interactive systems, CACM 23(7): ' +
  'K 0.28 s (average non-secretary typist), P 1.10 s, B 0.10 s per press or release, H 0.40 s, M 1.35 s. ' +
  'System response time is measured, not modelled.';

const SHIFTED = /[A-Z~!@#$%^&*()_+{}|:"<>?]/;

/** Keys pressed to type `text`: one per character, one more for each character that needs Shift. */
export function keystrokesForText(text) {
  let n = 0;
  for (const ch of String(text)) n += SHIFTED.test(ch) ? 2 : 1;
  return n;
}

/** Keys pressed for a chord written the Playwright way, e.g. "Control+Shift+K" is 3. */
export function keystrokesForChord(chord) {
  const s = String(chord);
  // "+" alone (or a trailing "++") is the plus key itself.
  if (s === '+') return 1;
  return s.replace(/\+\+$/, '+PLUS').split('+').filter(Boolean).length;
}

/** Which hand-device a step uses. */
export function deviceOf(kind) {
  return kind === 'click' || kind === 'double-click' || kind === 'file-pick' || kind === 'scroll' ? 'mouse' : 'keyboard';
}

/**
 * Operators for one step. `prevDevice` is the device of the step before ('mouse',
 * 'keyboard' or null at the start; the operator starts with the hand on the mouse).
 * Returns { ops: {K,P,B,H,M}, seconds }.
 */
export function operatorsForStep(step, prevDevice) {
  const ops = { K: 0, P: 0, B: 0, H: 0, M: 0 };
  if (!step.chain) ops.M += 1;
  const device = deviceOf(step.kind);
  if (prevDevice && prevDevice !== device) ops.H += 1;
  if (!prevDevice && device === 'keyboard') ops.H += 1;
  switch (step.kind) {
    case 'click': case 'scroll': ops.P += 1; ops.B += 2; break;
    case 'double-click': case 'file-pick': ops.P += 1; ops.B += 4; break;
    case 'type': case 'key': case 'request': ops.K += step.keystrokes; break;
    default: throw new Error(`unknown step kind: ${step.kind}`);
  }
  return { ops, seconds: secondsFor(ops) };
}

export function secondsFor(ops) {
  return round(Object.entries(ops).reduce((s, [k, n]) => s + OPERATORS[k] * n, 0));
}

/** Totals for a list of steps. */
export function modelSteps(steps) {
  const total = { K: 0, P: 0, B: 0, H: 0, M: 0 };
  let prev = null;
  for (const step of steps) {
    const { ops } = operatorsForStep(step, prev);
    for (const k of Object.keys(total)) total[k] += ops[k];
    prev = deviceOf(step.kind);
  }
  return { operator_counts: total, human_seconds: secondsFor(total) };
}

export const round = (x, d = 3) => Math.round(x * 10 ** d) / 10 ** d;
