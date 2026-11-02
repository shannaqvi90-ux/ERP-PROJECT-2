// Product-neutral task definition. Drivers for each product live in drivers/<product>/.
export default {
  id: 'who-renamed-user',
  title: 'Who changed this user\'s name, and when',
  named: false,
  piece: 'p07',
  actor: 'admin',
  startAt: 'home',
  moments: [],
  start: 'Signed in as an administrator, on the screen the product shows right after sign-in.',
  goal: 'Find out who last changed the name of the user now called {newName} (one of 100,000 users), from what to what, and when.',
  done: 'The change is on screen: the old name {user.name}, the new name {newName}, the person who made it ({changedBy}) and the time.',
  input: { newName: 'Majid A. Pillai', changedBy: 'Noor Editor' },
  // The record the person finds, by the name it has now (lib/runner.mjs findsProblem): the measured part
  // types a piece of it, and no screen shows it before that. The old name is what they find out.
  finds: 'newName',
  data: ['user.name', 'user.login'],
  // The writes set-up sends for each product whose driver is built: the editor's role and account when
  // missing, and the rename the person looks for, made by that editor (its body carries the new name, a
  // task input, never the old one); a health check also creates the one dataset user.
  fixtures: { ours: ['POST /api/identity/roles', 'POST /api/identity/users', 'PUT /api/identity/users/*'] },
  healthFixtures: { ours: [] },
  notes: 'p07 (audit trail) on the list that exists in both products today: the shared dataset\'s 100,000 users, ' +
    'whose names both products track (the reference keeps the name among the tracked fields of its users). ' +
    'Set-up renames the user as {changedBy}, an ordinary user who may change users, so it is the latest change; clean-up puts the name back. ' +
    'who-changed-field (contacts) waits for p16.',
};
