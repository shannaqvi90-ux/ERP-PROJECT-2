import { adminRpc, expertPaths, openRecord, saveForm, signInAs } from './_common.mjs';

async function needle(ctx) {
  const rpc = await adminRpc(ctx);
  const [c] = await rpc.searchRead('res.partner', [['ref', '=', ctx.needles.contact.ref]], ['id', 'phone']);
  if (!c) throw new Error('needle contact missing; run tools/odoo-reference/up.sh');
  return c;
}

function build(keyboard, compact = false) {
  return async (op, ctx) => {
    // Critic p06 r7: the compact variant types the number without spaces, as ours' driver does;
    // Odoo's phone field shows it grouped (+971 50 555 0199) and keeps it as +971505550199.
    const phone = compact ? ctx.task.input.phone.replace(/\s+/g, '') : ctx.task.input.phone;
    // Clicking the field puts the caret in it; select its content and type over it.
    await op.click('.o_form_view .o_field_widget[name="phone"] input', { label: 'phone field' });
    await op.press('Control+a', { label: 'select the old number' });
    await op.type(phone, { label: 'new number' });
    await saveForm(op, keyboard);
    return {};
  };
}

export default {
  built: true,
  path: 'Phone field > Ctrl+A > type the number > Save.',
  run: build(false, true),
  variants: {
    'compact-pointer': { path: 'Click the phone field > Ctrl+A > type +971505550199 > click Save', run: build(false, true) },
    'compact-keyboard': { path: 'Click the phone field > Ctrl+A > type +971505550199 > Alt+S', run: build(true, true) },
  },
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
  ready: '.o_form_view .o_field_widget[name="phone"] input',
  async verify(ctx) {
    const [c] = await (await adminRpc(ctx)).read('res.partner', [ctx.state.contact.id], ['phone']);
    const shown = await ctx.page.locator('.o_form_view .o_field_widget[name="phone"] input').inputValue();
    // Critic p06 r7: the same number, separators free (the instrument's own rule for an entered value),
    // and the field shows it exactly as the task writes it.
    const digits = v => String(v || '').replace(/[^0-9+]/g, '');
    return { verified: digits(c.phone) === digits(ctx.task.input.phone) && shown === ctx.task.input.phone, details: { saved: c.phone, shown } };
  },
  async cleanup(ctx) {
    if (ctx.state.contact) await (await adminRpc(ctx)).write('res.partner', [ctx.state.contact.id], { phone: ctx.needles.contact.mobile });
  },
};
