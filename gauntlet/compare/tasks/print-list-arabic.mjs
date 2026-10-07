// Product-neutral task definition. Drivers for each product live in drivers/<product>/.
export default {
  id: 'print-list-arabic',
  title: 'Print a list report in Arabic',
  named: false,
  piece: 'p06',
  actor: 'an administrator who works in Arabic',
  startAt: 'list',
  moments: ['list filtered'],
  start: 'Signed in, working in Arabic (the interface is Arabic and right to left), on the list of purchase orders.',
  goal: 'Narrow the list to the orders of the vendor {contact.parent_name} and print that list as a PDF in Arabic.',
  done: 'The product hands over one PDF that holds every order the narrowed list shows, and only those, in Arabic and laid out right to left.',
  data: ['contact.parent_name', 'user.name'],
  notes: 'The comparison named in gauntlet/pieces/p06-form-report.md ("print a list report in Arabic"). Odoo Community has no list report ' +
    '(a printed table of a list\'s rows); its nearest feature selects the listed records and prints their own document, all of them in ' +
    'one PDF (Print > Purchase Order). The printed order follows the vendor\'s language, so set-up gives the vendor and its contacts Arabic ' +
    'and clean-up restores them. The Odoo user who works in Arabic is a rig fixture (arabic.reporter, purchase administrator), created by ' +
    'set-up when missing. Our product has no purchase orders yet: its driver prints the stand-in list it has, the users named like the ' +
    'dataset user {user.name} without the first name (the product\'s search matches each word in the name or e-mail: 53 users of the shared dataset, against the vendor\'s 27 orders in the reference).',
};
