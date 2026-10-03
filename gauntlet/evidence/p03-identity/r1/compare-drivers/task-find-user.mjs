// Added by the p03 critic (round 1): the piece's comparison "find one user among 100,000".
export default {
  id: 'find-user',
  title: 'Find one user among 100,000',
  named: true,
  piece: 'p03-identity',
  actor: 'admin',
  start: 'Signed in as an administrator, on the screen the product shows right after sign-in.',
  goal: 'Open the user whose name the administrator knows ({user.name}) out of 100,000 users and read their sign-in e-mail.',
  done: 'The user\'s own record is open on screen and shows the sign-in e-mail {user.login}.',
  input: { name: 'Rania Rahul Al Kaabi', login: 'rania.al.kaabi.050092@staff.example' },
  notes: 'Both products hold the 100,000 users of gauntlet/compare/data/out/users.csv (ours via ERP_SEED_USERS_CSV); the name occurs once.',
};
