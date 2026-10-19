// Proposed by the p04 critic (drivers in gauntlet/evidence/p04-shell/r9); registered in the harness by p01 round 11.
export default {
  id: 'new-user-keyboard',
  title: 'Start a new record from the home screen by keyboard only (new user)',
  named: false,
  piece: 'p04',
  actor: 'admin',
  startAt: 'home',
  keyboardOnly: true,
  // Round 14 (critic p01 r13, fix c): the writes set-up sends for each product whose driver is built
  // (set-up writes nothing); any other set-up write is refused for our product (lib/runner.mjs setUpFixturesFor).
  fixtures: { ours: [] },
  moments: [],
  start: 'Signed in, on the screen the product shows right after sign-in.',
  goal: 'Without touching the mouse, open an empty form for a new user, ready to type the first field.',
  done: 'An empty new-user form is on screen and no step used the mouse.',
  notes: 'Proposed by the p04 critic, round 7: "every action reachable without a mouse" and "open any action by typing". Every step must be a key or key chord.',
};