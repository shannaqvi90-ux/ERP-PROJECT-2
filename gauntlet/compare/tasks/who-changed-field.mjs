export default {
  id: 'who-changed-field',
  title: 'Who changed this field, and when',
  named: false,
  piece: 'p07',
  actor: 'admin',
  start: 'Signed in, on the screen the product shows right after sign-in.',
  goal: 'Find out who last changed the e-mail address of the contact {contact.name}, from what to what, and when.',
  done: 'The change is on screen: the old and new e-mail, the person who made it ({changedBy}) and the time.',
  input: { newEmail: 'shamma.romaithi.new@mail.example', changedBy: 'Noor Editor' },
  data: ['contact.name', 'contact.email'],
  notes: 'Set-up makes the change as {changedBy} (an ordinary user) through the back end, so it is the latest change; clean-up restores the address.',
};
