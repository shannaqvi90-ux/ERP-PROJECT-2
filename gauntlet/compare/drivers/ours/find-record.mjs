import { oursAs } from '../../lib/ours-api.mjs';

// Things are found by role and label (the Contacts link, the palette's input, the record's fields),
// never by layout, so the driver keeps working while the contacts screen changes.
const contactsLink = page => page.getByRole('navigation').getByRole('link', { name: 'Contacts', exact: true }).first();

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

/** The contact's own record (its form, outside the list grid) shows the mobile number. */
const recordShows = m => [...document.querySelectorAll('form, [role="dialog"], [role="complementary"], aside')]
  .filter(el => !el.closest('table, [role="grid"], [role="rowgroup"]'))
  .some(el => [...el.querySelectorAll('input')].some(i => i.value === m) || el.textContent.includes(m));

/**
 * The fewest letters an expert of this product types to put the contact first: the shortest word
 * prefixes of the name (any of its words, in order) whose ranked search answers the contact as the
 * best match, found through the contacts list's own search before the measured run. Each part is at
 * least three letters or a whole word: a part of one or two letters cannot use the search's
 * trigram index, and at 100,000 contacts such a search ("sh w al r") answers in a second instead
 * of milliseconds. Falls back to the first three letters of each word.
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

// The expert paths. From home: the command palette finds the contact itself (Ctrl+K, the name,
// Enter opens its form); or the Contacts screen (a click on Contacts, or G then O), whose search box
// has the focus on arrival, the name (or the shortest prefixes of its words that rank the contact
// first) typed, Enter opens the best match. Tried and left out (round 1, on the shared dataset): the
// initials (seven people share "swar", and the contact is not the best of them) and prefixes of one or
// two letters ("sh w al r": the shortest, but a search the trigram index cannot serve, a second slower).
function build(how, typedOf) {
  return async (op, ctx) => {
    const { name, mobile } = ctx.needles.contact;
    const typed = typedOf(ctx);
    if (how === 'palette') {
      await op.press('Control+k', { label: 'command palette' });
      await op.waitFor(() => document.activeElement && document.activeElement.tagName === 'INPUT', { label: 'palette input focused' });
      await op.type(typed, { label: 'contact name' });
      await op.waitFor(n => {
        const first = document.querySelector('[role="option"][aria-selected="true"]') ?? document.querySelector('[role="option"]');
        return !!first && first.textContent.includes(n);
      }, { label: 'the contact offered first', arg: name });
      await op.shot('result list');
      await op.press('Enter', { label: 'open the contact' });
    } else {
      if (how === 'goto') {
        await op.press('g', { label: 'go to' });
        await op.press('o', { label: 'Contacts' });
      } else {
        await op.click(contactsLink(op.page), { label: 'Contacts' });
      }
      await op.waitFor(() => document.activeElement && document.activeElement.matches('input.search, input[type="search"]'), { label: 'contact list ready, search focused' });
      await op.type(typed, { label: 'contact name' });
      const row = op.page.getByRole('row').filter({ hasText: name }).first();
      await op.waitFor(row, { label: 'the row with the name' });
      await op.shot('result list');
      await op.press('Enter', { label: 'open the best match' });
    }
    await op.waitFor(recordShows, { label: 'the contact\'s record with the mobile number', arg: mobile, timeout: 20_000 });
    return {};
  };
}

const whole = ctx => ctx.needles.contact.name;
const shortest = ctx => ctx.state.shortest;

export default {
  built: true,
  path: 'Ctrl+K > type the contact\'s name > Enter opens the contact\'s form (the palette finds contacts, best match first): the form shows the mobile number.',
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
    ctx.state.shortest = await shortestPrefixes(api, name, contact.id);
  },
  async signIn(ctx) {
    const { login, password } = ctx.product.users.admin;
    const page = ctx.page;
    await page.goto(ctx.product.baseUrl + '/');
    await page.getByLabel('E-mail', { exact: true }).or(page.locator('input[name="email"]')).first().fill(login);
    await page.getByLabel('Password', { exact: true }).or(page.locator('input[name="password"]')).first().fill(password);
    await page.keyboard.press('Enter');
    await contactsLink(page).waitFor();
  },
  ready: page => contactsLink(page),
  run: build('palette', whole),
  variants: {
    palette: { path: 'Ctrl+K > type the contact\'s name > Enter opens the contact\'s form.', run: build('palette', whole) },
    'palette-shortest': { path: 'Ctrl+K > the shortest word prefixes of the name (three letters or more each) that rank the contact first, e.g. "sha wal rom" > Enter opens the contact\'s form.', run: build('palette', shortest) },
    menu: { path: 'Contacts (navigation; the search box has the focus) > type the name > Enter opens the best match.', run: build('menu', whole) },
    goto: { path: 'G then O (Contacts; the search box has the focus) > type the name > Enter opens the best match.', run: build('goto', whole) },
    shortest: { path: 'Contacts (navigation; the search box has the focus) > the shortest word prefixes of the name (three letters or more each) that rank the contact first, e.g. "sha wal rom" > Enter opens the best match.', run: build('menu', shortest) },
  },
  async verify(ctx) {
    const { name, mobile } = ctx.needles.contact;
    const shown = await ctx.read(([n, m]) => {
      const outside = [...document.querySelectorAll('form, [role="dialog"], [role="complementary"], aside')].filter(el => !el.closest('table, [role="grid"]'));
      const text = outside.map(el => el.innerText).join('\n') + '\n' + outside.flatMap(el => [...el.querySelectorAll('input, textarea')].map(i => i.value)).join('\n');
      return { name: text.includes(n), mobile: text.includes(m) };
    }, [name, mobile]);
    return { verified: shown.name && shown.mobile, details: { record_shows_name: shown.name, record_shows_mobile: shown.mobile, url: ctx.page.url() } };
  },
};
