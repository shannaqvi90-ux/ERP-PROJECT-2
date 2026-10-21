export default {
  id: 'switch-company',
  title: 'Switch company',
  named: false,
  piece: 'p02',
  actor: 'admin (works in two companies)',
  startAt: 'home',
  // Round 14 (critic p01 r13, fix c): the writes set-up sends for each product whose driver is built
  // (set-up makes the two companies when missing and puts the actor in the first); any other set-up write is refused for our product (lib/runner.mjs setUpFixturesFor).
  fixtures: { ours: ['POST /api/tenancy/companies', 'POST /api/tenancy/branches', 'PUT /api/tenancy/companies/*', 'PUT /api/tenancy/workplace'] },
  moments: [],
  start: 'Signed in, working in "{from}", on the screen the product shows right after sign-in.',
  goal: 'Switch to working in "{to}".',
  done: 'The product shows the user working in "{to}".',
  input: { from: 'Demo Trading LLC', to: 'Demo Manufacturing FZE' },
  notes: 'Both companies exist in each product before the task (fixtures).',
};
