// Product-neutral task definition. Drivers for each product live in drivers/<product>/.
export default {
  id: 'find-record',
  title: 'Find one record among 100,000',
  named: true,
  actor: 'admin',
  startAt: 'home',
  moments: ['result list'],
  start: 'Signed in, on the screen the product shows right after sign-in.',
  goal: 'Open the contact whose name the user knows ({contact.name}) out of 100,000 contacts and read its mobile number.',
  done: 'The contact\'s own record is open on screen and shows the mobile number {contact.mobile}.',
  data: ['contact.name', 'contact.mobile', 'contact.email'],
  notes: 'The name occurs exactly once in the shared dataset (gauntlet/compare/data). Both products hold the same 100,000 contacts.',
};
