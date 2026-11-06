// Critic p16-contacts r2.
import { adminRpc, expertPaths, openApp, saveForm, signInAs, chatterSettled } from './_common.mjs';

const plain = s => String(s ?? '').replace(/[^\p{L}\p{N}]/gu, '');
const digitsOnly = phone => phone.replace(/\D/g, '');

async function remove(ctx) {
  const rpc = await adminRpc(ctx);
  const { name, email } = ctx.task.input;
  const ids = await rpc.call('res.partner', 'search', [['|', ['name', '=', name], ['email', '=', email]]], { context: { active_test: false } });
  if (ids.length) await rpc.unlink('res.partner', ids);
}

function build(keyboard) {
  return async (op, ctx) => {
    const { name, email, phone } = ctx.task.input;
    await openApp(op, 'Contacts');
    if (keyboard) await op.press('Alt+c', { label: 'New (hotkey)' });
    else await op.click('.o_control_panel .o_list_button_add, .o_control_panel .o-kanban-button-new', { label: 'New' });
    await op.waitFor('.o_form_view .o_field_widget[name="name"] input:focus', { label: 'new contact form, name focused' });
    await op.type(name, { label: 'name' });
    await op.click('.o_form_view .o_field_widget[name="email"] input', { label: 'e-mail field' });
    await op.type(email, { label: 'e-mail' });
    await op.click('.o_form_view .o_field_widget[name="phone"] input', { label: 'phone field' });
    await op.type(digitsOnly(phone), { label: 'phone' });
    await op.shot('contact filled in');
    await saveForm(op, keyboard);
    await op.waitFor(chatterSettled, { label: 'the new record\'s thread loaded' });
    return {};
  };
}

export default {
  built: true,
  path: 'Apps menu > Contacts > New > name > e-mail field > e-mail > phone field > digits > Save.',
  ...expertPaths(build, {
    keyboard: 'Apps menu > Contacts > Alt+C > name > click e-mail > e-mail > click phone > digits > Alt+S',
    pointer: 'Apps menu > Contacts > New > name > click e-mail > e-mail > click phone > digits > click Save',
  }),
  async setup(ctx) { await remove(ctx); },
  async signIn(ctx) { await signInAs(ctx, 'admin'); },
  async verify(ctx) {
    const rpc = await adminRpc(ctx);
    const { name, email, phone } = ctx.task.input;
    const found = await rpc.searchRead('res.partner', [['name', '=', name]], ['id', 'name', 'email', 'phone']);
    return { verified: found.length === 1 && found[0].email === email && plain(found[0].phone) === plain(phone), details: { found } };
  },
  async cleanup(ctx) { await remove(ctx); },
};
