export default {
  id: 'arabic-report',
  title: 'Print a document in Arabic',
  named: false,
  piece: 'p06',
  actor: 'admin (working in English)',
  startAt: 'record',
  // Round 14 (critic p01 r13, fix c): the writes set-up sends for each product whose driver is built
  // (set-up puts the actor back to English); any other set-up write is refused for our product (lib/runner.mjs setUpFixturesFor).
  fixtures: { ours: ['PUT /api/identity/me/preferences'] },
  moments: [],
  start: 'Signed in, working in English, with an order to the vendor {contact.parent_name} open on screen. The vendor\'s language is Arabic.',
  goal: 'Print the order as a PDF document in the vendor\'s language, Arabic.',
  done: 'The product hands over a PDF of the order whose content is in Arabic and laid out right to left.',
  data: ['contact.parent_name'],
  notes: 'Odoo has no generic printable record in its core; its nearest printed document is the purchase order, which follows the vendor\'s language. Set-up creates the order and sets the vendor\'s language; clean-up restores both.',
};
