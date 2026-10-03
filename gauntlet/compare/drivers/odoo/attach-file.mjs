import path from 'node:path';
import { adminRpc, openRecord, signInAs } from './_common.mjs';

async function removeAttachments(ctx) {
  const rpc = await adminRpc(ctx);
  const ids = await rpc.search('ir.attachment', [['res_model', '=', 'res.partner'], ['res_id', '=', ctx.state.contactId], ['name', '=', ctx.task.input.file]]);
  await rpc.unlink('ir.attachment', ids);
}

export default {
  built: true,
  path: 'Paperclip on the record (it already holds a file, so the attachment box opens) > Attach files > choose the file in the file dialog.',
  async setup(ctx) {
    const rpc = await adminRpc(ctx);
    const [id] = await rpc.search('res.partner', [['ref', '=', ctx.needles.contact.ref]]);
    if (!id) throw new Error('needle contact missing; run tools/odoo-reference/up.sh');
    ctx.state.contactId = id;
    await removeAttachments(ctx);
  },
  async signIn(ctx) {
    await signInAs(ctx, 'admin');
    await openRecord(ctx, 'res.partner', ctx.state.contactId);
  },
  async run(op, ctx) {
    const file = path.join(ctx.harnessDir, 'data', 'fixtures', ctx.task.input.file);
    await op.click('.o-mail-Chatter .o-mail-Chatter-attachFiles', { label: 'paperclip (attachment box)' });
    const attach = op.page.locator('.o-mail-AttachmentBox button', { hasText: 'Attach files' });
    await op.waitFor(attach, { label: 'attachment box' });
    await op.pickFile(attach, file, { label: ctx.task.input.file });
    await op.waitFor(name => [...document.querySelectorAll('.o-mail-Chatter .o-mail-AttachmentContainer, .o-mail-Chatter .o-mail-AttachmentCard, .o-mail-Chatter .o-mail-AttachmentImage, .o-mail-Chatter [title]')]
      .some(e => (e.innerText || e.getAttribute('title') || '').includes(name)) && !document.querySelector('.o-mail-Chatter [class*="uploading" i], .o-mail-Chatter [class*="Uploading"], .o-mail-Chatter .fa-spinner, .o-mail-Chatter .fa-circle-o-notch'),
    { label: 'file listed on the record', arg: ctx.task.input.file });
    return {};
  },
  async verify(ctx) {
    const rpc = await adminRpc(ctx);
    const rows = await rpc.searchRead('ir.attachment', [['res_model', '=', 'res.partner'], ['res_id', '=', ctx.state.contactId], ['name', '=', ctx.task.input.file]], ['id', 'file_size']);
    return { verified: rows.length === 1 && rows[0].file_size > 0, details: { attachments: rows } };
  },
  async cleanup(ctx) { if (ctx.state.contactId) await removeAttachments(ctx); },
};
