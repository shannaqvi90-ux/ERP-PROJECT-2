export default {
  id: 'switch-to-arabic',
  title: 'Switch to Arabic',
  named: true,
  actor: 'language tester (an ordinary internal user who works in Contacts)',
  startAt: 'list',
  saves: true,
  // Where the saved end state lives in each product (round 10): the back-end reads verify() takes it
  // from and the parts of their answers that hold it (a change anywhere else proves nothing), and the
  // writes that save it (the measured part must send one of them).
  endState: {
    odoo: { reads: [{ read: 'POST /web/dataset/call_kw/res.users/read', parts: ['result.lang'] }] },
    ours: { reads: [{ read: 'GET /api/auth/session', parts: ['user.language'] }], writes: ['PUT /api/identity/me/preferences'] },
  },
  moments: [],
  start: 'Signed in as an ordinary user working in English, on their working screen (the contacts list, with records).',
  goal: 'Switch your own interface language to Arabic.',
  done: 'The interface, including the working screen, is shown in Arabic, laid out right to left, with the records still on screen.',
  notes: 'Clean-up switches the user back to English through the back end (not measured).',
};
