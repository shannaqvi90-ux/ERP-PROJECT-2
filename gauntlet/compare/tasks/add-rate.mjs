export default {
  id: 'add-rate',
  title: 'Add an exchange rate',
  named: false,
  piece: 'p08',
  actor: 'admin',
  startAt: 'home',
  saves: true,
  // Where the saved end state lives in each product (round 10): the back-end reads verify() takes it
  // from and the parts of their answers that hold it (a change anywhere else proves nothing), and the
  // writes that save it (the measured part must send one of them).
  endState: {
    odoo: { reads: [{ read: 'POST /web/dataset/call_kw/res.currency.rate/search_read', parts: ['result.inverse_company_rate', 'result.company_rate'] }], writes: ['POST /web/dataset/call_kw/res.currency/web_save', 'POST /web/dataset/call_kw/res.currency.rate/web_save'] },
    ours: { reads: [{ read: 'GET /api/currency/rates', parts: ['items.exchangeRate'] }], writes: ['POST /api/currency/rates'] },
  },
  // The writes set-up sends for each product whose driver is built (set-up removes any euro rate
  // dated today, putting the euro in use first when it is not); any other set-up write is refused for
  // our product (lib/runner.mjs setUpFixturesFor).
  fixtures: { ours: ['PUT /api/currency/currencies/*', 'DELETE /api/currency/rates/*'] },
  enters: ['aedPerUnit'],
  moments: ['rate entered'],
  start: 'Signed in as an administrator, on the screen the product shows right after sign-in.',
  goal: 'Record today\'s rate for the euro: 1 EUR = {aedPerUnit} AED.',
  done: 'The rate is saved; the back end holds a EUR rate dated today equal to {aedPerUnit} AED per euro.',
  input: { currency: 'EUR', aedPerUnit: '4.2875' },
  notes: 'Set-up removes any EUR rate dated today; clean-up deletes the new one. The rig and the dataset hold 100,000 rates.',
};
