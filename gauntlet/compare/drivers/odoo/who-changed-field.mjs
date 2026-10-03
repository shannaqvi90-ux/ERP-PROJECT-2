import { adminRpc, openApp, signInAs } from './_common.mjs';
import { OdooRpc } from '../../lib/odoo-rpc.mjs';

const EDITOR = { login: 'noor.editor', password: 'noor.editor' };

/** An ordinary internal user with contact rights, named as the task says. */
async function ensureEditor(ctx) {
  const rpc = await adminRpc(ctx);
  const groups = [await rpc.ref('base.group_user'), await rpc.ref('base.group_partner_manager')];
  const values = { name: ctx.task.input.changedBy, password: EDITOR.password, active: true, tz: 'Asia/Dubai', group_ids: [[6, 0, groups]] };
  const found = await rpc.call('res.users', 'search', [[['login', '=', EDITOR.login]]], { context: { active_test: false } });
  if (found.length) await rpc.write('res.users', found, values);
  else await rpc.create('res.users', { ...values, login: EDITOR.login, email: 'noor.editor@demo-trading.example' });
  return EDITOR;
}

export default {
  built: true,
  path: 'Apps menu > Contacts (search focused) > type the name > Enter > open the result; the change log beside the form shows the latest change first (old > new e-mail, who, when).',
  async setup(ctx) {
    const admin = await adminRpc(ctx);
    const [c] = await admin.searchRead('res.partner', [['ref', '=', ctx.needles.contact.ref]], ['id', 'email']);
    if (!c) throw new Error('needle contact missing; run tools/odoo-reference/up.sh');
    if (c.email !== ctx.needles.contact.email) await admin.write('res.partner', [c.id], { email: ctx.needles.contact.email });
    // The change to find, made a moment before the task by an ordinary user who maintains contacts.
    const editor = await ensureEditor(ctx);
    const asEditor = await new OdooRpc(ctx.product).login(editor);
    await asEditor.write('res.partner', [c.id], { email: ctx.task.input.newEmail });
    ctx.state.contact = c;
  },
  async signIn(ctx) { await signInAs(ctx, 'admin'); },
  async run(op, ctx) {
    const { name, email } = ctx.needles.contact;
    const { newEmail, changedBy } = ctx.task.input;
    await openApp(op, 'Contacts');
    await op.waitFor('.o_searchview_input:focus', { label: 'contact list ready, search focused' });
    await op.type(name, { label: 'contact name' });
    await op.press('Enter', { label: 'search', chain: true });
    await op.waitFor(() => document.querySelectorAll('.o_data_row, .o_kanban_record:not(.o_kanban_ghost)').length === 1, { label: 'one result' });
    await op.click(op.page.locator('.o_data_row, .o_kanban_record:not(.o_kanban_ghost)').first(), { label: 'open the result' });
    await op.waitFor(([oldEmail, newE, who]) => {
      const first = document.querySelector('.o-mail-Chatter .o-mail-Message');
      const text = first?.innerText || '';
      return text.includes(oldEmail) && text.includes(newE) && text.includes(who);
    }, { label: 'latest change on screen', arg: [email, newEmail, changedBy] });
    const shown = await op.page.locator('.o-mail-Chatter .o-mail-Message').first().innerText();
    return { shown };
  },
  async verify(ctx, outcome) {
    const { email } = ctx.needles.contact;
    const { newEmail, changedBy } = ctx.task.input;
    const text = outcome.shown || '';
    const hasTime = /\d{1,2}:\d{2}|ago|now/i.test(text);
    return {
      verified: text.includes(email) && text.includes(newEmail) && text.includes(changedBy) && hasTime,
      details: { latest_change_on_screen: text.replace(/\s+/g, ' ').slice(0, 300) },
    };
  },
  async cleanup(ctx) {
    if (ctx.state.contact) await (await adminRpc(ctx)).write('res.partner', [ctx.state.contact.id], { email: ctx.needles.contact.email });
  },
};
