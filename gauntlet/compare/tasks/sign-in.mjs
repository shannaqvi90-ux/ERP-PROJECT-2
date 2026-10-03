export default {
  id: 'sign-in',
  title: 'Sign in',
  named: false,
  piece: 'p00',
  actor: 'an ordinary internal user with an e-mail sign-in',
  start: 'Signed out, on the product\'s sign-in screen (the address the user keeps bookmarked).',
  goal: 'Sign in as {user} with the password {password}.',
  done: 'The screen the product shows right after sign-in is ready to work in, and the back end holds a session for that user.',
  input: { user: 'signin.tester@demo-trading.example', password: 'Sign-In-Pass-2026', name: 'Sara Signin' },
  notes: 'Set-up creates the same user, with the same e-mail sign-in and password, in each product, so the keys typed are identical.',
};
