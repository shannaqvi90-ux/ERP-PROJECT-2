export default {
  id: 'see-converted',
  title: 'See an amount converted to the base currency',
  named: false,
  piece: 'p08',
  actor: 'admin',
  startAt: 'home',
  // The writes set-up sends for each product whose driver is built: today's euro rate is made the
  // task's rate (the euro put in use first when it is not); any other set-up write is refused for our
  // product (lib/runner.mjs setUpFixturesFor). Clean-up removes the rate (and, in the reference, the
  // draft invoice its path leaves).
  fixtures: { ours: ['PUT /api/currency/currencies/*', 'DELETE /api/currency/rates/*', 'POST /api/currency/rates'] },
  moments: ['converted amount'],
  start: 'Signed in as an administrator, on the screen the product shows right after sign-in. Today\'s rate for the euro is {aedPerUnit} AED.',
  goal: 'Find out what {amount} EUR is in AED at today\'s rate.',
  done: 'The screen shows {amount} EUR as {aed} AED.',
  input: { currency: 'EUR', amount: '1250', aedPerUnit: '4.2875', aed: '5,359.38' },
  notes: 'An amount in a foreign currency seen in the company\'s base currency (1250 x 4.2875 = 5,359.375, rounded once to the fils: 5,359.38). ' +
    'Set-up makes today\'s euro rate 4.2875 AED in both products; neither product saves anything the task needs.',
};
