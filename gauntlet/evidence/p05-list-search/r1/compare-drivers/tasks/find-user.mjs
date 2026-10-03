// Critic p05 round 1: the platform's 100,000-record list today is users (p16 contacts does not exist yet).
export default {
  id: 'find-user',
  title: 'Find one user among 100,000',
  named: false,
  piece: 'p05',
  actor: 'admin',
  start: 'Signed in, on the screen the product shows right after sign-in.',
  goal: 'Open the user whose name the administrator knows ({name}) out of 100,000 users and read their sign-in e-mail.',
  done: 'The user\'s own record is open on screen and shows the e-mail {login}.',
  input: { name: 'Yousef Samir Wang', login: 'yousef.wang.060019@staff.example', ref: 'U060019' },
  notes: 'Both products hold the shared dataset\'s 100,000 users (gauntlet/compare/data/out/users.csv); the name occurs exactly once.',
};
