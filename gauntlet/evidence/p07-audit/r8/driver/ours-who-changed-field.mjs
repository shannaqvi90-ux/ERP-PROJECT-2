import { oursAs } from '../../lib/ours-api.mjs';

// Written by the critic of p07-audit round 4 (the p07 builder left the p00 stub). Things are found by
// role and label, never by layout.
//
// The change the person looks for (the contact's e-mail, changed by {changedBy}, an ordinary user who
// may read and change contacts) is made BEFORE the run, outside the harness, by the critic (curl as that
// user, PUT /api/contacts/contacts/{id}): our contacts API saves a whole contact (the name is required in
// every save), so a set-up write necessarily carries the contact's name and the harness's round-14 guard
// (lib/sandbox/bridge.mjs, setUpCarries) refuses it. Set-up here only reads, and checks that the latest
// change of the contact is that change; clean-up is done by the critic afterwards, also outside.
const contactsLink = page => page.getByRole('navigation').getByRole('link', { name: 'Contacts', exact: true }).first();

/** Word prefixes (up to `longest` letters a word, the name's order), shortest first. */
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

async function shortestPrefixes(api, name, id, maxProbes = 400) {
  const words = name.toLocaleLowerCase().split(/\s+/).filter(Boolean);
  const indexed = typed => typed.split(' ').every(part => part.length >= 3 || words.includes(part));
  for (const typed of prefixCandidates(name).filter(indexed).slice(0, maxProbes)) {
    const page = await api.get(`/api/contacts/contacts?take=1&search=${encodeURIComponent(typed)}`);
    if (page.items?.[0]?.id === id) return typed;
  }
  return name.split(/\s+/).map(w => w.slice(0, 3)).join(' ').toLocaleLowerCase();
}

/** The latest change under the record's heading names the old and new e-mail, the person and a time. */
const latestShows = ([oldEmail, newEmail, who]) => {
  const line = document.querySelector('[data-testid="audit-latest"]');
  const text = line ? line.innerText : '';
  return text.includes(oldEmail) && text.includes(newEmail) && text.includes(who) && !!line.querySelector('time');
};

function build(how, typedOf) {
  return async (op, ctx) => {
    const { name, email } = ctx.needles.contact;
    const { newEmail, changedBy } = ctx.task.input;
    const typed = typedOf(ctx);
    if (how === 'palette') {
      await op.press('Control+k', { label: 'command palette' });
      await op.waitFor(() => document.activeElement && document.activeElement.tagName === 'INPUT', { label: 'palette input focused' });
      await op.type(typed, { label: 'contact name' });
      await op.waitFor(n => {
        const first = document.querySelector('[role="option"][aria-selected="true"]') ?? document.querySelector('[role="option"]');
        return !!first && first.textContent.includes(n);
      }, { label: 'the contact offered first', arg: name });
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
      await op.press('Enter', { label: 'open the best match' });
    }
    await op.waitFor(latestShows, { label: 'the latest change on the record', arg: [email, newEmail, changedBy], timeout: 20_000 });
    return {};
  };
}

const whole = ctx => ctx.needles.contact.name;
const shortest = ctx => ctx.state.shortest;

export default {
  built: true,
  path: 'Ctrl+K > type the contact\'s name > Enter opens the contact: the line under its heading shows the latest change (who, when, old and new e-mail).',
  async setup(ctx) {
    const api = await oursAs(ctx.product, 'admin');
    const { name, email } = ctx.needles.contact;
    const { newEmail, changedBy } = ctx.task.input;
    const found = await api.get(`/api/contacts/contacts?search=${encodeURIComponent(name)}`);
    const contact = found.items.find(c => c.nameEn === name);
    if (!contact) throw new Error(`our product does not hold the dataset contact ${name}; start it with ERP_SEED_CONTACTS_CSV`);
    const history = await api.get(`/api/audit/records/${contact.id}?type=contacts.contacts&take=1`);
    const latest = history.items?.[0];
    const change = latest?.changes?.find(c => c.field === 'email');
    if (!latest || latest.actorName !== changedBy || !change || change.old !== email || change.new !== newEmail) {
      throw new Error(`the contact's latest change is not ${changedBy}'s e-mail change ${email} -> ${newEmail}; make it before the run (see the note at the top of this driver)`);
    }
    ctx.state.contactId = contact.id;
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
    palette: { path: 'Ctrl+K > the contact\'s name > Enter: the heading shows the latest change.', run: build('palette', whole) },
    'palette-shortest': { path: 'Ctrl+K > the shortest word prefixes that rank the contact first > Enter: the heading shows the latest change.', run: build('palette', shortest) },
    menu: { path: 'Contacts (search focused) > the name > Enter: the heading shows the latest change.', run: build('menu', whole) },
    goto: { path: 'G then O (search focused) > the name > Enter: the heading shows the latest change.', run: build('goto', whole) },
    shortest: { path: 'Contacts (search focused) > the shortest word prefixes > Enter: the heading shows the latest change.', run: build('menu', shortest) },
  },
  async verify(ctx) {
    const { email } = ctx.needles.contact;
    const { newEmail, changedBy } = ctx.task.input;
    const shown = await ctx.read(() => {
      const line = document.querySelector('[data-testid="audit-latest"]');
      return { text: line ? line.innerText : '', time: !!line && !!line.querySelector('time') };
    });
    const ok = shown.text.includes(email) && shown.text.includes(newEmail) && shown.text.includes(changedBy) && shown.time;
    return { verified: ok, details: { latest_change_on_screen: shown.text.replace(/\s+/g, ' ').slice(0, 300) } };
  },
};
