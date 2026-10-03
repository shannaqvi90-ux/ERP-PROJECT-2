export default {
  id: 'reach-screen-keyboard',
  title: 'Reach a named screen by keyboard only',
  named: false,
  piece: 'p04',
  actor: 'admin',
  startAt: 'home',
  moments: [],
  start: 'Signed in, on the screen the product shows right after sign-in.',
  goal: 'Without touching the mouse, open the list of the workspace\'s users (the screen named "Users").',
  done: 'The users list is on screen with its rows, and no step used the mouse.',
  notes: 'Proposed by the p04 critic, round 1 (its task and drivers, added to the harness in p04 round 2). Every step must be a key or a key chord; the verification fails a run with a click in it.',
};
