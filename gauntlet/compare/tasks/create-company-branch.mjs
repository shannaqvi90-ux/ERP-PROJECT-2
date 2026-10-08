export default {
  id: 'create-company-branch',
  title: 'Create a company with a branch',
  named: false,
  piece: 'p02',
  actor: 'admin',
  startAt: 'home',
  moments: ['branch filled in'],
  start: 'Signed in as an administrator, on the screen the product shows right after sign-in.',
  goal: 'Create the company "{company}" with one branch, "{branch}".',
  done: 'Both are saved; the back end holds the company and the branch under it.',
  input: { company: 'Falcon Logistics LLC', branch: 'Falcon Logistics LLC - Jebel Ali Branch' },
  notes: 'Set-up removes any earlier copy; clean-up deletes both.',
};
