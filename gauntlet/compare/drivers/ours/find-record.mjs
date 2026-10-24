import { oursAs } from '../../lib/ours-api.mjs';
import { initialsOf, prefixCandidates } from './find-user.mjs';

// Things are found by role and label (the Contacts link, the palette's input, the record's fields),
// never by layout, so the driver keeps working while the contacts screen changes.
const contactsLink = page => page.getByRole('navigation').getByRole('link', { name: 'Contacts', exact: true }).first();

/** The contact's own record (its form, outside the list grid) shows the mobile number. */
const recordShows = m => [...document.querySelectorAll('form, [role="dialog"], [role="complementary"], aside')]
  .filter(el => !el.closest('table, [role="grid"], [role="rowgroup"]'))
  .some(el => [...el.querySelectorAll('input')].some(i => i.value === m) || el.textContent.includes(m));

/**
 * The fewest letters an expert of this product types to put the contact first: the shortest word
 * prefixes of the name whose ranked search answers the contact as the best match, found through
 * the contacts list's own search before the measured run. Falls back to the first three letters
 * of each word.
 */
async function shortestPrefixes(api, name, id, maxProbes = 400) {
  for (const typed of prefixCandidates(name).slice(0, maxProbes)) {
    const page = await api.get(`/api/contacts/contacts?take=1&search=${encodeURIComponent(typed)}`);
    if (page.items?.[0]?.id === id) return typed;
  }
  return name.split(/\s+/).map(w => w.slice(0, 3)).join(' ').toLocaleLowerCase();
}

// The expert paths. From home: the command palette finds the contact itself (Ctrl+K, the name,
// Enter opens its form); or the Contacts screen (a click on Contacts, or G then O), whose search box
// has the focus on arrival, the name typed, Enter opens the best match.
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
const initials = ctx => ctx.state.initials;

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
    ctx.state.initials = initialsOf(name);
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
    'palette-shortest': { path: 'Ctrl+K > the shortest word prefixes of the name that rank the contact first (found through the list\'s search before the run) > Enter opens the contact\'s form.', run: build('palette', shortest) },
    menu: { path: 'Contacts (navigation; the search box has the focus) > type the name > Enter opens the best match.', run: build('menu', whole) },
    goto: { path: 'G then O (Contacts; the search box has the focus) > type the name > Enter opens the best match.', run: build('goto', whole) },
    shortest: { path: 'Contacts (navigation; the search box has the focus) > the shortest word prefixes of the name that rank the contact first > Enter opens the best match.', run: build('menu', shortest) },
    initials: { path: 'Contacts (navigation; the search box has the focus) > the initials of the name (one letter per word, e.g. "swar") > Enter opens the best match.', run: build('menu', initials) },
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
