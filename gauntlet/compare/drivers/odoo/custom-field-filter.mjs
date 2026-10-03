import { adminRpc, openApp, signInAs } from './_common.mjs';

// Odoo Community's no-code custom field is a "property": defined from any contact's form, stored
// for all contacts, and searchable from the search box. (Studio, which adds real fields and
// columns, is an Enterprise app.)
async function definitionOf(ctx) {
  const rpc = await adminRpc(ctx);
  const [contactId] = await rpc.search('res.partner', [['ref', '=', ctx.needles.contact.ref]], { limit: 1 });
  const [c] = await rpc.read('res.partner', [contactId], ['properties_base_definition_id']);
  const defId = c.properties_base_definition_id[0];
  const [d] = await rpc.read('properties.base.definition', [defId], ['properties_definition']);
  return { rpc, contactId, defId, definition: d.properties_definition || [] };
}

async function removeField(ctx) {
  const { rpc, defId, definition } = await definitionOf(ctx);
  const keep = definition.filter(p => p.string !== ctx.task.input.label);
  if (keep.length !== definition.length) await rpc.write('properties.base.definition', [defId], { properties_definition: keep });
}

export default {
  built: true,
  path: 'Find the contact (Apps menu > Contacts > type the name > Enter > open it) > actions menu (⋮) > Edit Properties > label > Add > type the value > Save > breadcrumb back to Contacts > Backspace (drop the name filter) > type the value > expand "Search Properties" > choose the Licence ref line.',
  async setup(ctx) { await removeField(ctx); },
  async signIn(ctx) { await signInAs(ctx, 'admin'); },
  async run(op, ctx) {
    const page = op.page;
    const { label, value } = ctx.task.input;
    const { name } = ctx.needles.contact;
    // Find the contact.
    await openApp(op, 'Contacts');
    await op.waitFor('.o_searchview_input:focus', { label: 'contact list, search focused' });
    await op.type(name, { label: 'contact name' });
    await op.press('Enter', { label: 'search', chain: true });
    await op.waitFor(() => document.querySelectorAll('.o_data_row, .o_kanban_record:not(.o_kanban_ghost)').length === 1, { label: 'one result' });
    await op.click(page.locator('.o_data_row, .o_kanban_record:not(.o_kanban_ghost)').first(), { label: 'open the contact' });
    await op.waitFor('.o_form_view .o_field_widget[name="name"]', { label: 'contact form' });
    // Define the field.
    await op.click('.o_control_panel .o_cp_action_menus button', { label: 'actions menu' });
    await op.click(page.locator('.o-dropdown--menu .o-dropdown-item', { hasText: 'Edit Properties' }), { label: 'Edit Properties' });
    await op.waitFor('.o_field_property_definition_header:focus', { label: 'new field popover, label focused' });
    await op.type(label, { label: 'field label' });
    await op.click(page.locator('.o_property_field_popover button', { hasText: /^Add$/ }), { label: 'Add' });
    // Record the value on the contact and save.
    const input = page.locator('.o_property_field', { hasText: label }).locator('input').first();
    await op.waitFor(input, { label: 'field on the form' });
    await op.fill(input, value, { label: 'value' });
    await op.shot('field added and filled in');
    await op.click('.o_form_view .o_form_button_save', { label: 'Save' });
    await op.waitFor('.o_form_view .o_form_button_save', { label: 'saved', state: 'hidden' });
    // Filter the contact list by the new field.
    await op.click(page.locator('.o_breadcrumb .breadcrumb-item a', { hasText: 'Contacts' }).first(), { label: 'breadcrumb: Contacts' });
    await op.waitFor('.o_searchview_input:focus', { label: 'contact list' });
    await op.press('Backspace', { label: 'drop the name filter' });
    await op.waitFor(() => !document.querySelector('.o_searchview_facet') && document.querySelectorAll('.o_data_row, .o_kanban_record:not(.o_kanban_ghost)').length > 1,
      { label: 'filter dropped, full list back' });
    await op.type(value, { label: 'value to filter by' });
    await op.click(page.locator('.o-dropdown-item', { hasText: 'Search Properties' }).locator('.o_expand'), { label: 'expand Search Properties' });
    await op.click(page.locator('.o-dropdown-item', { hasText: label }).first(), { label: `${label} line` });
    await op.waitFor(n => document.querySelectorAll('.o_data_row, .o_kanban_record:not(.o_kanban_ghost)').length === 1
      && [...document.querySelectorAll('.o_searchview_facet')].some(f => f.innerText.includes(n)), { label: 'filtered to one contact', arg: label });
    return {};
  },
  async verify(ctx) {
    const { label, value } = ctx.task.input;
    const { rpc, contactId, definition } = await definitionOf(ctx);
    const field = definition.find(p => p.string === label);
    const [c] = await rpc.read('res.partner', [contactId], ['properties']);
    const stored = (c.properties || []).find(p => p.string === label)?.value;
    const ui = await ctx.page.evaluate(() => ({
      rows: [...document.querySelectorAll('.o_data_row')].map(r => r.innerText.replace(/\s+/g, ' ').trim()),
      facets: [...document.querySelectorAll('.o_searchview_facet')].map(f => f.innerText.replace(/\s+/g, ' ')),
    }));
    const listed = ui.rows.length === 1 && ui.rows[0].includes(ctx.needles.contact.name);
    return { verified: !!field && field.type === 'char' && stored === value && listed, details: { field: field || null, stored, ...ui } };
  },
  async cleanup(ctx) { await removeField(ctx); },
};
