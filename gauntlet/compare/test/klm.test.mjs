import test from 'node:test';
import assert from 'node:assert/strict';
import { OPERATORS, continues, keystrokesForChord, keystrokesForText, modelSteps, operatorsForStep } from '../lib/klm.mjs';

test('operator times are the published Card, Moran and Newell averages', () => {
  assert.deepEqual(OPERATORS, { K: 0.28, P: 1.1, B: 0.1, H: 0.4, M: 1.35 });
});

test('typing counts one key per character plus Shift for capitals and symbols', () => {
  assert.equal(keystrokesForText('abc'), 3);
  assert.equal(keystrokesForText('Abc'), 4);
  assert.equal(keystrokesForText('LR-7731'), 9);
  assert.equal(keystrokesForText('a@b.c'), 6);
  assert.equal(keystrokesForText(''), 0);
});

test('a chord counts each key in it', () => {
  assert.equal(keystrokesForChord('Enter'), 1);
  assert.equal(keystrokesForChord('Control+K'), 2);
  assert.equal(keystrokesForChord('Control+Shift+K'), 3);
  assert.equal(keystrokesForChord('+'), 1);
  assert.equal(keystrokesForChord('Control++'), 2);
});

test('a click is M + P + BB, with homing when the hand comes from the keyboard', () => {
  assert.deepEqual(operatorsForStep({ kind: 'click', keystrokes: 0 }, 'mouse').ops, { K: 0, P: 1, B: 2, H: 0, M: 1 });
  assert.deepEqual(operatorsForStep({ kind: 'click', keystrokes: 0 }, 'keyboard').ops, { K: 0, P: 1, B: 2, H: 1, M: 1 });
});

test('a chained step has no mental preparation', () => {
  assert.equal(operatorsForStep({ kind: 'key', keystrokes: 1, chain: true }, 'keyboard').ops.M, 0);
  assert.equal(operatorsForStep({ kind: 'key', keystrokes: 1 }, 'keyboard').ops.M, 1);
});

test('the operator starts with the hand on the mouse', () => {
  assert.equal(operatorsForStep({ kind: 'type', keystrokes: 3 }, null).ops.H, 1);
  assert.equal(operatorsForStep({ kind: 'click', keystrokes: 0 }, null).ops.H, 0);
});

test('file picks are modelled as a double click', () => {
  assert.deepEqual(operatorsForStep({ kind: 'file-pick', keystrokes: 0, chain: true }, 'mouse').ops, { K: 0, P: 1, B: 4, H: 0, M: 0 });
});

test('totals for a short path add up', () => {
  // click, click, type 10 keys, Enter (chained), click
  const steps = [
    { kind: 'click', keystrokes: 0 }, { kind: 'click', keystrokes: 0 },
    { kind: 'type', keystrokes: 10 }, { kind: 'key', chord: 'Enter', keystrokes: 1 },
    { kind: 'click', keystrokes: 0 },
  ];
  const m = modelSteps(steps);
  assert.deepEqual(m.operator_counts, { K: 11, P: 3, B: 6, H: 2, M: 4 });
  assert.equal(m.human_seconds, Number((11 * 0.28 + 3 * 1.1 + 6 * 0.1 + 2 * 0.4 + 4 * 1.35).toFixed(3)));
});

test('unknown step kinds are rejected', () => {
  assert.throws(() => operatorsForStep({ kind: 'wave', keystrokes: 0 }, null), /unknown step kind/);
});

test('a scroll is a mouse step modelled like a click', () => {
  assert.deepEqual(operatorsForStep({ kind: 'scroll', keystrokes: 0 }, 'mouse').ops, { K: 0, P: 1, B: 2, H: 0, M: 1 });
  assert.deepEqual(operatorsForStep({ kind: 'scroll', keystrokes: 0 }, 'keyboard').ops, { K: 0, P: 1, B: 2, H: 1, M: 1 });
});

test('continuation is derived from the steps: typing after a click on its field, after a key; Enter after typing or an arrow', () => {
  const click = { kind: 'click', keystrokes: 0 };
  const key = chord => ({ kind: 'key', chord, keystrokes: 1 });
  const type = (same_field = false) => ({ kind: 'type', keystrokes: 3, same_field });
  assert.equal(continues(null, type()), false, 'the first step always starts with M');
  assert.equal(continues(click, type(true)), true, 'typing into the field the click put the caret in');
  assert.equal(continues(click, type(false)), false, 'typing after a click on something else (a button that opened a form)');
  assert.equal(continues(key('Tab'), type()), true, 'Tab to the next field, then type');
  assert.equal(continues(key('Control+k'), type()), true, 'a palette key, then the command');
  assert.equal(continues(type(), key('Enter')), true, 'Enter sends what was just typed');
  assert.equal(continues(key('ArrowDown'), key('Enter')), true, 'Enter opens what the arrow selected');
  assert.equal(continues(type(), key('Tab')), false, 'Tab after typing is a decision of its own');
  assert.equal(continues(key('ArrowDown'), key('ArrowDown')), true, 'a run of one navigation key');
  assert.equal(continues(key('Enter'), key('Enter')), false, 'a second Enter is a second decision');
  assert.equal(continues(click, key('Control+a')), true, 'select the content of the field just clicked');
  assert.equal(continues(type(), key('Control+a')), false);
  assert.equal(continues(click, { kind: 'file-pick', keystrokes: 0 }), true);
  assert.equal(continues(click, click), false);
  assert.equal(continues(key('Alt+s'), click), false);
});

test('plant K1 (round 3): a chain flag written on a step changes nothing; only the sequence counts', () => {
  const honest = [{ kind: 'click', keystrokes: 0 }, { kind: 'type', keystrokes: 1, same_field: true }, { kind: 'click', keystrokes: 0 }];
  const claimed = honest.map(s => ({ ...s, chain: true }));
  assert.deepEqual(modelSteps(claimed), modelSteps(honest));
  assert.equal(modelSteps(honest).operator_counts.M, 2);
});

test('round 5: no step continues one that began on another screen (a key that opened a new screen, then typing)', async () => {
  const { continues, modelSteps } = await import('../lib/klm.mjs');
  const enter = { kind: 'key', chord: 'Enter', keystrokes: 1, screen: '/odoo/discuss' };
  const typedOnNewScreen = { kind: 'type', keystrokes: 5, text: 'Majid', screen: '/odoo/users' };
  const typedOnSameScreen = { kind: 'type', keystrokes: 5, text: 'Majid', screen: '/odoo/discuss' };
  assert.equal(continues(enter, typedOnNewScreen), false, 'typing on the screen the Enter opened starts with M');
  assert.equal(continues(enter, typedOnSameScreen), true, 'typing right after a key on the same screen continues it');
  // Steps without a screen (API requests, results recorded before instrument 5) keep the old rules.
  assert.equal(continues({ kind: 'key', chord: 'Control+k', keystrokes: 2 }, { kind: 'type', keystrokes: 3, text: 'abc' }), true);
  const palette = [
    { kind: 'key', chord: 'Control+k', keystrokes: 2, screen: '/odoo/discuss' },
    { kind: 'type', keystrokes: 6, text: '/users', screen: '/odoo/discuss' },
    { kind: 'key', chord: 'Enter', keystrokes: 1, screen: '/odoo/discuss' },
    { kind: 'type', keystrokes: 5, text: 'Majid', screen: '/odoo/users' },
  ];
  assert.equal(modelSteps(palette).operator_counts.M, 2, 'the palette path carries an M for the new screen');
});
