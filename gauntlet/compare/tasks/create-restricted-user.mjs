export default {
  id: 'create-restricted-user',
  title: 'Create a user with a restricted role',
  named: true,
  actor: 'admin',
  startAt: 'home',
  saves: true,
  // Where the saved end state lives in each product (round 10): the back-end reads verify() takes it
  // from and the parts of their answers that hold it (a change anywhere else proves nothing), and the
  // writes that save it (the measured part must send one of them).
  endState: {
    odoo: { reads: [{ read: 'POST /web/dataset/call_kw/res.users/search_read', parts: ['result.name'] }], writes: ['POST /web/dataset/call_kw/res.users/web_save'] },
    ours: { reads: [{ read: 'GET /api/identity/users', parts: ['items.displayName', 'items.email'] }], writes: ['POST /api/identity/users'] },
  },
  enters: ['name', 'login'],
  moments: ['user filled in'],
  start: 'Signed in as an administrator, on the screen the product shows right after sign-in.',
  goal: 'Create the user "Hessa Clerk", sign-in hessa.clerk@demo-trading.example, who may view and create contacts and do nothing else: no purchasing, accounting, settings or user administration.',
  done: 'The user is saved; the back end confirms the user exists, can create contacts and holds no administration, purchasing or accounting rights.',
  input: { name: 'Hessa Clerk', login: 'hessa.clerk@demo-trading.example' },
  notes: 'Fixed values so keystroke counts are comparable run to run; set-up removes any earlier copy of the user.',
};
