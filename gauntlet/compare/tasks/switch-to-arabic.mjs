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
    odoo: { reads: [{ read: 'POST /web/dataset/call_kw/res.users/read', parts: ['result.lang'] }], writes: ['POST /web/dataset/call_kw/res.users/web_save', 'POST /web/dataset/call_button/res.users/preference_save'] },
    ours: { reads: [{ read: 'GET /api/auth/session', parts: ['user.language'] }], writes: ['PUT /api/identity/me/preferences'] },
  },
  // Round 14 (critic p01 r13, fix c): the writes set-up sends for each product whose driver is built
  // (set-up makes the tester when missing and puts the tester back to English); any other set-up write is refused for our product (lib/runner.mjs setUpFixturesFor).
  fixtures: { ours: ['POST /api/identity/users', 'PUT /api/identity/me/preferences'] },
  moments: [],
  start: 'Signed in as an ordinary user working in English, on their working screen (the contacts list, with records).',
  goal: 'Switch your own interface language to Arabic.',
  done: 'The interface, including the working screen, is shown in Arabic, laid out right to left, with the records still on screen.',
  notes: 'Clean-up switches the user back to English through the back end (not measured).',
};
