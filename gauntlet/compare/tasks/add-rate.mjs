export default {
  id: 'add-rate',
  title: 'Add an exchange rate',
  named: false,
  piece: 'p08',
  actor: 'admin',
  start: 'Signed in as an administrator, on the screen the product shows right after sign-in.',
  goal: 'Record today\'s rate for the euro: 1 EUR = {aedPerUnit} AED.',
  done: 'The rate is saved; the back end holds a EUR rate dated today equal to {aedPerUnit} AED per euro.',
  input: { currency: 'EUR', aedPerUnit: '4.2875' },
  notes: 'Set-up removes any EUR rate dated today; clean-up deletes the new one. The rig and the dataset hold 100,000 rates.',
};
