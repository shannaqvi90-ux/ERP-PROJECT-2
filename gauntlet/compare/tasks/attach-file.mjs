export default {
  id: 'attach-file',
  title: 'Attach a file to a record',
  named: false,
  piece: 'p11',
  actor: 'admin',
  startAt: 'record',
  saves: true,
  // Where the saved end state lives in each product (round 10): the back-end reads verify() takes it
  // from and the parts of their answers that hold it (a change anywhere else proves nothing), and the
  // writes that save it (the measured part must send one of them).
  endState: {
    odoo: { reads: [{ read: 'POST /web/dataset/call_kw/ir.attachment/search_read', parts: ['result.name', 'result.file_size'] }], writes: ['POST /mail/attachment/upload'] },
  },
  enters: ['file'],
  moments: [],
  start: 'Signed in, with the contact {contact.name} open on screen.',
  goal: 'Attach the file {file} to the contact.',
  done: 'The file is listed on the contact; the back end holds it attached to that contact.',
  input: { file: 'trade-licence-TL-2026-73519.pdf' },
  files: ['data/fixtures/trade-licence-TL-2026-73519.pdf'],
  data: ['contact.name'],
  notes: 'The file is committed (data/fixtures). Clean-up deletes the attachment.',
};
