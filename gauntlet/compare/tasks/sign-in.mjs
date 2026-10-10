export default {
  id: 'sign-in',
  title: 'Sign in',
  named: false,
  piece: 'p00',
  actor: 'an ordinary internal user with an e-mail sign-in',
  startAt: 'sign-in',
  saves: true,
  // Where the saved end state lives in each product (round 10): the back-end reads verify() takes it
  // from and the parts of their answers that hold it (a change anywhere else proves nothing), and the
  // writes that save it (the measured part must send one of them).
  endState: {
    odoo: { reads: [{ read: 'POST /web/session/get_session_info', parts: ['result.uid'] }], writes: ['POST /web/login'] },
    ours: { reads: [{ read: 'GET /api/auth/session', parts: ['authenticated', 'user.id'] }], writes: ['POST /api/auth/sign-in'] },
  },
  moments: [],
  start: 'Signed out, on the product\'s sign-in screen (the address the user keeps bookmarked).',
  goal: 'Sign in as {user} with the password {password}.',
  done: 'The screen the product shows right after sign-in is ready to work in, and the back end holds a session for that user.',
  input: { user: 'signin.tester@demo-trading.example', password: 'Sign-In-Pass-2026', name: 'Sara Signin' },
  // Both products sign in with a passkey too: every browser of the task has the person's passkey
  // device, and confirming on it is a counted step (lib/device.mjs, op.confirmOnDevice).
  device: 'passkey',
  notes: 'Set-up creates the same user, with the same e-mail sign-in and password, in each product, so the keys typed are identical.',
};
