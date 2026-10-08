export default {
  id: 'create-restricted-user',
  title: 'Create a user with a restricted role',
  named: true,
  actor: 'admin',
  startAt: 'home',
  moments: ['user filled in'],
  start: 'Signed in as an administrator, on the screen the product shows right after sign-in.',
  goal: 'Create the user "Hessa Clerk", sign-in hessa.clerk@demo-trading.example, who may view and create contacts and do nothing else: no purchasing, accounting, settings or user administration.',
  done: 'The user is saved; the back end confirms the user exists, can create contacts and holds no administration, purchasing or accounting rights.',
  input: { name: 'Hessa Clerk', login: 'hessa.clerk@demo-trading.example' },
  notes: 'Fixed values so keystroke counts are comparable run to run; set-up removes any earlier copy of the user.',
};
