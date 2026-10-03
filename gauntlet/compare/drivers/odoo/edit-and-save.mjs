import { adminRpc, expertPaths, openRecord, saveForm, signInAs } from './_common.mjs';

async function needle(ctx) {
  const rpc = await adminRpc(ctx);
  const [c] = await rpc.searchRead('res.partner', [['ref', '=', ctx.needles.contact.ref]], ['id', 'phone']);
  if (!c) throw new Error('needle contact missing; run tools/odoo-reference/up.sh');
  return c;
}

function build(keyboard) {
  return async (op, ctx) => {
    const { phone } = ctx.task.input;
    // Clicking the field puts the caret in it; select its content and type over it.
    await op.click('.o_form_view .o_field_widget[name="phone"] input', { label: 'phone field' });
    await op.press('Control+a', { label: 'select the old number', chain: true });
    await op.type(phone, { label: 'new number', chain: true });
    await saveForm(op, keyboard);
    return {};
  };
}

export default {
  built: true,
  path: 'Phone field > Ctrl+A > type the number > Save.',
  ...expertPaths(build, {
    keyboard: 'Click the phone field > Ctrl+A > type the number > Alt+S',
    pointer: 'Click the phone field > Ctrl+A > type the number > click Save',
  }),
  async setup(ctx) {
    const c = await needle(ctx);
    ctx.state.contact = c;
    await (await adminRpc(ctx)).write('res.partner', [c.id], { phone: ctx.needles.contact.mobile });
  },
  async signIn(ctx) {
    await signInAs(ctx, 'admin');
    await openRecord(ctx, 'res.partner', ctx.state.contact.id);
    await ctx.page.locator('.o_form_view .o_field_widget[name="phone"] input').waitFor();
  },
  async verify(ctx) {
    const [c] = await (await adminRpc(ctx)).read('res.partner', [ctx.state.contact.id], ['phone']);
    const shown = await ctx.page.locator('.o_form_view .o_field_widget[name="phone"] input').inputValue();
    return { verified: c.phone === ctx.task.input.phone && shown === ctx.task.input.phone, details: { saved: c.phone, shown } };
  },
  async cleanup(ctx) {
    if (ctx.state.contact) await (await adminRpc(ctx)).write('res.partner', [ctx.state.contact.id], { phone: ctx.needles.contact.mobile });
  },
};
