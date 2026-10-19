import { adminRpc, expertPaths, openRecord, saveForm, signInAs } from './_common.mjs';

async function needle(ctx) {
  const rpc = await adminRpc(ctx);
  const [c] = await rpc.searchRead('res.partner', [['ref', '=', ctx.needles.contact.ref]], ['id', 'phone']);
  if (!c) throw new Error('needle contact missing; run tools/odoo-reference/up.sh');
  return c;
}

/**
 * The number as the reference's shortest credited entry types it: its digits only (no spaces, no +;
 * the phone field keeps what is typed, and the instrument's entered-value rule is "separators free":
 * 971505550199 holds +971 50 555 0199). The field shows no country code of its own, so the code is
 * typed (round 13, critic p06 r7: typing only the 9 digits after it stores 505550199, which is not
 * the number).
 */
const digitsOnly = phone => phone.replace(/\D/g, '');

const PHONE = '.o_form_view .o_field_widget[name="phone"] input';

/** A value's letters and digits in order: the instrument's entered-value rule (separators free). */
const plain = s => String(s ?? '').replace(/[^\p{L}\p{N}]/gu, '');

function build(keyboard) {
  return async (op, ctx) => {
    // A triple click selects the field's whole content (round 13, critic p06 r7: one step where a
    // click and Ctrl+A took two); typing replaces it.
    await op.tripleClick(PHONE, { label: 'phone field (selects the number)' });
    await op.type(digitsOnly(ctx.task.input.phone), { label: 'new number' });
    await saveForm(op, keyboard);
    return {};
  };
}

export default {
  built: true,
  path: 'Triple-click the phone field > type the number\'s digits > Save.',
  ...expertPaths(build, {
    keyboard: 'Triple-click the phone field > type the digits > Alt+S',
    pointer: 'Triple-click the phone field > type the digits > click Save',
  }),
  async setup(ctx) {
    const c = await needle(ctx);
    ctx.state.contact = c;
    await (await adminRpc(ctx)).write('res.partner', [c.id], { phone: ctx.needles.contact.mobile });
  },
  async signIn(ctx) {
    await signInAs(ctx, 'admin');
    await openRecord(ctx, 'res.partner', ctx.state.contact.id);
    await ctx.page.locator(PHONE).waitFor();
  },
  ready: PHONE,
  // The instrument's own rule for an entered value (lib/runner.mjs containsValue: letters and digits in
  // order, separators free), not the exact spaced text (lead decision on critic p06 r7): the number
  // saved and shown is the task's, however it is spaced.
  async verify(ctx) {
    const [c] = await (await adminRpc(ctx)).read('res.partner', [ctx.state.contact.id], ['phone']);
    const shown = await ctx.page.locator(PHONE).inputValue();
    const want = ctx.task.input.phone;
    return { verified: plain(c.phone) === plain(want) && plain(shown) === plain(want), details: { saved: c.phone, shown } };
  },
  async cleanup(ctx) {
    if (ctx.state.contact) await (await adminRpc(ctx)).write('res.partner', [ctx.state.contact.id], { phone: ctx.needles.contact.mobile });
  },
};
