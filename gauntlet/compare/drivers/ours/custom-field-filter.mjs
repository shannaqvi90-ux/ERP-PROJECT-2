import { OursApi, oursAs } from '../../lib/ours-api.mjs';

// Written by the p09 builder (round 1). Shortest expert path through our screens: Custom fields
// (navigation) > New custom field > the label (the cursor is in it; type is Text, searched by the
// list's search box, English label only) > Enter saves > Contacts (navigation; the search box has the
// focus) > the shortest word prefixes of the contact's name that rank it first > Enter opens its form > a click in the new field > the value > Ctrl+S >
// Escape (closes the form) > "/" (the list's search box, its text chosen) > the value: the list holds exactly that contact. A
// custom field of a text kind is searched by the same box as the contact's own fields; the column
// chooser and a column's Filter menu filter by it too (longer: five more actions), see
// docs/decisions/p09-custom-fields-compare-paths.md.
// Set-up switches off any earlier copy of the field (a field is switched off, never deleted); clean-up
// switches off the one this run added.

const link = (page, name) => page.getByRole('navigation').getByRole('link', { name, exact: true }).first();

/** Word prefixes (up to `longest` letters a word, the name's order), shortest first. A driver stands alone, so this is its own copy. */
function prefixCandidates(name, longest = 4) {
  const words = name.toLocaleLowerCase().split(/\s+/).filter(Boolean);
  let combos = [[]];
  for (const w of words) {
    const next = [];
    for (const c of combos) for (let l = 0; l <= Math.min(longest, w.length); l++) next.push([...c, w.slice(0, l)]);
    combos = next;
  }
  const typed = [...new Set(combos.map(c => c.filter(Boolean).join(' ')).filter(Boolean))];
  return typed.sort((a, b) => a.length - b.length || a.localeCompare(b));
}

/**
 * The fewest letters an expert of this product types to put the contact first (the shortest word
 * prefixes of the name, each at least three letters or a whole word: a shorter part cannot use the
 * search's trigram index), found through the contacts list's own search before the measured run.
 */
async function shortestPrefixes(api, name, id, maxProbes = 400) {
  const words = name.toLocaleLowerCase().split(/\s+/).filter(Boolean);
  const indexed = typed => typed.split(' ').every(part => part.length >= 3 || words.includes(part));
  for (const typed of prefixCandidates(name).filter(indexed).slice(0, maxProbes)) {
    const page = await api.get(`/api/contacts/contacts?take=1&search=${encodeURIComponent(typed)}`);
    if (page.items?.[0]?.id === id) return typed;
  }
  return name.split(/\s+/).map(w => w.slice(0, 3)).join(' ').toLocaleLowerCase();
}

async function fieldsLabelled(api, label) {
  const filter = encodeURIComponent(`labelEn eq '${label}' and isActive eq true`);
  return (await api.get(`/api/customfields/fields?filter=${filter}&take=50`)).items;
}

async function switchOff(ctx) {
  const api = await oursAs(ctx.product, 'admin');
  for (const row of await fieldsLabelled(api, ctx.task.input.label)) {
    const field = await api.get(`/api/customfields/fields/${row.id}`);
    await api.put(`/api/customfields/fields/${field.id}`, {
      labelEn: field.labelEn, labelAr: field.labelAr, required: field.required, default: field.default, decimals: field.type === 'number' ? field.decimals : null,
      options: field.type === 'select' ? field.options : null, lookupTarget: field.lookupTarget, searchable: field.searchable, sortOrder: field.sortOrder, isActive: false, version: field.version,
    });
  }
}

export default {
  built: true,
  path: 'Custom fields (navigation) > New custom field > type the label > Enter (saved) > Contacts (navigation) > type the shortest word prefixes of the contact\'s name > Enter opens it > click the new field > type the value > Ctrl+S > Alt+Shift+F: the form closes and the list is filtered by the field\'s value (a condition on that field only): exactly that contact.',
  async setup(ctx) {
    const api = await oursAs(ctx.product, 'admin');
    const { name, name_ar: nameAr, mobile, email } = ctx.needles.contact;
    const found = await api.get(`/api/contacts/contacts?search=${encodeURIComponent(name)}`);
    let contact = found.items.find(c => c.nameEn === name);
    if (!contact) {
      // A driver health check (./erp verify, a clean stack without the dataset) creates the one contact.
      if (ctx.health) contact = await api.post('/api/contacts/contacts', { kind: 'person', nameEn: name, nameAr: nameAr ?? '', mobile, email, country: 'AE', defaultCurrency: 'AED', tags: [] });
      else throw new Error(`our product does not hold the dataset contact ${name}; start it with ERP_SEED_CONTACTS_CSV=gauntlet/compare/data/out/contacts.csv on a fresh database`);
    }
    ctx.state.contactId = contact.id;
    ctx.state.shortest = await shortestPrefixes(api, name, contact.id);
    await switchOff(ctx);
  },
  async signIn(ctx) {
    const page = ctx.page;
    const { login, password } = ctx.product.users.admin;
    await page.goto(ctx.product.baseUrl + '/');
    await page.locator('input[name="email"]:focus').waitFor();
    await page.keyboard.type(login);
    await page.keyboard.press('Tab');
    await page.keyboard.type(password);
    await page.keyboard.press('Enter');
    await link(page, 'Custom fields').waitFor();
    await page.locator('main h1').waitFor();
  },
  async run(op, ctx) {
    const { label, value } = ctx.task.input;
    const { name } = ctx.needles.contact;
    // Define the field.
    await op.click(link(op.page, 'Custom fields'), { label: 'Custom fields (navigation)' });
    await op.click(op.page.getByRole('button', { name: 'New custom field', exact: true }).first(), { label: 'New custom field' });
    await op.waitFor('.list-record input[name="labelEn"]:focus', { label: 'new field form, label focused' });
    await op.type(label, { label: 'field label' });
    await op.press('Enter', { label: 'save (Enter)' });
    await op.waitFor('.list-record [role="status"]', { label: 'saved' });
    // Record the value on the contact.
    await op.click(link(op.page, 'Contacts'), { label: 'Contacts (navigation)' });
    await op.waitFor(() => document.activeElement && document.activeElement.matches('input.search, input[type="search"]'), { label: 'contact list ready, search focused' });
    await op.type(ctx.state.shortest, { label: 'the shortest word prefixes of the name' });
    await op.waitFor(n => [...document.querySelectorAll('table[role=grid] tbody tr')].some(r => r.textContent.includes(n)), { label: 'the contact in the list', arg: name });
    await op.press('Enter', { label: 'open the best match' });
    const input = op.page.locator('.list-record .field', { has: op.page.locator('label', { hasText: new RegExp(`^${label}$`) }) }).locator('input').first();
    await op.waitFor(input, { label: 'the new field on the contact form' });
    await op.click(input, { label: 'the new field' });
    await op.type(value, { label: 'value' });
    await op.shot('field added and filled in');
    await op.press('Control+s', { label: 'save (Ctrl+S)' });
    await op.waitFor('.list-record [role="status"]', { label: 'saved' });
    // Filter the contact list by the new field: Alt+Shift+F in the field lists the records that hold its
    // value (the field becomes a column, filtered by "is" the value, the form closes).
    await op.press('Alt+Shift+F', { label: 'list the records that hold this value (Alt+Shift+F)' });
    await op.waitFor(n => {
      const rows = [...document.querySelectorAll('table[role=grid] tbody tr')];
      return rows.length === 1 && rows[0].textContent.includes(n);
    }, { label: 'filtered to one contact', arg: name });
    return {};
  },
  async verify(ctx) {
    const { label, value } = ctx.task.input;
    const { name } = ctx.needles.contact;
    const api = new OursApi(ctx.product).withBrowserSession(await ctx.context.cookies());
    const fields = await fieldsLabelled(api, label);
    const field = fields[0];
    const contact = ctx.state.contactId ? await api.get(`/api/contacts/contacts/${ctx.state.contactId}`) : null;
    const entry = (contact?.customFieldDefinitions ?? []).find(e => e.key === field?.key);
    const ui = await ctx.read(() => ({
      rows: [...document.querySelectorAll('table[role=grid] tbody tr')].map(r => r.innerText.replace(/\s+/g, ' ').trim()),
    }));
    const listed = ui.rows.length === 1 && ui.rows[0].includes(name);
    return { verified: fields.length === 1 && field.fieldType === 'text' && entry?.value === value && listed, details: { field: field ?? null, stored: entry?.value ?? null, rows: ui.rows } };
  },
  async cleanup(ctx) { await switchOff(ctx); },
};
