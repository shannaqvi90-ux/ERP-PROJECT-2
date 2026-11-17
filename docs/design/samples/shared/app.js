// Behaviour shared by the sample pages: theme, density, language link, menus, row selection, keyboard.
(function () {
  var h = document.documentElement;
  var q = new URLSearchParams(location.search);
  function qs() { var p = new URLSearchParams(); if (h.dataset.theme !== 'light') p.set('theme', h.dataset.theme); var d0 = h.getAttribute('data-density-default'); p.set('density', h.dataset.density); var s = p.toString(); return s ? '?' + s : ''; }
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
    var nb = e.target.closest('[data-nav-toggle]');
    if (nb) document.body.classList.toggle('nav-collapsed');
  });
  document.addEventListener('keydown', function (e) {
    var t = e.target, inField = /INPUT|TEXTAREA/.test(t.tagName) && t.type !== 'checkbox';
    if (e.key === '/' && !inField) { var s = document.querySelector('[data-search]'); if (s) { e.preventDefault(); s.focus(); } }
    if (e.key === 'Escape') document.querySelectorAll('.menu').forEach(function (x) { x.hidden = true; });
    var row = t.closest && t.closest('tr.row');
    if (row && (e.key === 'ArrowDown' || e.key === 'ArrowUp')) { var n = e.key === 'ArrowDown' ? row.nextElementSibling : row.previousElementSibling; if (n) { e.preventDefault(); n.focus(); } }
    if (row && e.key === 'Enter') row.click();
  });
  sync();
})();
