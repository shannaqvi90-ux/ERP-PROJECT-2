export default {
  id: 'switch-company',
  title: 'Switch company',
  named: false,
  piece: 'p02',
  actor: 'admin (works in two companies)',
  start: 'Signed in, working in "{from}", on the screen the product shows right after sign-in.',
  goal: 'Switch to working in "{to}".',
  done: 'The product shows the user working in "{to}".',
  input: { from: 'Demo Trading LLC', to: 'Demo Manufacturing FZE' },
  notes: 'Both companies exist in each product before the task (fixtures).',
};
