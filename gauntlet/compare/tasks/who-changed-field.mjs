export default {
  id: 'who-changed-field',
  title: 'Who changed this field, and when',
  named: false,
  piece: 'p07',
  actor: 'admin',
  startAt: 'home',
  moments: [],
  start: 'Signed in, on the screen the product shows right after sign-in.',
  goal: 'Find out who last changed the e-mail address of the contact {contact.name}, from what to what, and when.',
  done: 'The change is on screen: the old and new e-mail, the person who made it ({changedBy}) and the time.',
  input: { newEmail: 'shamma.romaithi.new@mail.example', changedBy: 'Noor Editor' },
  // Round 14 (critic p01 r13, plants N2 and N3): the record the person finds. The measured part types a
  // piece of it (its letters in order), and no screen shows it before that (lib/runner.mjs findsProblem).
  finds: 'contact.name',
  data: ['contact.name', 'contact.email'],
  notes: 'Set-up makes the change as {changedBy} (an ordinary user) through the back end, so it is the latest change; clean-up restores the address.',
};
