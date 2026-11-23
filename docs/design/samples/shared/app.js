// Behaviour shared by the sample pages: theme, density, language link, menus, row selection, keyboard.
(function () {
  var h = document.documentElement;
  var q = new URLSearchParams(location.search);
  function qs() { var p = new URLSearchParams(); if (h.dataset.theme !== (h.getAttribute('data-theme-default') || 'light')) p.set('theme', h.dataset.theme); p.set('density', h.dataset.density); var s = p.toString(); return s ? '?' + s : ''; }
  function palette(open) { if (!document.querySelector('.palette')) return; if (open) { h.dataset.palette = 'open'; var i = document.querySelector('.palette input'); if (i) i.focus(); } else delete h.dataset.palette; }
  function sync() {
    document.querySelectorAll('[data-switch]').forEach(function (g) {
      var cur = g.dataset.switch === 'theme' ? h.dataset.theme : h.dataset.density;
      g.querySelectorAll('button').forEach(function (b) { b.setAttribute('aria-pressed', String(b.dataset.v === cur)); });
    });
    document.querySelectorAll('a[data-lang-swap]').forEach(function (a) {
      var f = location.pathname.split('/').pop();
      a.href = f.replace(/\.(en|ar)\.html$/, function (m, l) { return '.' + (l === 'en' ? 'ar' : 'en') + '.html'; }) + qs();
    });
    document.querySelectorAll('a[data-keep]:not([data-lang-swap])').forEach(function (a) { var b = a.getAttribute('data-base') || a.getAttribute('href'); a.setAttribute('data-base', b); a.href = b.split('?')[0] + qs(); });
    try { history.replaceState(null, '', location.pathname + qs()); } catch (e) {}
  }
  document.addEventListener('click', function (e) {
    var b = e.target.closest('[data-switch] button');
    if (b) { var k = b.parentNode.dataset.switch; if (k === 'theme') h.dataset.theme = b.dataset.v; else h.dataset.density = b.dataset.v; sync(); return; }
    var m = e.target.closest('.menu-wrap > button');
    document.querySelectorAll('.menu').forEach(function (x) { if (!m || x.previousElementSibling !== m) x.hidden = true; });
    if (m) { var mm = m.nextElementSibling; mm.hidden = !mm.hidden; return; }
    var tab = e.target.closest('[role=tab]');
    if (tab) { tab.parentNode.querySelectorAll('[role=tab]').forEach(function (t) { t.setAttribute('aria-selected', String(t === tab)); }); return; }
    var row = e.target.closest('tbody tr.row');
    if (row && !e.target.closest('a,button')) { row.parentNode.querySelectorAll('tr.is-selected').forEach(function (r) { r.classList.remove('is-selected'); var c = r.querySelector('input'); if (c) c.checked = false; }); row.classList.add('is-selected'); var c2 = row.querySelector('input'); if (c2) c2.checked = true; }
    if (e.target.closest('[data-palette-open]')) { palette(true); return; }
    if (e.target.closest('.palette-scrim')) { palette(false); return; }
    var gt = e.target.closest('[data-group-toggle]');
    if (gt) { var tb = gt.closest('tbody'); tb.classList.toggle('is-collapsed'); gt.setAttribute('aria-expanded', String(!tb.classList.contains('is-collapsed'))); return; }
    var nb = e.target.closest('[data-nav-toggle]');
    if (nb) document.body.classList.toggle('nav-collapsed');
  });
  document.addEventListener('keydown', function (e) {
    var t = e.target, inField = /INPUT|TEXTAREA/.test(t.tagName) && t.type !== 'checkbox';
    if (e.key === '/' && !inField) { var s = document.querySelector('[data-search]'); if (s) { e.preventDefault(); s.focus(); } }
    if ((e.ctrlKey || e.metaKey) && (e.key === 'k' || e.key === 'K')) { e.preventDefault(); palette(h.dataset.palette !== 'open'); return; }
    if (e.key === 'Escape') { document.querySelectorAll('.menu').forEach(function (x) { x.hidden = true; }); palette(false); }
    var row = t.closest && t.closest('tr.row');
    if (row && (e.key === 'ArrowDown' || e.key === 'ArrowUp')) { var n = e.key === 'ArrowDown' ? row.nextElementSibling : row.previousElementSibling; if (n) { e.preventDefault(); n.focus(); } }
    if (row && e.key === 'Enter') row.click();
  });
  sync();
})();
