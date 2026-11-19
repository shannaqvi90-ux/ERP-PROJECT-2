// Direction G command palette: finds contacts, invoices and orders, runs actions on them, and jumps
// anywhere. Opened by Ctrl K or the command field (shared/app.js sets html[data-palette=open]); this
// file renders results, filters as you type, and handles the keys inside the palette.
// Keys: Up/Down move, Enter runs, Tab shows the actions of the highlighted record, Shift+Tab or
// Backspace on an empty field goes back, Esc closes. Prefix > for commands only, # for documents only.
(function () {
  var h = document.documentElement;
  var pal = document.querySelector('.palette');
  var dataEl = document.getElementById('pal-data');
  if (!pal || !dataEl) return;
  var D = JSON.parse(dataEl.textContent);
  var input = pal.querySelector('input');
  var list = pal.querySelector('.pal-body');
  var scope = pal.querySelector('.pal-scope');
  var ctxEl = pal.querySelector('.pal-ctx');
  var toastEl = document.querySelector('.toast');
  var byId = {};
  var state = { rec: null, act: 0, flat: [] };
  var toastTimer = 0;

  // ---- text helpers: normalise Latin case and Arabic letter forms, keep a map back to the original
  function norm(s) {
    var out = '', map = [];
    for (var i = 0; i < s.length; i++) {
      var ch = s[i];
      if (/[ً-ْٰـ]/.test(ch)) continue; // short vowels, tatweel
      if (/[آأإٱ]/.test(ch)) ch = 'ا';
      else if (ch === 'ى' || ch === 'ئ') ch = 'ي';
      else if (ch === 'ة') ch = 'ه';
      else if (ch === 'ؤ') ch = 'و';
      else ch = ch.toLowerCase();
      out += ch; map.push(i);
    }
    return { s: out, map: map };
  }
  function esc(s) { return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;'); }
  function fill(t, name) { return String(t).replace(/\{name\}/g, name); }
  function svg(n) { return '<svg class="ic" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="' + (D.icons[n] || D.icons.check) + '"/></svg>'; }
  function keys(k, cls) { return k ? '<span class="keys' + (cls ? ' ' + cls : '') + '">' + k.split(' ').map(function (x) { return '<kbd>' + esc(x) + '</kbd>'; }).join('') + '</span>' : ''; }
  function qs() {
    var p = new URLSearchParams();
    if (h.dataset.theme !== (h.getAttribute('data-theme-default') || 'light')) p.set('theme', h.dataset.theme);
    p.set('density', h.dataset.density);
    return '?' + p.toString();
  }

  D.items.forEach(function (it) { it.tn = norm(it.t); it.kn = norm((it.kw || '') + ' ' + (it.tDark || '') + ' ' + (it.tLight || '') + ' ' + (it.tCompact || '') + ' ' + (it.tComfortable || '')).s; byId[it.id] = it; });

  // Labels that depend on the current state (theme and density switches).
  function label(it) {
    if (it.id === 'theme') return h.dataset.theme === 'light' ? it.tDark : it.tLight;
    if (it.id === 'density') return h.dataset.density === 'compact' ? it.tComfortable : it.tCompact;
    return it.t;
  }
  function iconOf(it) {
    if (it.id === 'theme') return h.dataset.theme === 'light' ? 'moon' : 'sun';
    return it.icon;
  }

  // ---- matching: label prefix > word start > inside the label > keywords (name, TRN, city, both languages)
  function match(it, q, words) {
    var lab = it.id === 'theme' || it.id === 'density' ? norm(label(it)) : it.tn;
    var i = lab.s.indexOf(q);
    if (i >= 0) {
      var start = i === 0 || /[\s\-·(,]/.test(lab.s[i - 1]);
      return { score: i === 0 ? 100 : start ? 80 : 60, range: [lab.map[i], lab.map[i + q.length - 1] + 1] };
    }
    var hay = lab.s + ' ' + it.kn;
    for (var w = 0; w < words.length; w++) if (hay.indexOf(words[w]) < 0) return null;
    return { score: 40, range: null };
  }
  function mark(text, range) {
    if (!range) return esc(text);
    return esc(text.slice(0, range[0])) + '<mark>' + esc(text.slice(range[0], range[1])) + '</mark>' + esc(text.slice(range[1]));
  }

  // Actions generated for a contact: statement, new invoice, payment.
  function dynFor(ct, q) {
    var out = [
      { id: 'dyn-st-' + ct.id, icon: 'print', t: fill(D.dyn.statement, ct.t), s: ct.od || '', k: 'S', toast: fill(D.dyn.statementToast, ct.t) },
      { id: 'dyn-in-' + ct.id, icon: 'invoice', t: fill(D.dyn.invoice, ct.t), k: 'N I', toast: fill(D.dyn.invoiceToast, ct.t) },
      { id: 'dyn-pa-' + ct.id, icon: 'coins', t: fill(D.dyn.payment, ct.t), k: 'P', toast: fill(D.dyn.paymentToast, ct.t) },
    ];
    out.forEach(function (o) { o.g = 'act'; o.type = 'action'; o.html = q ? mark(o.t, rangeIn(o.t, q)) : esc(o.t); });
    return out;
  }
  function rangeIn(text, q) { var n = norm(text), i = n.s.indexOf(q); return i < 0 ? null : [n.map[i], n.map[i + q.length - 1] + 1]; }

  // ---- build the groups for the current query
  function compute() {
    var raw = input.value;
    var groups = [];
    if (state.rec) {
      var q0 = norm(raw.trim()).s;
      var acts = (D.recActs[state.rec.type] || []).map(function (a) {
        return { id: 'ra-' + a[0], icon: a[1], t: a[2], k: a[3], g: 'act', type: 'action', recAct: a[0], toast: a[4] ? fill(a[4], state.rec.t.split(' · ')[0]) : '' };
      }).filter(function (a) { return !q0 || norm(a.t).s.indexOf(q0) >= 0; });
      acts.forEach(function (a) { a.html = mark(a.t, q0 ? rangeIn(a.t, q0) : null); });
      if (acts.length) groups.push({ t: fill(D.i18n.actionsOn, state.rec.t), items: acts });
      scope.textContent = state.rec.type === 'contact' ? D.groups.rec : D.i18n.scopeDoc;
      return groups;
    }
    var mode = '';
    var text = raw;
    if (/^\s*>/.test(text)) { mode = 'cmd'; text = text.replace(/^\s*>/, ''); }
    else if (/^\s*#/.test(text)) { mode = 'doc'; text = text.replace(/^\s*#/, ''); }
    scope.textContent = mode === 'cmd' ? D.i18n.scopeCmd : mode === 'doc' ? D.i18n.scopeDoc : D.i18n.scopeAll;
    var q = norm(text.trim()).s;
    if (!q && !mode) {
      D.suggest.forEach(function (sg) {
        var its = [];
        sg.ids.forEach(function (id) {
          if (id.indexOf('dyn:') === 0) { var ct = byId[id.slice(4)]; if (ct) its = its.concat(dynFor(ct, '')); return; }
          var it = byId[id]; if (it) its.push(Object.assign({}, it, { html: esc(label(it)) }));
        });
        if (its.length) groups.push({ t: sg.t, items: its });
      });
      return groups;
    }
    var words = q.split(/\s+/).filter(Boolean);
    var res = { rec: [], act: [], go: [] };
    D.items.forEach(function (it, n) {
      if (mode === 'cmd' && it.g === 'rec') return;
      if (mode === 'doc' && (it.g !== 'rec' || it.type === 'contact')) return;
      var m = q ? match(it, q, words) : { score: 1, range: null };
      if (!m) return;
      res[it.g].push(Object.assign({}, it, { score: m.score, n: n, html: mark(label(it), m.range) }));
    });
    Object.keys(res).forEach(function (g) { res[g].sort(function (a, b) { return b.score - a.score || a.n - b.n; }); });
    var topContact = null;
    for (var i = 0; i < res.rec.length; i++) if (res.rec[i].type === 'contact') { topContact = res.rec[i]; break; }
    if (topContact && mode !== 'doc') res.act = dynFor(byId[topContact.id], q).concat(res.act);
    if (res.rec.length) groups.push({ t: D.groups.rec, items: pickRecs(res.rec) });
    if (res.act.length) groups.push({ t: D.groups.act, items: res.act.slice(0, 5) });
    if (res.go.length) groups.push({ t: D.groups.go, items: res.go.slice(0, 4) });
    return groups;
  }

  // At most six records, mixing types: up to three contacts, two invoices and one order first.
  function pickRecs(arr) {
    if (arr.length <= 6) return arr;
    var quota = { contact: 3, invoice: 2, order: 1 }, out = [], rest = [];
    arr.forEach(function (r) { if (quota[r.type] > 0) { quota[r.type]--; out.push(r); } else rest.push(r); });
    while (out.length < 6 && rest.length) out.push(rest.shift());
    return out.sort(function (a, b) { return b.score - a.score || a.n - b.n; });
  }
  function render() {
    var groups = compute();
    state.flat = [];
    var html = '';
    groups.forEach(function (g, gi) {
      html += '<div class="pal-g" role="group" aria-labelledby="pal-g' + gi + '"><p class="pal-gt" id="pal-g' + gi + '">' + esc(g.t) + '</p>';
      g.items.forEach(function (it) {
        var n = state.flat.length;
        state.flat.push(it);
        var canTab = !state.rec && (it.type === 'contact' || it.type === 'invoice' || it.type === 'order');
        html += '<div class="pal-i" role="option" id="pal-o' + n + '" data-n="' + n + '" aria-selected="false">' +
          '<span class="pal-ic">' + svg(iconOf(it)) + '</span>' +
          '<span class="pal-t" title="' + esc(label(it)) + '">' + (it.html || esc(label(it))) + '</span>' +
          (it.s ? '<span class="pal-s" title="' + esc(it.s.replace(/<[^>]+>/g, '')) + '">' + it.s + '</span>' : '<span class="pal-s"></span>') +
          (it.hint ? '<span class="pal-hint">' + esc(it.hint) + '</span>' : '') +
          (canTab ? '<span class="pal-tab">' + keys('Tab') + '</span>' : '') +
          (it.k ? keys(it.k) : canTab || state.rec ? keys('↵', 'k-enter') : '') + '</div>';
      });
      html += '</div>';
    });
    if (!state.flat.length) {
      var raw = input.value.replace(/^\s*[>#]/, '').trim();
      html = '<p class="pal-empty">' + svg('warn') + '<span>' + esc(D.i18n.none.replace('{q}', raw)) + '</span></p>';
    }
    if (D.items.length < 20 && !state.rec) html += '<p class="pal-note">' + esc(D.i18n.signNote) + '</p>';
    list.innerHTML = html;
    state.act = 0;
    highlight();
  }
  function highlight(scroll) {
    var opts = list.querySelectorAll('.pal-i');
    opts.forEach(function (o, i) { var on = i === state.act; o.classList.toggle('is-on', on); o.setAttribute('aria-selected', String(on)); });
    if (opts[state.act]) { input.setAttribute('aria-activedescendant', opts[state.act].id); if (scroll) opts[state.act].scrollIntoView({ block: 'nearest' }); }
    else input.removeAttribute('aria-activedescendant');
  }

  // ---- record mode (Tab): the actions of one record
  function enterRec(it) {
    state.rec = it;
    ctxEl.hidden = false;
    ctxEl.innerHTML = svg(it.icon) + '<span>' + esc(it.t.split(' · ')[0]) + '</span>';
    ctxEl.title = it.t;
    input.value = '';
    input.placeholder = D.i18n.phRec;
    render();
  }
  function exitRec() {
    state.rec = null;
    ctxEl.hidden = true;
    input.value = '';
    input.placeholder = D.i18n.ph;
    render();
  }

  // ---- running an item
  function close() { delete h.dataset.palette; }
  function toast(msg) {
    if (!toastEl) return;
    toastEl.innerHTML = '<span class="toast-ic">' + svg('check') + '</span><span class="toast-x"><b>' + esc(msg) + '</b><small>' + esc(D.i18n.sample) + '</small></span>';
    toastEl.hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(function () { toastEl.hidden = true; }, 3600);
  }
  function go(href) { location.href = href + '.' + D.lang + '.html' + qs(); }
  function run(it) {
    if (!it) return;
    if (it.recAct === 'open' || (!it.recAct && (it.type === 'contact' || it.type === 'invoice' || it.type === 'order'))) {
      var rec = it.recAct ? state.rec : it;
      if (rec.href) { go(rec.href); return; }
      var openAct = (D.recActs[rec.type] || [])[0];
      close(); toast(rec.type === 'contact' ? D.i18n.one : (it.toast || (openAct && openAct[4] ? fill(openAct[4], rec.t.split(' · ')[0]) : rec.t))); return;
    }
    if (it.cmd === 'theme') { var t = h.dataset.theme === 'light' ? 'dark' : 'light'; var b = document.querySelector('[data-switch=theme] button[data-v=' + t + ']'); close(); if (b) b.click(); return; }
    if (it.cmd === 'density') { var d = h.dataset.density === 'compact' ? 'comfortable' : 'compact'; var b2 = document.querySelector('[data-switch=density] button[data-v=' + d + ']'); close(); if (b2) b2.click(); return; }
    if (it.cmd === 'lang') { var a = document.querySelector('a[data-lang-swap]'); if (a) location.href = a.href; return; }
    if (it.cmd === 'print') { close(); setTimeout(function () { window.print(); }, 50); return; }
    if (it.href) { go(it.href); return; }
    close(); toast(it.toast || label(it));
  }

  // ---- events
  input.addEventListener('input', function () { if (!state.rec) input.placeholder = D.i18n.ph; render(); });
  input.addEventListener('keydown', function (e) {
    var n = state.flat.length;
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault();
      if (!n) return;
      state.act = (state.act + (e.key === 'ArrowDown' ? 1 : -1) + n) % n;
      highlight(true);
    } else if (e.key === 'PageDown' || e.key === 'PageUp') {
      e.preventDefault();
      if (!n) return;
      state.act = e.key === 'PageDown' ? n - 1 : 0;
      highlight(true);
    } else if (e.key === 'Enter') {
      e.preventDefault();
      run(state.flat[state.act]);
    } else if (e.key === 'Tab') {
      e.preventDefault();
      var cur = state.flat[state.act];
      if (e.shiftKey) { if (state.rec) exitRec(); return; }
      if (!state.rec && cur && (cur.type === 'contact' || cur.type === 'invoice' || cur.type === 'order')) enterRec(byId[cur.id] || cur);
    } else if (e.key === 'Backspace' && state.rec && !input.value) {
      e.preventDefault(); exitRec();
    } else if (e.key === 'Escape' && state.rec) {
      e.preventDefault(); e.stopPropagation(); exitRec();
    }
  });
  list.addEventListener('mousedown', function (e) { e.preventDefault(); });
  list.addEventListener('mousemove', function (e) {
    var o = e.target.closest('.pal-i'); if (!o) return;
    var i = +o.dataset.n; if (i !== state.act) { state.act = i; highlight(); }
  });
  list.addEventListener('click', function (e) { var o = e.target.closest('.pal-i'); if (o) run(state.flat[+o.dataset.n]); });
  ctxEl.addEventListener('click', exitRec);

  // Open and close come from app.js (Ctrl K, the command field, the scrim, Esc) or from ?palette=open.
  var was = false;
  function onChange(initial) {
    var isOpen = h.dataset.palette === 'open';
    if (isOpen && !was) {
      state.rec = null; ctxEl.hidden = true; input.placeholder = D.i18n.ph;
      input.value = initial && D.demo ? D.demo : '';
      render();
      setTimeout(function () { input.focus(); input.setSelectionRange(input.value.length, input.value.length); }, 0);
    }
    was = isOpen;
  }
  new MutationObserver(function () { onChange(false); }).observe(h, { attributes: true, attributeFilter: ['data-palette'] });
  // Remember the theme and density chosen on any page (read back by the inline script in <head>).
  function remember() { try { localStorage.setItem('bz-g-theme', h.dataset.theme); localStorage.setItem('bz-g-density', h.dataset.density); } catch (e) {} }
  new MutationObserver(remember).observe(h, { attributes: true, attributeFilter: ['data-theme', 'data-density'] });
  if (/[?&](theme|density)=/.test(location.search)) remember();
  input.placeholder = D.i18n.ph;
  onChange(true);
})();
