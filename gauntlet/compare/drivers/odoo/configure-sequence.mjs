import { adminRpc, developerMode, expertPaths, saveForm, signInAs, technicalMenu } from './_common.mjs';

const DEFAULT = { prefix: 'P', padding: 5 };

async function sequence(ctx) {
  const rpc = await adminRpc(ctx);
  const [s] = await rpc.searchRead('ir.sequence', [['code', '=', 'purchase.order']], ['id', 'name', 'prefix', 'padding']);
  if (!s) throw new Error('purchase order sequence missing (Purchase app not installed?)');
  return s;
}

function build(keyboard) {
  return async (op, ctx) => {
    const { prefix, digits } = ctx.task.input;
    const page = op.page;
    await developerMode(op);
    await technicalMenu(op, 'Sequences');
    await op.waitFor('.o_searchview_input:focus', { label: 'sequence list, search focused' });
    await op.type('Purchase Order', { label: 'search' });
    await op.press('Enter', { label: 'search', chain: true });
    const row = page.locator('.o_data_row', { has: page.locator('td', { hasText: /^Purchase Order$/ }) }).first();
    await op.waitFor(() => document.querySelectorAll('.o_data_row').length >= 1 && document.querySelectorAll('.o_data_row').length <= 3, { label: 'search result' });
    await op.click(row, { label: 'open Purchase Order' });
    const prefixInput = '.o_form_view .o_field_widget[name="prefix"] input';
    await op.waitFor(prefixInput, { label: 'sequence form' });
    await op.click(prefixInput, { label: 'prefix field' });
    await op.press('Control+a', { label: 'select the old prefix', chain: true });
    await op.type(prefix, { label: 'prefix', chain: true });
    const size = '.o_form_view .o_field_widget[name="padding"] input';
    await op.click(size, { label: 'sequence size field' });
    await op.press('Control+a', { label: 'select the old size', chain: true });
    await op.type(String(digits), { label: 'size', chain: true });
    await op.shot('sequence edited');
    await saveForm(op, keyboard);
    return {};
  };
}

export default {
  built: true,
  path: 'Apps menu > Settings > scroll > Activate the developer mode > Technical > (scroll) Sequences > search "Purchase Order" > open > prefix > size > Save.',
  ...expertPaths(build, {
    keyboard: 'Apps menu > Settings > scroll > developer mode > Technical > Sequences > search > open > click prefix, Ctrl+A, type > click size, Ctrl+A, type > Alt+S',
    pointer: 'Apps menu > Settings > scroll > developer mode > Technical > Sequences > search > open > click prefix, Ctrl+A, type > click size, Ctrl+A, type > Save',
  }),
  async setup(ctx) {
    const s = await sequence(ctx);
    ctx.state.sequenceId = s.id;
    await (await adminRpc(ctx)).write('ir.sequence', [s.id], DEFAULT);
  },
  async signIn(ctx) { await signInAs(ctx, 'admin'); },
  async verify(ctx) {
    const rpc = await adminRpc(ctx);
    const [s] = await rpc.read('ir.sequence', [ctx.state.sequenceId], ['prefix', 'padding', 'number_next_actual']);
    const next = `${s.prefix}${String(s.number_next_actual).padStart(s.padding, '0')}`;
    const { prefix, digits } = ctx.task.input;
    return { verified: s.prefix === prefix && s.padding === digits && new RegExp(`^${prefix}\\d{${digits}}$`).test(next), details: { prefix: s.prefix, padding: s.padding, next_number: next } };
  },
  async cleanup(ctx) { if (ctx.state.sequenceId) await (await adminRpc(ctx)).write('ir.sequence', [ctx.state.sequenceId], DEFAULT); },
};
