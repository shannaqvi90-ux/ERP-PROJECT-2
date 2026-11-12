import fs from 'node:fs';
import path from 'node:path';
import { adminRpc, openRecord, signInAs } from './_common.mjs';

const NAMES = t => [t.input.file, 'delivery-note-critic.txt'];
async function removeAttachments(ctx) {
  const rpc = await adminRpc(ctx);
  const ids = await rpc.search('ir.attachment', [['res_model', '=', 'res.partner'], ['res_id', '=', ctx.state.contactId], ['name', 'in', NAMES(ctx.task)]]);
  await rpc.unlink('ir.attachment', ids);
}

export default {
  built: true,
  path: 'Paperclip on the record (the attachment box opens) > the file\'s card (the viewer opens).',
  async setup(ctx) {
    const rpc = await adminRpc(ctx);
    const [id] = await rpc.search('res.partner', [['ref', '=', ctx.needles.contact.ref]]);
    if (!id) throw new Error('needle contact missing');
    ctx.state.contactId = id;
    await removeAttachments(ctx);
    const datas = fs.readFileSync(path.join(ctx.harnessDir, 'data', 'fixtures', ctx.task.input.file)).toString('base64');
    await rpc.create('ir.attachment', { name: ctx.task.input.file, datas, res_model: 'res.partner', res_id: id, mimetype: ctx.task.input.file.endsWith('.png') ? 'image/png' : 'application/pdf' });
    await rpc.create('ir.attachment', { name: 'delivery-note-critic.txt', datas: Buffer.from('Delivery note\n').toString('base64'), res_model: 'res.partner', res_id: id, mimetype: 'text/plain' });
  },
  async signIn(ctx) {
    await signInAs(ctx, 'admin');
    await openRecord(ctx, 'res.partner', ctx.state.contactId);
  },
  ready: '.o_form_view .o-mail-Chatter',
  async run(op, ctx) {
    const name = ctx.task.input.file;
    const open = await op.page.locator('.o-mail-AttachmentBox').count();
    if (!open) {
      await op.click('.o-mail-Chatter .o-mail-Chatter-attachFiles', { label: 'paperclip (attachment box)' });
      await op.waitFor(op.page.locator('.o-mail-AttachmentBox'), { label: 'attachment box' });
    }
    await op.click(op.page.locator('.o-mail-AttachmentBox').locator(`[title="${name}"], .o-mail-AttachmentCard:has-text("${name}")`).first(), { label: 'the file\'s card' });
    await op.waitFor(op.page.locator('.o-FileViewer'), { label: 'viewer shown' });
    return {};
  },
  async verify(ctx) {
    const shown = await ctx.read(name => ({ viewer: !!document.querySelector('.o-FileViewer'), title: (document.querySelector('.o-FileViewer')?.innerText || '').includes(name) }), ctx.task.input.file);
    return { verified: shown.viewer, details: shown };
  },
  async cleanup(ctx) { if (ctx.state.contactId) await removeAttachments(ctx); },
};
