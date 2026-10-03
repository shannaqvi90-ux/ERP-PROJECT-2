// The "screen" of an API task is the developer's HTTP client: the requests sent and what came
// back. The runner renders it as a neutral page for the start and done screenshots, the same way
// for both products (same layout, fonts and colours; only the requests differ).
const esc = s => String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

export function apiTranscriptHtml(task, steps) {
  const rows = steps.filter(s => s.kind === 'request').map(s => `
    <section><div class="req"><b>${s.n}</b> ${esc(s.text)}</div>
    <div class="res">HTTP ${esc(s.status ?? '')} · ${esc(s.took)} s</div>
    ${s.response ? `<pre>${esc(s.response)}</pre>` : ''}</section>`).join('');
  return `<!doctype html><html lang="en"><head><meta charset="utf-8"><title>Product</title>
<style>
  body { margin: 0; padding: 24px; background: #fff; color: #111; font: 14px/1.5 ui-monospace, Menlo, Consolas, monospace; }
  h1 { font: 600 16px system-ui, sans-serif; margin: 0 0 4px; } p { margin: 0 0 16px; color: #555; font-family: system-ui, sans-serif; }
  section { border-top: 1px solid #ddd; padding: 8px 0; } .res { color: #555; }
  pre { margin: 4px 0 0; white-space: pre-wrap; word-break: break-all; max-height: 160px; overflow: hidden; background: #f4f4f4; padding: 8px; }
</style></head><body>
<h1>HTTP client</h1><p>${esc(task.title)}: ${steps.length ? `${steps.filter(s => s.kind === 'request').length} request(s) sent` : 'signed in, nothing sent yet'}</p>
${rows}
</body></html>`;
}
