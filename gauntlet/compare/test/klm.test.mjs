import test from 'node:test';
import assert from 'node:assert/strict';
import { OPERATORS, keystrokesForChord, keystrokesForText, modelSteps, operatorsForStep } from '../lib/klm.mjs';

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
    { kind: 'type', keystrokes: 10 }, { kind: 'key', keystrokes: 1, chain: true },
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
