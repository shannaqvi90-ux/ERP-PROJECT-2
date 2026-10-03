export default {
  id: 'attach-file',
  title: 'Attach a file to a record',
  named: false,
  piece: 'p11',
  actor: 'admin',
  start: 'Signed in, with the contact {contact.name} open on screen.',
  goal: 'Attach the file {file} to the contact.',
  done: 'The file is listed on the contact; the back end holds it attached to that contact.',
  input: { file: 'trade-licence-TL-2026-73519.pdf' },
  files: ['data/fixtures/trade-licence-TL-2026-73519.pdf'],
  data: ['contact.name'],
  notes: 'The file is committed (data/fixtures). Clean-up deletes the attachment.',
};
