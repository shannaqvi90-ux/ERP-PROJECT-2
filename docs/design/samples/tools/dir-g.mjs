// Direction G layout: "Command centre, best of all". F's structure and working touches (sidebar with
// recent records, page header, saved-view tabs with counts, rows grouped by status with sticky AED
// subtotals, Group by / Sort / Columns, the next-best-action hint, the order timeline) set in D's
// dark-first visual language (tokens, Geist / Geist Mono / Readex Pro, crisp grid, restrained glow,
// shortcut keys on buttons, status bar), with D's command palette (Ctrl K) working on every screen.
import { mk2, head2, logos } from './lib2.mjs';
export const id = 'g';
const DEC = { KWD: 3, BHD: 3, OMR: 3 };
export function pages(lang) {
  const c = mk2(lang);
  const { L, ic, num, money, ar, T, X, open } = c;
  const lg = logos();
  // Amounts inside running Arabic text are isolated (U+2066 ... U+2069) so "18,420.00 AED" keeps its
  // order instead of turning into "AED 18,420.00"; isolates also work inside title attributes.
  const iso = (s) => (ar ? `\u2066${s}\u2069` : s);
  // Currency after the amount everywhere, and no amount without its currency.
  c.ACTIONS[1].items[1][2] = L('from 62,000.00 AED', `من ${iso('62,000.00 AED')}`);
  c.ACTIONS[3].items[0][2] = L('limit 4,000.00 AED', `الحد ${iso('4,000.00 AED')}`);
  c.NBA.body = c.NBA.body.replace('18,420.00 AED', iso('18,420.00 AED'));
  const kbd = (...ks) => `<span class="keys">${ks.map((k) => `<kbd>${k}</kbd>`).join('')}</span>`;
  const btn = (icon, label, o = {}) => `<button class="btn${o.primary ? ' btn-primary' : ''}${o.ghost ? ' btn-ghost' : ''}${o.quiet ? ' btn-quiet' : ''}${o.sm ? ' btn-sm' : ''}${o.cls ? ' ' + o.cls : ''}"${o.k ? ` ${T(`${label} (${o.k})`)}` : ''}${o.attr || ''}>${icon ? ic(icon) : ''}<span>${label}</span>${o.k && o.showK !== false ? kbd(...o.k.split(' ')) : ''}</button>`;
  const stLabel = { active: L('Active', 'نشط'), hold: L('On hold', 'موقوف مؤقتًا'), blocked: L('Credit stop', 'إيقاف ائتماني') };
  const plain = (cents, cur) => { const s = c.fmt(Math.abs(cents) / 100, DEC[cur] || 2); return `${cents < 0 ? `(${s})` : s} ${cur}`; };
  const ltr = (s) => `<bdi dir="ltr">${s}</bdi>`;
  const pm = (cents, cur) => `<bdi dir="ltr"${cents < 0 ? ' class="neg-t"' : ''}>${plain(cents, cur)}</bdi>`;

  // ---------- command palette data (records, actions, go to), rendered by palette.js
  const PI = {};
  const items = [];
  const add = (it) => { items.push(it); PI[it.id] = it; return it; };
  const kw = (...a) => a.filter(Boolean).join(' ');
  for (const ct of c.DATA) {
    const x = X[ct.ref];
    const odTxt = ct.ref === c.OPEN ? `${L('3 invoices overdue', '3 فواتير متأخرة')} · ${pm(x.overdue, 'AED')}` : x.overdue ? `${L('Overdue', 'متأخر')} ${pm(x.overdue, ct.currency)}` : '';
    add({ id: ct.ref, g: 'rec', type: 'contact', icon: ct.kind === 'person' ? 'user' : 'building', t: c.ln(ct),
      s: `${ltr(ct.ref)} · ${c.kindT(ct.kind)} · ${c.city(ct.city)} · ${pm(ct.balanceCents, ct.currency)}`,
      kw: kw(ct.name, ct.nameAr, ct.ref, ct.trn, ct.city, c.city(ct.city), ct.email, ct.phone, L('contact customer', 'جهة اتصال عميل')),
      od: odTxt, href: ct.ref === c.OPEN ? 'contact-form' : '', hint: ct.status !== 'active' ? stLabel[ct.status] : '' });
  }
  const nameOf = (ref) => c.ln(c.DATA.find((d) => d.ref === ref));
  const INV = [
    ['INV-2026-01842', 'C000003', 1862500, 'AED', L('open, due 14/12/2026', 'مفتوحة، تستحق 14/12/2026')],
    ['INV-2026-01790', 'C000003', 960000, 'AED', L('12 days late', 'متأخرة 12 يومًا')],
    ['INV-2026-01702', 'C000003', 498000, 'AED', L('26 days late', 'متأخرة 26 يومًا')],
    ['INV-2026-01655', 'C000003', 384000, 'AED', L('41 days late', 'متأخرة 41 يومًا')],
    ['INV-2026-01733', 'C000009', 1248000, 'USD', L('45 days late', 'متأخرة 45 يومًا')],
    ['INV-2026-01698', 'C000023', 4672447, 'AED', L('38 days late', 'متأخرة 38 يومًا')],
    ['INV-2026-01771', 'C000025', 1947225, 'AED', L('21 days late', 'متأخرة 21 يومًا')],
  ];
  for (const [n, ref, cents, cur, st] of INV) add({ id: n, g: 'rec', type: 'invoice', icon: 'invoice', t: `${n} · ${nameOf(ref)}`, s: `${L('Invoice', 'فاتورة')} · ${pm(cents, cur)} · ${st}`, kw: kw(n, c.DATA.find((d) => d.ref === ref).name, c.DATA.find((d) => d.ref === ref).nameAr, L('invoice', 'فاتورة')) });
  const ORD = [
    ['SO-2026-2187', 'C000003', 1862500, 'AED', L('Sales order', 'أمر بيع'), L('invoiced 14/11/2026', 'مفوتر 14/11/2026')],
    ['SO-2026-2210', 'C000026', 3950000, 'AED', L('Sales order', 'أمر بيع'), L('on hold, over the credit limit', 'موقوف، يتجاوز حد الائتمان')],
    ['PO-2026-0418', 'C000005', 8430000, 'AED', L('Purchase order', 'أمر شراء'), L('waiting for your approval', 'بانتظار موافقتك')],
    ['QT-2026-0915', 'C000027', 6420000, 'AED', L('Quotation', 'عرض سعر'), L('sent 17/11/2026', 'أُرسل 17/11/2026')],
  ];
  for (const [n, ref, cents, cur, kind, st] of ORD) add({ id: n, g: 'rec', type: 'order', icon: n.startsWith('PO') ? 'truck' : n.startsWith('QT') ? 'file' : 'cart', t: `${n} · ${nameOf(ref)}`, s: `${kind} · ${pm(cents, cur)} · ${st}`, kw: kw(n, c.DATA.find((d) => d.ref === ref).name, c.DATA.find((d) => d.ref === ref).nameAr, L('order', 'أمر طلب'), kind) });
  const ACT = [
    ['new-contact', 'plus', L('New contact', 'جهة اتصال جديدة'), 'N C', '', L('New contact started', 'بدأ إنشاء جهة اتصال جديدة'), L('add create customer vendor', 'إضافة إنشاء عميل مورد')],
    ['new-invoice', 'invoice', L('New invoice', 'فاتورة جديدة'), 'N I', '', L('New invoice started', 'بدأ إنشاء فاتورة جديدة'), L('create bill sales', 'إنشاء مبيعات')],
    ['record-payment', 'coins', L('Record payment', 'تسجيل دفعة'), 'P', '', L('Payment form opened', 'فُتح نموذج الدفعة'), L('receipt receive money', 'إيصال استلام')],
    ['reminders', 'mail', L('Send overdue reminders', 'إرسال تذكيرات المتأخرات'), 'R', `${L('14 invoices', '14 فاتورة')} · ${pm(18642050, 'AED')}`, L('14 reminders sent', 'أُرسلت 14 تذكيرًا'), L('chase overdue statement statements', 'متابعة متأخرة كشف حساب كشوف')],
    ['approvals', 'check', L('Review approvals', 'مراجعة الموافقات'), 'A', L('5 waiting for you', '5 بانتظارك'), L('Approvals opened', 'فُتحت الموافقات'), L('approve', 'اعتماد')],
    ['invite', 'user', L('Invite user', 'دعوة مستخدم'), 'I', '', L('Invitation form opened', 'فُتح نموذج الدعوة'), L('add user people', 'إضافة مستخدم')],
    ['theme', 'moon', '', '', '', '', L('theme dark light appearance', 'المظهر داكن فاتح')],
    ['density', 'rows', '', '', '', '', L('density compact comfortable rows', 'الكثافة مدمج مريح')],
    ['lang', 'globe', L('Switch to Arabic', 'التبديل إلى الإنجليزية'), '', L('العربية, right to left', 'English, left to right'), '', 'language arabic english العربية الإنجليزية لغة'],
    ['print', 'print', L('Print this page', 'طباعة هذه الصفحة'), 'Ctrl P', '', '', L('print pdf', 'طباعة')],
    ['signout', 'logout', L('Sign out', 'تسجيل الخروج'), '', '', '', L('log out exit', 'خروج')],
  ];
  for (const [aid, icon, t, k, s, toast, extra] of ACT) add({ id: aid, g: 'act', type: 'action', icon, t, k, s, toast, kw: kw(t, extra), href: aid === 'signout' ? 'sign-in' : '', cmd: ['theme', 'density', 'lang', 'print'].includes(aid) ? aid : '' });
  Object.assign(PI.theme, { tDark: L('Switch to dark theme', 'التبديل إلى المظهر الداكن'), tLight: L('Switch to light theme', 'التبديل إلى المظهر الفاتح') });
  Object.assign(PI.density, { tComfortable: L('Switch to comfortable rows', 'التبديل إلى الصفوف المريحة'), tCompact: L('Switch to compact rows', 'التبديل إلى الصفوف المدمجة') });
  const GO = [
    ['go-home', 'dashboard', L('Home', 'الرئيسية'), 'G H', 'dashboard', L('dashboard today', 'لوحة اليوم')],
    ['go-contacts', 'users', L('Contacts', 'جهات الاتصال'), 'G C', 'contacts', L('customers vendors', 'العملاء الموردون')],
    ['go-invoices', 'invoice', L('Sales invoices', 'فواتير المبيعات'), 'G S', '', L('sales', 'المبيعات')],
    ['go-ageing', 'chart', L('Receivables ageing', 'أعمار الذمم المدينة'), 'G A', '', L('aging report overdue', 'تقرير متأخرة')],
    ['go-po', 'truck', L('Purchase orders', 'أوامر الشراء'), 'G P', '', L('purchasing', 'المشتريات')],
    ['go-stock', 'box', L('Stock levels', 'مستويات المخزون'), 'G I', '', L('inventory products', 'المخزون المنتجات')],
    ['go-vat', 'percent', L('VAT returns', 'الإقرارات الضريبية'), 'G V', '', L('tax fta', 'ضريبة الهيئة')],
    ['go-users', 'gear', L('Users and roles', 'المستخدمون والأدوار'), 'G ,', 'settings', L('settings permissions', 'الإعدادات الصلاحيات')],
  ];
  for (const [gid, icon, t, k, href, extra] of GO) add({ id: gid, g: 'go', type: 'nav', icon, t, k, href, toast: href ? '' : L(`${t} opened`, `فُتحت ${t}`), kw: kw(t, extra) });

  const palData = (ctx) => {
    const sign = ctx.screen === 'signin';
    const list = sign ? [
      ...['lang', 'theme', 'density'].map((k) => PI[k]),
      { id: 'forgot', g: 'act', type: 'action', icon: 'key', t: L('Reset my password', 'إعادة تعيين كلمة المرور'), s: L('A link goes to your work email', 'يصل رابط إلى بريد العمل'), toast: L('Reset link sent to layla@alwasl-trading.example', 'أُرسل رابط إعادة التعيين إلى layla@alwasl-trading.example'), kw: L('forgot password reset', 'نسيت كلمة المرور') },
      { id: 'passkey', g: 'act', type: 'action', icon: 'shield', t: L('Sign in with a passkey', 'الدخول بمفتاح المرور'), toast: L('Waiting for your passkey', 'بانتظار مفتاح المرور'), kw: 'passkey' },
      { id: 'sso', g: 'act', type: 'action', icon: 'building', t: L('Company single sign-on', 'الدخول الموحّد للشركة'), toast: L('Opening your company sign-on', 'جارٍ فتح الدخول الموحّد'), kw: 'sso single sign-on' },
      { id: 'status', g: 'go', type: 'nav', icon: 'globe', t: L('Service status', 'حالة الخدمة'), s: L('All systems normal', 'كل الأنظمة تعمل'), toast: L('All systems normal', 'كل الأنظمة تعمل'), kw: 'status' },
      { id: 'help', g: 'go', type: 'nav', icon: 'help', t: L('Help centre', 'مركز المساعدة'), toast: L('Help centre opened', 'فُتح مركز المساعدة'), kw: 'help support' },
    ] : items;
    const recActs = {
      contact: [
        ['open', 'edit', L('Open record', 'فتح السجل'), '↵'],
        ['statement', 'print', L('Send statement', 'إرسال كشف حساب'), 'S', L('Statement sent to {name}', 'أُرسل كشف الحساب إلى {name}')],
        ['invoice', 'invoice', L('New invoice', 'فاتورة جديدة'), 'N I', L('New invoice for {name}', 'فاتورة جديدة: {name}')],
        ['payment', 'coins', L('Record payment', 'تسجيل دفعة'), 'P', L('Payment form for {name}', 'نموذج دفعة: {name}')],
        ['email', 'mail', L('Email', 'إرسال بريد'), 'M', L('Email draft to {name}', 'مسودة بريد إلى {name}')],
        ['call', 'phone', L('Call', 'اتصال'), '', L('Calling {name}', 'جارٍ الاتصال بـ {name}')],
        ['copy', 'file', L('Copy TRN', 'نسخ الرقم الضريبي'), 'Ctrl C', L('TRN copied', 'نُسخ الرقم الضريبي')],
      ],
      invoice: [
        ['open', 'edit', L('Open invoice', 'فتح الفاتورة'), '↵', L('{name} opened', 'فُتحت {name}')],
        ['email', 'mail', L('Send by email', 'إرسال بالبريد'), 'M', L('{name} sent', 'أُرسلت {name}')],
        ['payment', 'coins', L('Record payment', 'تسجيل دفعة'), 'P', L('Payment form for {name}', 'نموذج دفعة: {name}')],
        ['print', 'print', L('Print in English and Arabic', 'طباعة بالعربية والإنجليزية'), 'Ctrl P', L('{name} ready to print', '{name} جاهزة للطباعة')],
        ['credit', 'file', L('Issue credit note', 'إصدار إشعار دائن'), '', L('Credit note started for {name}', 'بدأ إشعار دائن لـ {name}')],
      ],
      order: [
        ['open', 'edit', L('Open order', 'فتح الأمر'), '↵', L('{name} opened', 'فُتح {name}')],
        ['invoice', 'invoice', L('Create invoice', 'إنشاء فاتورة'), 'N I', L('Invoice started from {name}', 'بدأت فاتورة من {name}')],
        ['print', 'print', L('Print', 'طباعة'), 'Ctrl P', L('{name} ready to print', '{name} جاهز للطباعة')],
      ],
    };
    const suggest = sign
      ? [{ t: L('Before you sign in', 'قبل تسجيل الدخول'), ids: ['lang', 'theme', 'density', 'forgot', 'passkey', 'sso', 'status', 'help'] }]
      : [
        ...(ctx.record ? [{ t: L(`Actions on ${nameOf(ctx.record)}`, `إجراءات على ${nameOf(ctx.record)}`), ids: [`dyn:${ctx.record}`] }] : []),
        ...(ctx.screen === 'settings' ? [{ t: L('On this page', 'في هذه الصفحة'), ids: ['invite'] }] : []),
        ...(ctx.screen === 'home' ? [{ t: L('Needs you today', 'يحتاجك اليوم'), ids: ['reminders', 'approvals'] }] : []),
        { t: L('Recent', 'الأخيرة'), ids: ['C000003', 'INV-2026-01842', 'PO-2026-0418'] },
        { t: L('Go to', 'انتقال إلى'), ids: ['go-home', 'go-contacts', 'go-invoices', 'go-ageing'].filter((g) => !(ctx.screen === 'home' && g === 'go-home')) },
        { t: L('Commands', 'الأوامر'), ids: ['new-contact', 'theme', 'lang'] },
      ];
    const D = {
      lang, demo: ctx.demo || '', items: list, recActs, suggest,
      groups: { rec: L('Records', 'السجلات'), act: L('Actions', 'الإجراءات'), go: L('Go to', 'انتقال إلى') },
      dyn: { statement: L('Send statement to {name}', 'إرسال كشف حساب إلى {name}'), invoice: L('New invoice for {name}', 'فاتورة جديدة: {name}'), payment: L('Record payment from {name}', 'تسجيل دفعة من {name}'),
        statementToast: L('Statement sent to {name}', 'أُرسل كشف الحساب إلى {name}'), invoiceToast: L('New invoice for {name}', 'فاتورة جديدة: {name}'), paymentToast: L('Payment form for {name}', 'نموذج دفعة: {name}') },
      i18n: { none: L('No matches for “{q}”. Try a name, a TRN, an invoice number or a command.', 'لا نتائج لـ «{q}». جرّب اسمًا أو رقمًا ضريبيًا أو رقم فاتورة أو أمرًا.'), signNote: L('Sign in to search contacts, invoices and orders.', 'سجّل الدخول للبحث في جهات الاتصال والفواتير والأوامر.'),
        actionsOn: L('Actions on {name}', 'إجراءات على {name}'), ph: L('Type a name, a TRN, a number or a command…', 'اكتب اسمًا أو رقمًا ضريبيًا أو رقمًا أو أمرًا…'), phRec: L('Filter actions…', 'تصفية الإجراءات…'),
        scopeAll: L('Everything', 'الكل'), scopeCmd: L('Commands', 'الأوامر'), scopeDoc: L('Documents', 'المستندات'), sample: L('Sample only: nothing was sent or saved.', 'عينة فقط: لم يُرسل أو يُحفظ شيء.'), one: L('This sample has one full record: Al Wasl Cables Co.', 'تحتوي هذه العينة على سجل كامل واحد: شركة الوصل للكابلات.') },
      icons: {},
    };
    const used = new Set([...list.map((i) => i.icon), ...Object.values(recActs).flat().map((a) => a[1]), 'print', 'invoice', 'coins', 'check', 'sun', 'moon', 'rows', 'warn']);
    for (const n of used) D.icons[n] = ic(n).match(/ d="([^"]+)"/)[1];
    return JSON.stringify(D).replace(/</g, '\\u003c');
  };
  const palette = (ctx) => `<div class="palette-scrim" aria-hidden="true"></div><div class="palette" role="dialog" aria-modal="true" aria-label="${L('Command palette', 'لوحة الأوامر')}">
<div class="pal-in">${ic('search')}<span class="pal-ctx" hidden></span><input type="text" role="combobox" aria-expanded="true" aria-controls="pal-list" aria-autocomplete="list" autocomplete="off" spellcheck="false" aria-label="${L('Search or run a command', 'ابحث أو نفّذ أمرًا')}" placeholder="${L('Type a name, a TRN, a number or a command…', 'اكتب اسمًا أو رقمًا ضريبيًا أو رقمًا أو أمرًا…')}"><span class="pal-scope">${L('Everything', 'الكل')}</span>${kbd('Esc')}</div>
<div class="pal-body" id="pal-list" role="listbox" aria-label="${L('Results', 'النتائج')}"></div>
<div class="pal-foot"><span>${kbd('↑', '↓')}${L('move', 'تنقّل')}</span><span>${kbd('↵')}${L('run', 'تنفيذ')}</span><span>${kbd('Tab')}${L('actions on this record', 'إجراءات على هذا السجل')}</span><span class="pal-tip">${L(`Type ${kbd('&gt;')} for commands, ${kbd('#')} for documents`, `اكتب ${kbd('&gt;')} للأوامر و${kbd('#')} للمستندات`)}</span></div></div>
<div class="toast" role="status" aria-live="polite" hidden></div><script type="application/json" id="pal-data">${palData(ctx)}</script>`;

  const tail = `<script src="../shared/app.js"></script><script src="palette.js"></script></body></html>`;
  const page = (title, body, cls = '') => `${head2(lang, title, { theme: 'dark', density: 'compact' })}<body class="g ${cls}">${body}${tail}`;

  // ---------- frame: F's sidebar (recent records), D's top bar with the command field and D's status bar
  const NAV = [
    { id: 'home', ic: 'dashboard', t: L('Home', 'الرئيسية'), k: 'G H' },
    { id: 'contacts', ic: 'users', t: L('Contacts', 'جهات الاتصال'), items: [[L('All contacts', 'كل جهات الاتصال'), 'contacts'], [L('Customers', 'العملاء')], [L('Vendors', 'الموردون')]] },
    { id: 'sales', ic: 'cart', t: L('Sales', 'المبيعات'), n: '14', items: [[L('Quotations', 'عروض الأسعار')], [L('Sales orders', 'أوامر البيع')], [L('Invoices', 'الفواتير'), '', '14']] },
    { id: 'purch', ic: 'truck', t: L('Purchasing', 'المشتريات'), items: [[L('Purchase orders', 'أوامر الشراء')], [L('Vendor bills', 'فواتير الموردين')]] },
    { id: 'inv', ic: 'box', t: L('Inventory', 'المخزون'), items: [[L('Products', 'المنتجات')], [L('Stock levels', 'مستويات المخزون')]] },
    { id: 'fin', ic: 'bank', t: L('Finance', 'المالية'), items: [[L('Journal entries', 'قيود اليومية')], [L('Chart of accounts', 'دليل الحسابات')], [L('VAT returns', 'الإقرارات الضريبية')]] },
    { id: 'set', ic: 'gear', t: L('Settings', 'الإعدادات'), items: [[L('Users and roles', 'المستخدمون والأدوار'), 'settings'], [L('Company', 'الشركة')], [L('Taxes', 'الضرائب')], [L('Currencies', 'العملات')]] },
  ];
  const side = (active) => `<aside class="side" aria-label="${L('Main navigation', 'التنقل الرئيسي')}">
<a class="brand" href="#" aria-label="Bizora ERP">${lg.icon}<span class="brand-words">${ar ? `${lg.ar}${lg.en}` : `${lg.en}${lg.ar}`}</span></a>
<nav class="navlist">${NAV.map((n) => {
    if (!n.items) return `<a class="nav-top${active === n.id ? ' is-active' : ''}" href="#"${active === n.id ? ' aria-current="page"' : ''} ${T(`${n.t} (${n.k})`)}>${ic(n.ic)}<span>${n.t}</span><span class="nav-k">${kbd(...n.k.split(' '))}</span></a>`;
    const on = n.items.some((i) => i[1] === active);
    return `<details class="nav-group"${on ? ' open' : ''}><summary>${ic(n.ic)}<span>${n.t}</span>${n.n && !on ? `<span class="nav-n" ${T(L('14 overdue invoices', '14 فاتورة متأخرة'))}>${n.n}</span>` : ''}${ic('chevd', 'chev')}</summary><div class="nav-items">${n.items.map((i) => `<a class="nav-item${i[1] && i[1] === active ? ' is-active' : ''}" href="#"${i[1] && i[1] === active ? ' aria-current="page"' : ''}><span>${i[0]}</span>${i[2] ? `<span class="nav-n">${i[2]}</span>` : ''}</a>`).join('')}</div></details>`;
  }).join('')}</nav>
<div class="pinned"><p>${L('Pinned', 'المثبّتة')}</p>${[['invoice', L('Overdue invoices', 'الفواتير المتأخرة'), '14', 'danger'], ['check', L('Approvals waiting', 'موافقات بانتظارك'), '5'], ['box', L('Stock to reorder', 'مخزون يحتاج إعادة طلب'), '8']].map(([i, t, n, tone]) => `<a href="#" ${T(t)}>${ic(i)}<span class="trunc">${t}</span><span class="pin-n${tone ? ' ' + tone : ''}">${n}</span></a>`).join('')}</div>
<div class="recent"><p>${L('Recent', 'الأخيرة')}</p>${[['building', L('Al Wasl Cables Co.', 'شركة الوصل للكابلات'), ''], ['invoice', 'INV-2026-01842', 'mono'], ['truck', 'PO-2026-0418', 'mono']].map(([i, t, m]) => `<a href="#" ${T(t)}>${ic(i)}<bdi class="${m}">${t}</bdi></a>`).join('')}</div>
<button class="side-tip" data-palette-open>${ic('search')}<span><b>${L('Ctrl K, from anywhere', 'Ctrl K من أي مكان')}</b><small>${L('Find a contact, invoice or order, then press Tab to act on it.', 'ابحث عن جهة اتصال أو فاتورة أو أمر، ثم اضغط Tab لتنفيذ إجراء عليه.')}</small></span></button>
<div class="side-foot"><span class="avatar av-person">${L('LM', 'لم')}</span><div class="me"><b ${T(L('Layla Al Mazrouei · Owner', 'ليلى المزروعي · المالك'))}>${L('Layla Al Mazrouei', 'ليلى المزروعي')}</b><small>${c.TODAY.co}</small></div><button class="icon-btn" aria-label="${L('Sign out', 'تسجيل الخروج')}" ${T(L('Sign out', 'تسجيل الخروج'))}>${ic('logout')}</button></div></aside>`;
  const crumbs = (arr) => `<ol class="crumbs" aria-label="${L('You are here', 'أنت هنا')}">${arr.map((t, i) => `<li${i === arr.length - 1 ? ` aria-current="page" ${T(t)}` : ''}>${t}</li>`).join(`<li class="cs" aria-hidden="true">${ic(ar ? 'chevl' : 'chevr')}</li>`)}</ol>`;
  const cmdk = () => `<button class="cmdk" data-palette-open aria-haspopup="dialog" aria-label="${L('Jump to, search or run a command', 'انتقل أو ابحث أو نفّذ أمرًا')}" ${T(L('Jump to, search or run a command (Ctrl K)', 'انتقل أو ابحث أو نفّذ أمرًا (Ctrl K)'))}>${ic('search')}<span class="ph">${L('Jump to, search or run a command…', 'انتقل أو ابحث أو نفّذ أمرًا…')}</span>${kbd('Ctrl', 'K')}</button>`;
  const topbar = (cr) => `<header class="topbar">${crumbs(cr)}${cmdk()}<div class="top-end">${c.switches()}<button class="icon-btn" aria-label="${L('Notifications, 3 new', 'الإشعارات، 3 جديدة')}" ${T(L('Notifications', 'الإشعارات'))}>${ic('bell')}<i class="badge-dot" aria-hidden="true"></i></button><button class="icon-btn" aria-label="${L('Help', 'المساعدة')}" ${T(L('Help and shortcuts (?)', 'المساعدة والاختصارات (?)'))}>${ic('help')}</button></div></header>`;
  const status = (hints) => `<footer class="status"><span class="live"><i class="dot"></i>${L('Live · synced 14:32', 'مباشر · تمت المزامنة 14:32')}</span><span>${c.TODAY.co}</span><span>${L('FY 2026 · base currency AED', 'السنة المالية 2026 · العملة الأساسية AED')}</span>
<span class="hints">${hints.map(([k, t]) => `<span>${kbd(...k.split(' '))}${t}</span>`).join('')}</span></footer>`;
  const frame = (active, cr, main, hints, ctx, cls = '') => `${side(active)}<div class="app">${topbar(cr)}<main class="main ${cls}">${main}</main>${status(hints)}</div>${palette(ctx)}`;

  // ---------- shared F pieces
  const timeline = (o = {}) => `<ol class="tl${o.v ? ' tl-v' : ''}" aria-label="${L('Order status', 'حالة الطلب')}">${c.TIMELINE.map(([t, d, s]) => `<li class="tl-${s}"${s === 'now' ? ' aria-current="step"' : ''}><i class="tl-dot">${s === 'done' ? ic('check') : ''}</i><span class="tl-t">${t}</span><small><bdi dir="ltr" class="mono">${d.replace(/^(due|يستحق) /, '')}</bdi>${/^(due|يستحق)/.test(d) ? ` <span class="tl-due">${L('due', 'يستحق')}</span>` : ''}</small></li>`).join('')}</ol>`;
  const nba = (o = {}) => `<div class="nba${o.wide ? ' nba-wide' : ''}" role="note"><span class="nba-ic">${ic('warn')}</span><div class="nba-x"><b>${c.NBA.title} · ${money(1842000, 'AED')}</b><p>${c.NBA.body}</p></div><div class="nba-a">${btn('print', c.NBA.cta, { ghost: true, sm: true, k: c.NBA.key, cls: 'btn-tone' })}<button class="link">${c.NBA.alt}</button></div></div>`;

  // ---------- dashboard (F's content)
  const dashboard = () => {
    const act = (a, i) => `<article class="act tone-${a.tone}"><header><span class="act-ic">${ic(a.icon)}</span><div class="act-h"><h3 ${T(a.title)}>${a.title}</h3><p><b class="tnum">${num(a.count)}</b> ${a.unit}${a.total ? `<span class="act-total">${a.total}</span>` : ''}</p></div></header>
<ul class="act-list">${a.items.map(([t, r, m, v]) => `<li><div class="al-t"><b ${T(t)}>${t}</b><small ${T(r + ' · ' + m)}>${a.id === 'stock' ? m : `<bdi dir="ltr" class="mono">${r}</bdi> · ${m}`}</small></div><div class="al-v">${typeof v === 'number' ? (a.id === 'stock' ? `<span class="mbar-wrap warn" ${T(L('Stock left against the reorder point', 'المخزون المتبقي مقابل حد الطلب'))}><span class="mbar"><i style="width:${v * 100}%"></i></span><bdi dir="ltr" class="mbar-v tnum">${c.fmt(v * 100, 0)}%</bdi></span>` : `<span class="pct-over" ${T(L('Credit used', 'الائتمان المستخدم'))}><bdi dir="ltr" class="tnum">${c.fmt(v * 100, 0)}%</bdi></span>`) : v}</div></li>`).join('')}</ul>
<footer>${btn(null, a.cta, { primary: i === 0, ghost: i !== 0, sm: true, k: a.key })}<a href="#" class="more">${L('See all', 'عرض الكل')} ${num(a.count)}</a></footer></article>`;
    const kpi = (k) => `<div class="kpi"><div class="kpi-h"><span ${T(k.label)}>${k.label}</span><span class="delta${k.good === true ? ' good' : k.good === false ? ' bad' : ''}">${k.dir === 'up' ? '▲' : k.dir === 'down' ? '▼' : ''}<bdi dir="ltr">${k.delta}</bdi></span></div><div class="kpi-v">${k.value}</div><div class="kpi-c">${c.bars(k.series, { w: 200, h: 28, gap: 3, label: k.label })}</div><div class="kpi-s" ${T(k.sub)}>${k.sub}${k.subv ? ` <span class="kpi-sv">${k.subv}</span>` : ''}</div></div>`;
    const main = `<div class="dash">
<section class="dash-head"><div><p class="date">${c.TODAY.long}</p><h1>${c.TODAY.greet}</h1></div><p class="lede">${ic('check')}${L('4 things need you today. Sales, stock and cash are otherwise on track.', '4 أمور تحتاجك اليوم، وما عداها من مبيعات ومخزون ونقد يسير كما يجب.')}</p></section>
<div class="sec-t"><h2>${L('Needs you today', 'يحتاجك اليوم')}</h2><span class="sec-n">4</span><span class="sec-meta">${L('Sorted by urgency', 'مرتبة حسب الأولوية')}</span></div>
<section class="acts">${c.ACTIONS.map(act).join('')}</section>
<div class="sec-t"><h2>${L('Key figures', 'الأرقام الرئيسية')}</h2><span class="sec-meta">${L('Last 12 months · base currency AED', 'آخر 12 شهرًا · العملة الأساسية AED')}</span></div>
<section class="kpis">${c.KPIS.map(kpi).join('')}</section>
<div class="dash-low"><section class="panel feed"><div class="sec-t in"><h2>${L('Recent activity', 'آخر النشاطات')}</h2><a href="#" class="more">${L('Audit log', 'سجل التدقيق')}</a></div>
<ol class="feed-list">${c.ACTIVITY.map(([t, i, who, what, ref, obj, amt]) => `<li><time class="mono">${t}</time><span class="f-ic">${ic(i)}</span><p ${T(`${who} ${what} ${ref}${obj ? ' · ' + obj : ''}`)}><b>${who}</b> ${what} <bdi dir="ltr" class="${/^[A-Z]+-\d/.test(ref) ? 'mono ' : ''}ref">${ref}</bdi>${obj ? ` · <span class="obj">${obj}</span>` : ''}</p><span class="f-amt">${amt}</span></li>`).join('')}</ol></section>
<section class="panel empty"><span class="empty-ic">${ic(c.EMPTY.icon)}</span><p class="empty-k">${c.EMPTY.title}</p><h3>${c.EMPTY.head}</h3><p>${c.EMPTY.body}</p><div class="empty-a">${btn('plus', c.EMPTY.cta, { ghost: true, sm: true })}<a href="#">${c.EMPTY.alt}</a></div></section></div></div>`;
    return page(L('Home · Bizora ERP', 'الرئيسية · بيزورا'), frame('home', [c.TODAY.co, L('Home', 'الرئيسية')], main, [['Ctrl K', L('commands', 'الأوامر')], ['R', L('reminders', 'التذكيرات')], ['A', L('approvals', 'الموافقات')], ['?', L('all shortcuts', 'كل الاختصارات')]], { screen: 'home' }));
  };

  // ---------- contacts: saved views, grouped rows with sticky AED subtotals, details pane (F) in D's grid
  const contacts = () => {
    const list = c.DATA.slice(0, 30);
    const groups = c.groupRows(list);
    const head = [['c-name', L('Name', 'الاسم'), 1], ['c-city', L('City', 'المدينة')], ['c-trn', L('TRN', 'الرقم الضريبي')], ['c-cr', L('Credit', 'الائتمان')], ['c-od num', L('Overdue', 'المتأخر')], ['c-bal num', L('Balance', 'الرصيد')]];
    const row = (r) => { const x = X[r.ref]; const sel = r.ref === c.OPEN; return `<tr class="row${sel ? ' is-selected' : ''}" tabindex="0"${sel ? ' aria-selected="true"' : ''}><td class="c-sel"><input type="checkbox"${sel ? ' checked' : ''} aria-label="${L('Select', 'تحديد')} ${c.esc(c.ln(r))}"></td>
<td class="c-name"><div class="nm"><span class="avatar av-${r.kind}">${c.initials(r)}</span><b ${T(`${c.ln(r)} · ${r.ref}`)}>${c.ln(r)}</b>${r.tags.includes('VIP') ? `<span class="tag tag-vip">${L('VIP', 'مميّز')}</span>` : ''}</div></td><td class="c-city" ${T(c.city(r.city))}>${c.city(r.city)}</td><td class="c-trn mono">${r.trn ? num(r.trn) : `<span class="dim" ${T(L('No TRN: not registered for VAT', 'لا يوجد رقم ضريبي: غير مسجل في ضريبة القيمة المضافة'))}>—</span>`}</td>
<td class="c-cr">${c.mbar(x.credit)}</td><td class="c-od num">${x.overdue ? money(x.overdue, r.currency, { cls: 'od' }) : '<span class="dim">—</span>'}</td><td class="c-bal num">${money(r.balanceCents, r.currency)}</td></tr>`; };
    const grp = (g) => `<tbody class="grp-${g.s}"><tr class="grp"><td colspan="5"><button class="grp-t" data-group-toggle aria-expanded="true">${ic('chevd', 'chev')}<span class="pill pill-${g.s}"><i class="dot"></i>${stLabel[g.s]}</span><span class="grp-n">${num(g.rows.length)} · ${L('subtotals in AED, base', 'المجاميع بالدرهم، العملة الأساسية')}</span></button></td><td class="num" ${T(L('Overdue subtotal, converted to AED at today\'s rates', 'إجمالي المتأخر محوّلًا إلى AED بأسعار اليوم'))}>${g.od ? money(g.od, 'AED', { cls: 'od' }) : '<span class="dim">—</span>'}</td><td class="num" ${T(L('Balance subtotal, converted to AED at today\'s rates', 'إجمالي الرصيد محوّلًا إلى AED بأسعار اليوم'))}>${money(g.base, 'AED')}</td></tr>${g.rows.map(row).join('')}</tbody>`;
    const grid = `<div class="grid-wrap"><table class="grid" aria-label="${L('Contacts', 'جهات الاتصال')}"><thead><tr><th class="c-sel"><input type="checkbox" aria-label="${L('Select all', 'تحديد الكل')}"></th>${head.map(([k, h, s]) => `<th class="${k}" scope="col" ${T(h)}${s ? ' aria-sort="ascending"' : ''}><span class="th-in">${h}${s ? ic('chevu', 'th-sort') : ''}</span><i class="rz" aria-hidden="true"></i></th>`).join('')}</tr></thead>${groups.map(grp).join('')}</table></div>`;
    const x = X[open.ref];
    const pane = `<aside class="pane" aria-label="${L('Contact details', 'تفاصيل جهة الاتصال')}">
<header class="pane-h"><span class="avatar lg av-company">${c.initials(open)}</span><div class="pane-title"><h2 ${T(c.ln(open))}>${c.ln(open)}</h2><p>${L('Customer', 'عميل')} · ${c.city(open.city)} · <bdi dir="ltr" class="mono">${open.ref}</bdi></p></div><span class="pill pill-active"><i class="dot"></i>${L('Active', 'نشط')}</span><div class="pane-nav"><button class="icon-btn" aria-label="${L('Open full record', 'فتح السجل كاملًا')}" ${T(L('Open full record (Enter)', 'فتح السجل كاملًا (Enter)'))}>${ic('edit')}</button><button class="icon-btn" aria-label="${L('Close', 'إغلاق')}" ${T(L('Close (Esc)', 'إغلاق (Esc)'))}>${ic('close')}</button></div></header>
<div class="pane-body">${nba()}
<div class="tiles"><div class="tile"><span class="k">${L('Balance', 'الرصيد')}</span>${money(open.balanceCents, 'AED')}${c.spark(x.trend, { w: 104, h: 22, area: true, label: L('Balance, last 12 months', 'الرصيد خلال آخر 12 شهرًا') })}</div>
<div class="tile tile-danger"><span class="k">${L('Overdue', 'المتأخر')}</span>${money(x.overdue, 'AED')}<small ${T(L('3 invoices, the oldest 41 days late', '3 فواتير، أقدمها متأخرة 41 يومًا'))}>${L('3 invoices · 41 days', '3 فواتير · 41 يومًا')}</small></div>
<div class="tile"><span class="k">${L('Credit limit', 'حد الائتمان')}</span>${money(open.creditLimit * 100, 'AED')}${c.mbar(x.credit)}</div></div>
<h3 class="sub">${L('Latest order', 'آخر طلب')} <bdi dir="ltr" class="sub-ref mono">SO-2026-2187</bdi><span class="sub-end">${money(1862500, 'AED')}</span></h3>${timeline()}
<h3 class="sub">${L('Details', 'التفاصيل')}</h3>
<dl class="kv"><div><dt>${L('TRN', 'الرقم الضريبي')}</dt><dd class="mono">${num(open.trn)}<span class="ok-mark" ${T(L('15 digits, checked', '15 رقمًا، تم التحقق'))}>${ic('check')}</span></dd></div><div><dt>${L('Payment terms', 'شروط الدفع')}</dt><dd>${L('30 days from invoice', '30 يومًا من تاريخ الفاتورة')}</dd></div>
<div><dt>${L('Phone', 'الهاتف')}</dt><dd class="mono">${num(open.phone)}</dd></div><div><dt>${L('Email', 'البريد')}</dt><dd class="trunc" ${T(open.email)}>${num(open.email)}</dd></div>
<div class="wide"><dt>${L('Address', 'العنوان')}</dt><dd>${L('97 Road 20, Fujairah Free Zone, Fujairah', '97 شارع 20، المنطقة الحرة بالفجيرة، الفجيرة')}</dd></div>
<div><dt>${L('Trade licence', 'الرخصة التجارية')}</dt><dd><bdi dir="ltr" class="mono">${open.licence}</bdi> <span class="warn-t" ${T(L('Expires in 44 days', 'تنتهي بعد 44 يومًا'))}>${ic('warn')}<bdi dir="ltr" class="mono">31/12/2026</bdi></span></dd></div><div><dt>${L('Tags', 'الوسوم')}</dt><dd>${c.tagsHtml(open)}</dd></div></dl></div></aside>`;
    const main = `<div class="pagehead"><div class="ph-row"><h1>${L('Contacts', 'جهات الاتصال')}</h1><span class="count mono">${num('100,000')}</span><div class="ph-end">${btn('upload', L('Import', 'استيراد'), { ghost: true, sm: true })}${btn('download', L('Export', 'تصدير'), { ghost: true, sm: true })}${c.overflow(c.moreItems)}${btn('plus', L('New contact', 'جهة اتصال جديدة'), { primary: true, k: 'N C' })}</div></div>
<div class="views" role="tablist" aria-label="${L('Saved views', 'طرق العرض المحفوظة')}">${c.VIEWS.map(([t, n, on, tone], i) => `<button role="tab" aria-selected="${!!on}" ${T(`${t} (Alt+${i + 1})`)}>${t}<span class="vn${tone ? ' vn-' + tone : ''} mono">${num(n)}</span></button>`).join('')}<button class="view-add">${ic('plus')}${L('Save view', 'حفظ طريقة العرض')}</button></div></div>
<div class="toolbar"><label class="search">${ic('search')}<input data-search placeholder="${L('Search name, TRN, phone or email', 'ابحث بالاسم أو الرقم الضريبي أو الهاتف أو البريد')}" aria-label="${L('Search contacts', 'البحث في جهات الاتصال')}">${kbd('/')}</label>
<span class="chip on">${L('Customers', 'العملاء')}<button aria-label="${L('Remove filter', 'إزالة التصفية')}">${ic('close')}</button></span><span class="chip add">${ic('plus')}${L('Filter', 'تصفية')}${kbd('F')}</span>
<div class="tb-end"><button class="sel-btn">${ic('list')}${L('Group by', 'تجميع حسب')}: <b>${L('Status', 'الحالة')}</b>${ic('chevd')}</button><button class="sel-btn">${ic('sort')}${L('Sort', 'ترتيب')}: <b>${L('Name', 'الاسم')}</b>${ic('chevd')}</button><button class="sel-btn">${ic('columns')}${L('Columns', 'الأعمدة')}: <b class="mono">6</b>${ic('chevd')}</button></div></div>
<div class="split"><section class="listcol">${grid}<div class="gridfoot"><span>${L('1–30 of', '1–30 من')} <bdi dir="ltr" class="mono">100,000</bdi></span><span>${L('1 selected', 'محدد: 1')}</span><span class="gf-tot">${L('Total balance', 'إجمالي الرصيد')} ${money(482615025, 'AED')}</span><span class="dim gf-end">${L('Loaded in 38 ms', 'حُمّلت في 38 ملّي ثانية')}</span></div></section>${pane}</div>`;
    return page(L('Contacts · Bizora ERP', 'جهات الاتصال · بيزورا'), frame('contacts', [L('Contacts', 'جهات الاتصال'), L('All contacts', 'كل جهات الاتصال')], main, [['↑ ↓', L('move', 'تنقّل')], ['↵', L('open', 'فتح')], ['S', L('statement', 'كشف حساب')], ['X', L('select', 'تحديد')], ['Ctrl K', L('commands', 'الأوامر')]], { screen: 'contacts', record: c.OPEN, demo: L('al wa', 'الوصل') }, 'is-list'));
  };

  // ---------- contact form (F's content)
  const form = () => {
    const g = c.form2();
    // Both money fields (AED, and USD with its AED equivalent) in the first row, the one error in the second.
    const [fCredit, fTerms, fCur, fEmail, fVat, fAcct, fAvg, fSince] = g.accounting;
    g.accounting = [fCredit, fAvg, fTerms, fEmail, fVat, fAcct, fCur, fSince];
    const x = X[open.ref];
    const sec = (idd, t, ico, items) => `<section class="fsec" id="${idd}"><header><h2>${ic(ico)}${t}</h2></header><div class="fgrid">${items.join('')}</div></section>`;
    const main = `<div class="rechead-wrap"><div class="rechead"><span class="avatar xl av-company">${c.initials(open)}</span><div class="rh-t"><p class="rh-k">${L('Customer', 'عميل')} · <bdi dir="ltr" class="mono">${open.ref}</bdi> · ${c.city(open.city)}</p><h1 ${T(c.ln(open))}>${c.ln(open)}</h1></div><span class="pill pill-active"><i class="dot"></i>${L('Active', 'نشط')}</span>${c.tagsHtml(open)}
<div class="rh-end">${btn('print', L('Statement', 'كشف حساب'), { ghost: true, sm: true, k: 'S' })}${btn('invoice', L('New invoice', 'فاتورة جديدة'), { ghost: true, sm: true, k: 'N I' })}${c.overflow([['archive', L('Archive', 'أرشفة'), ''], ['merge', L('Merge duplicates', 'دمج المكرر'), ''], ['trash', L('Delete', 'حذف'), 'Del']])}</div></div>
<dl class="rh-facts"><div><dt>${L('Balance', 'الرصيد')}</dt><dd>${money(open.balanceCents, 'AED')}</dd></div><div><dt>${L('Overdue', 'المتأخر')}</dt><dd>${money(x.overdue, 'AED', { cls: 'od' })}</dd></div><div><dt>${L('Credit limit', 'حد الائتمان')}</dt><dd>${money(open.creditLimit * 100, 'AED')}</dd></div><div><dt>${L('Sales this year', 'مبيعات هذا العام')}</dt><dd>${money(x.salesYtd, 'AED')}</dd></div><div><dt>${L('Last order', 'آخر طلب')}</dt><dd class="mono"><bdi dir="ltr">14/11/2026</bdi></dd></div></dl>
<div class="ftabs" role="tablist">${[[L('General', 'عام'), 1], [L('Addresses', 'العناوين'), 0, 2], [L('Accounting', 'المحاسبة')], [L('People', 'الأشخاص'), 0, 2], [L('Documents', 'المستندات'), 0, 3], [L('History', 'السجل')]].map(([t, on, n], i) => `<button role="tab" aria-selected="${!!on}" ${T(`${t} (Alt+${i + 1})`)}>${t}${n ? ` <span class="cnt">${n}</span>` : ''}</button>`).join('')}<span class="ftabs-k">${kbd('Alt', '1–6')}</span></div></div>
<div class="formbody"><div class="fcol">${nba({ wide: true })}
${sec('s-id', L('Identity', 'الهوية'), 'building', g.identity)}${sec('s-ac', L('Accounting and credit', 'المحاسبة والائتمان'), 'coins', g.accounting)}${sec('s-ct', L('Contact and address', 'التواصل والعنوان'), 'pin', g.contact)}</div>
<aside class="faside"><section class="panel"><h3>${L('Latest order', 'آخر طلب')} <bdi dir="ltr" class="sub-ref mono">SO-2026-2187</bdi></h3>${timeline({ v: true })}</section>
<section class="panel"><h3>${L('Credit', 'الائتمان')}</h3><div class="cr-big">${money(open.balanceCents, 'AED')}<small>${L('of', 'من')} ${money(open.creditLimit * 100, 'AED')}</small></div>${c.mbar(x.credit)}${c.spark(x.trend, { w: 256, h: 40, area: true, cls: 'cr-spark' })}<p class="note">${L('Balance, last 12 months', 'الرصيد خلال آخر 12 شهرًا')}</p></section>
<section class="panel"><h3>${L('Record', 'السجل')}</h3><dl class="audit"><div><dt>${L('Created', 'أُنشئ')}</dt><dd><bdi dir="ltr" class="mono">14/03/2022</bdi> · ${L('Omar Al Suwaidi', 'عمر السويدي')}</dd></div><div><dt>${L('Last changed', 'آخر تغيير')}</dt><dd><bdi dir="ltr" class="mono">17/11/2026 14:32</bdi> · ${L('Layla Al Mazrouei', 'ليلى المزروعي')}</dd></div><div><dt>${L('History', 'السجل')}</dt><dd><a href="#">${L('23 changes in the audit trail', '23 تغييرًا في سجل التدقيق')}</a></dd></div></dl></section></aside>
<div class="savebar" role="region" aria-label="${L('Save changes', 'حفظ التغييرات')}"><span class="sb-state"><i class="chg-dot"></i><b>${L('2 unsaved changes', 'تغييران غير محفوظين')}</b><span class="dim" ${T(L('Credit limit, Accounts email', 'حد الائتمان، بريد الحسابات'))}>${L('Credit limit, Accounts email', 'حد الائتمان، بريد الحسابات')}</span></span><span class="sb-err">${ic('warn')}${L('1 field to fix', 'حقل واحد للتصحيح')}</span><span class="sb-end">${btn(null, L('Discard', 'تجاهل'), { ghost: true, k: 'Esc' })}${btn('save', L('Save', 'حفظ'), { primary: true, k: 'Ctrl S' })}</span></div></div>`;
    return page(c.ln(open) + ' · Bizora ERP', frame('contacts', [L('Contacts', 'جهات الاتصال'), L('All contacts', 'كل جهات الاتصال'), c.ln(open)], main, [['Ctrl S', L('save', 'حفظ')], ['Esc', L('discard', 'تجاهل')], ['Alt 1–6', L('tabs', 'التبويبات')], ['S', L('statement', 'كشف حساب')], ['Ctrl K', L('commands', 'الأوامر')]], { screen: 'form', record: c.OPEN }, 'is-form'));
  };

  // ---------- settings: users and roles (F's content)
  const settings = () => {
    const P = c.PERMS;
    const main = `<div class="pagehead"><div class="ph-row"><h1>${L('Users and roles', 'المستخدمون والأدوار')}</h1><span class="count"><bdi dir="ltr" class="mono">12 / 25</bdi> ${L('seats used', 'مقعدًا مستخدمًا')}</span><div class="ph-end">${btn('download', L('Export', 'تصدير'), { ghost: true, sm: true })}${btn('plus', L('Invite user', 'دعوة مستخدم'), { primary: true, k: 'I' })}</div></div>
<div class="views" role="tablist">${[[L('Users', 'المستخدمون'), '12', 1], [L('Roles and permissions', 'الأدوار والصلاحيات'), '8'], [L('Sign-in policy', 'سياسة الدخول')], [L('API keys', 'مفاتيح الواجهة'), '2'], [L('Audit log', 'سجل التدقيق')]].map(([t, n, on]) => `<button role="tab" aria-selected="${!!on}">${t}${n ? `<span class="vn mono">${n}</span>` : ''}</button>`).join('')}</div></div>
<div class="toolbar"><label class="search">${ic('search')}<input data-search placeholder="${L('Find a user by name, email or role', 'ابحث عن مستخدم بالاسم أو البريد أو الدور')}" aria-label="${L('Find a user', 'البحث عن مستخدم')}">${kbd('/')}</label><span class="chip on">${L('All users', 'كل المستخدمين')}</span><span class="chip">${L('Two-step off', 'التحقق غير مفعّل')} <b class="mono">5</b></span><span class="chip">${L('Invited', 'مدعوون')} <b class="mono">1</b></span><span class="chip">${L('Locked', 'مقفلون')} <b class="mono">1</b></span></div>
<div class="split"><div class="setcol">${c.usersGrid()}
<div class="below"><section class="panel"><div class="sec-t in"><h2>${L('Access changes this month', 'تغييرات الصلاحيات هذا الشهر')}</h2><a href="#" class="more">${L('Audit log', 'سجل التدقيق')}</a></div><ol class="feed-list acc">${c.ACCESS.map(([t, i, w, m]) => `<li><time class="mono"><bdi dir="ltr">${t}</bdi></time><span class="f-ic">${ic(i)}</span><p ${T(w + ' · ' + m)}><b>${w}</b> · ${m}</p></li>`).join('')}</ol></section>
<section class="panel"><div class="sec-t in"><h2>${L('Sign-in policy', 'سياسة الدخول')}</h2><a href="#" class="more">${L('Change', 'تغيير')}</a></div><dl class="policy">${c.POLICY.map(([k, v]) => `<div><dt>${k}</dt><dd>${ic('check')}${v}</dd></div>`).join('')}</dl></section></div></div>
<aside class="pane rolepane" aria-label="${L('Roles', 'الأدوار')}"><div class="rp-h"><h2>${L('Roles', 'الأدوار')}</h2>${btn('plus', L('New role', 'دور جديد'), { ghost: true, sm: true })}</div><ul class="rolelist">${c.rolesList()}</ul>
<section class="role-open"><header><span class="role-ic2">${ic('shield')}</span><div><h3>${L('Accountant', 'محاسب')}</h3><p>${L('2 members · changed 02/11/2026', 'عضوان · عُدّل في 02/11/2026')}</p></div>${btn('edit', L('Edit', 'تعديل'), { ghost: true, sm: true, k: 'E' })}</header>
<table class="perm"><thead><tr><th scope="col">${L('Area', 'المجال')}</th>${P.cols.map((h) => `<th scope="col">${h}</th>`).join('')}</tr></thead><tbody>${P.modules.map((m, i) => `<tr><th scope="row">${m}</th>${P.accountant[i].map((v) => `<td><span class="pm ${v ? 'on' : 'off'}" role="img" aria-label="${v ? L('Allowed', 'مسموح') : L('Not allowed', 'غير مسموح')}">${v ? ic('check') : ''}</span></td>`).join('')}</tr>`).join('')}</tbody></table>
<p class="rp-note">${ic('info')}<span>${L('Approvals above 50,000.00 AED also need the Owner.', `الاعتمادات التي تتجاوز ${iso('50,000.00 AED')} تحتاج أيضًا إلى المالك.`)}</span></p>
<div class="members"><h4>${L('Members', 'الأعضاء')}</h4><ul>${[[L('OS', 'عس'), L('Omar Al Suwaidi', 'عمر السويدي'), L('signed in 17/11/2026', 'دخل في 17/11/2026')], [L('FK', 'فك'), L('Fatima Al Kaabi', 'فاطمة الكعبي'), L('signed in 14/11/2026', 'دخلت في 14/11/2026')]].map(([i, n, d]) => `<li><span class="avatar av-person">${i}</span><b>${n}</b><small>${d.replace(/(\d\d\/\d\d\/\d{4})/, '<bdi dir="ltr" class="mono">$1</bdi>')}</small></li>`).join('')}</ul></div></section></aside></div>`;
    return page(L('Users and roles · Bizora ERP', 'المستخدمون والأدوار · بيزورا'), frame('settings', [L('Settings', 'الإعدادات'), L('Users and roles', 'المستخدمون والأدوار')], main, [['↑ ↓', L('move', 'تنقّل')], ['I', L('invite', 'دعوة')], ['/', L('find', 'بحث')], ['Ctrl K', L('commands', 'الأوامر')]], { screen: 'settings' }, 'is-settings'));
  };

  // ---------- sign-in (F's content, D's ground and status bar)
  const signin = () => {
    const body = `<div class="signin"><header class="si-top">${cmdk().replace(L('Jump to, search or run a command…', 'انتقل أو ابحث أو نفّذ أمرًا…'), L('Language, theme or help…', 'اللغة أو المظهر أو المساعدة…'))}<div class="top-end">${c.switches()}</div></header>
<main class="si-main"><form class="si-card" onsubmit="return false"><div class="si-logos">${lg.en}${lg.ar}</div><h1>${L('Welcome back', 'أهلًا بعودتك')}</h1><p class="si-sub">${L('Sign in with your work email to open your company.', 'سجّل الدخول ببريد العمل لفتح حساب شركتك.')}</p>
${c.F.text('s-co', L('Company code', 'رمز الشركة'), 'alwasl-trading', { ltr: true })}${c.F.text('s-em', L('Work email', 'بريد العمل'), 'layla@alwasl-trading.example', { ltr: true })}
<div class="fld"><label for="s-pw">${L('Password', 'كلمة المرور')}</label><div class="ctl"><input id="s-pw" type="password" dir="ltr" value="correct-horse-1"><button class="in-btn" type="button" aria-label="${L('Show password', 'إظهار كلمة المرور')}" ${T(L('Show password', 'إظهار كلمة المرور'))}>${ic('eye')}</button></div><div class="hint warn">${ic('warn')}${L('Caps Lock is on', 'مفتاح الأحرف الكبيرة مفعّل')}</div></div>
<div class="row-between"><label class="chk"><input type="checkbox" checked> ${L('Keep me signed in on this device', 'أبقِني مسجّلًا على هذا الجهاز')}</label><a href="#">${L('Forgot password?', 'نسيت كلمة المرور؟')}</a></div>
<button class="btn btn-primary btn-block">${L('Sign in', 'تسجيل الدخول')}${kbd('↵')}</button>
<div class="or"><span>${L('or', 'أو')}</span></div><div class="alt2">${btn('key', L('Use a passkey', 'استخدام مفتاح المرور'), { ghost: true })}${btn('building', L('Company single sign-on', 'الدخول الموحّد للشركة'), { ghost: true })}</div></form>
<ul class="si-points"><li>${ic('shield')}${L('Two-step verification', 'التحقق بخطوتين')}</li><li>${ic('globe')}${L('Data kept in the UAE', 'البيانات محفوظة في الإمارات')}</li><li>${ic('book')}${L('English and Arabic, on screen and in print', 'العربية والإنجليزية على الشاشة وفي الطباعة')}</li></ul></main>
<footer class="si-foot"><span class="live"><i class="dot"></i>${L('All systems normal', 'كل الأنظمة تعمل')}</span><span>Bizora ERP</span><a href="#">${L('Privacy', 'الخصوصية')}</a><a href="#">${L('Service status', 'حالة الخدمة')}</a><a href="#">${L('Help', 'المساعدة')}</a><span class="hints"><span>${kbd('Tab')}${L('next field', 'الحقل التالي')}</span><span>${kbd('↵')}${L('sign in', 'دخول')}</span><span>${kbd('Ctrl', 'K')}${L('commands', 'الأوامر')}</span></span></footer></div>${palette({ screen: 'signin' })}`;
    return page(L('Sign in · Bizora ERP', 'تسجيل الدخول · بيزورا'), body, 'is-signin');
  };

  return { dashboard: dashboard(), contacts: contacts(), form: form(), settings: settings(), signin: signin() };
}
