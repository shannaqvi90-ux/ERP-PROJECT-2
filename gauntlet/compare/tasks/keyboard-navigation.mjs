export default {
  id: 'keyboard-navigation',
  title: 'Navigate by keyboard',
  named: false,
  piece: 'p04',
  actor: 'admin',
  startAt: 'home',
  moments: [],
  start: 'Signed in, on the screen the product shows right after sign-in.',
  goal: 'Without touching the mouse, open the contacts list and open the third contact in it.',
  done: 'The third contact of the list (in the list\'s own order) is open on screen, and no step used the mouse.',
  notes: 'Every step must be a key or a key chord; the verification fails a run with a click in it.',
};
