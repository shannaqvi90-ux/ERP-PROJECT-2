// Product-neutral task definition. Drivers for each product live in drivers/<product>/.
export default {
  id: 'find-user',
  title: 'Find one user among 100,000',
  named: false,
  piece: 'p03',
  actor: 'admin',
  start: 'Signed in as an administrator, on the screen the product shows right after sign-in.',
  goal: 'Open the user whose name the administrator knows ({user.name}) out of 100,000 users and read their sign-in e-mail.',
  done: 'The user\'s own record is open on screen and shows the sign-in {user.login}.',
  data: ['user.name', 'user.login'],
  notes: 'The bar\'s "find one record among 100,000" on the list that exists in both products today: the shared dataset\'s 100,000 users ' +
    '(gauntlet/compare/data/out/users.csv), loaded into the reference by its rig and into our product with ERP_SEED_USERS_CSV. ' +
    'The name occurs exactly once in the dataset. find-record (contacts) stays the named task.',
};
