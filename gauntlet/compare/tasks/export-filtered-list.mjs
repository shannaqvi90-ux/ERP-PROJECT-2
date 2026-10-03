export default {
  id: 'export-filtered-list',
  title: 'Export a filtered list',
  named: false,
  piece: 'p14',
  actor: 'admin',
  start: 'Signed in as an administrator, on the screen the product shows right after sign-in.',
  goal: 'Export every contact tagged "{tag}" to a spreadsheet with at least their name, e-mail and phone.',
  done: 'The product hands over a spreadsheet file; it holds one row per contact tagged "{tag}" (every one of them, not just the first page) with name, e-mail and phone columns.',
  input: { tag: 'Government' },
  notes: 'Thousands of contacts carry the tag, so the export must cover more than the page on screen. The verification reads the file.',
};
