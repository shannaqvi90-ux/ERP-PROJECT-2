// Direction F layout: "Best of all". Light sidebar with collapsible groups (B), dense calm grids (A),
// Naskh titles and Kufi interface in Arabic (C), plus saved-view tabs, grouped rows with sticky
// subtotals, an inline status timeline and a next-best-action hint on the record.
import { mk2, head2, tail2, logos } from './lib2.mjs';
export const id = 'f';
export function pages(lang) {
  const c = mk2(lang);
  const { L, ic, num, money, ar, T, X, open } = c;
  c.NBA.body = c.NBA.body.replace('AED 18,420.00', '18,420.00 AED');
  const lg = logos();
  const kbd = (...ks) => `<span class="keys">${ks.map((k) => `<kbd>${k}</kbd>`).join('')}</span>`;
  const page = (title, body, cls = '') => `${head2(lang, title, { theme: 'light', density: 'compact' })}<body class="f ${cls}">${body}${tail2}`;
  const btn = (icon, label, o = {}) => `<button class="btn${o.primary ? ' btn-primary' : ''}${o.quiet ? ' btn-quiet' : ''}${o.soft ? ' btn-soft' : ''}${o.sm ? ' btn-sm' : ''}"${o.k ? ` ${T(`${label} (${o.k})`)}` : ''}>${icon ? ic(icon) : ''}<span>${label}</span>${o.k && o.showK ? kbd(...o.k.split(' ')) : ''}</button>`;
  const stLabel = { active: L('Active', 'نشط'), hold: L('On hold', 'موقوف مؤقتًا'), blocked: L('Credit stop', 'إيقاف ائتماني') };

  // ---------- sidebar (B's lightness, A's structure)
  const NAV = [
    { id: 'home', ic: 'dashboard', t: L('Home', 'الرئيسية') },
    { id: 'contacts', ic: 'users', t: L('Contacts', 'جهات الاتصال'), items: [[L('All contacts', 'كل جهات الاتصال'), 'contacts'], [L('Customers', 'العملاء')], [L('Vendors', 'الموردون')]] },
    { id: 'sales', ic: 'cart', t: L('Sales', 'المبيعات'), n: '14', items: [[L('Quotations', 'عروض الأسعار')], [L('Sales orders', 'أوامر البيع')], [L('Invoices', 'الفواتير'), '', '14']] },
    { id: 'purch', ic: 'truck', t: L('Purchasing', 'المشتريات'), items: [[L('Purchase orders', 'أوامر الشراء')], [L('Vendor bills', 'فواتير الموردين')]] },
    { id: 'inv', ic: 'box', t: L('Inventory', 'المخزون'), items: [[L('Products', 'المنتجات')], [L('Stock levels', 'مستويات المخزون')]] },
    { id: 'fin', ic: 'bank', t: L('Finance', 'المالية'), items: [[L('Journal entries', 'قيود اليومية')], [L('Chart of accounts', 'دليل الحسابات')], [L('VAT returns', 'الإقرارات الضريبية')]] },
    { id: 'set', ic: 'gear', t: L('Settings', 'الإعدادات'), items: [[L('Users and roles', 'المستخدمون والأدوار'), 'settings'], [L('Company', 'الشركة')], [L('Taxes', 'الضرائب')], [L('Currencies', 'العملات')]] },
  ];
  const side = (active) => `<aside class="side" aria-label="${L('Main navigation', 'التنقل الرئيسي')}">
<a class="brand" href="#" aria-label="Bizora ERP">${lg.icon}<span class="brand-words">${ar ? `${lg.ar}${lg.en}` : `${lg.en}${lg.ar}`}</span></a>
<button class="jump" data-palette-open aria-label="${L('Jump to', 'انتقال إلى')}">${ic('search')}<span>${L('Jump to…', 'انتقال إلى…')}</span>${kbd('Ctrl', 'K')}</button>
<nav class="navlist">${NAV.map((n) => {
    if (!n.items) return `<a class="nav-top${active === n.id ? ' is-active' : ''}" href="#"${active === n.id ? ' aria-current="page"' : ''}>${ic(n.ic)}<span>${n.t}</span><span class="nav-k">${kbd('G', 'H')}</span></a>`;
    const on = n.items.some((i) => i[1] === active);
    return `<details class="nav-group"${on ? ' open' : ''}><summary>${ic(n.ic)}<span>${n.t}</span>${n.n && !on ? `<span class="nav-n">${n.n}</span>` : ''}${ic('chevd', 'chev')}</summary><div class="nav-items">${n.items.map((i) => `<a class="nav-item${i[1] && i[1] === active ? ' is-active' : ''}" href="#"${i[1] && i[1] === active ? ' aria-current="page"' : ''}><span>${i[0]}</span>${i[2] ? `<span class="nav-n">${i[2]}</span>` : ''}</a>`).join('')}</div></details>`;
  }).join('')}</nav>
<div class="recent"><p>${L('Recent', 'الأخيرة')}</p>${[['building', L('Al Wasl Cables Co.', 'شركة الوصل للكابلات')], ['invoice', 'INV-2026-01842'], ['truck', 'PO-2026-0418']].map(([i, t]) => `<a href="#" ${T(t)}>${ic(i)}<bdi>${t}</bdi></a>`).join('')}</div>
<div class="side-foot"><span class="avatar av-person">${L('LM', 'لم')}</span><div class="me"><b>${L('Layla Al Mazrouei', 'ليلى المزروعي')}</b><small>${c.TODAY.co}</small></div><button class="icon-btn" aria-label="${L('Sign out', 'تسجيل الخروج')}" ${T(L('Sign out', 'تسجيل الخروج'))}>${ic('logout')}</button></div></aside>`;
  const crumbs = (arr) => `<ol class="crumbs" aria-label="${L('You are here', 'أنت هنا')}">${arr.map((t, i) => `<li${i === arr.length - 1 ? ' aria-current="page"' : ''}>${t}</li>`).join(`<li class="cs" aria-hidden="true">${ic(ar ? 'chevl' : 'chevr')}</li>`)}</ol>`;
  const topbar = (cr) => `<header class="topbar">${crumbs(cr)}<div class="top-end">${c.switches()}<button class="icon-btn" aria-label="${L('Notifications, 3 new', 'الإشعارات، 3 جديدة')}" ${T(L('Notifications', 'الإشعارات'))}>${ic('bell')}<i class="badge-dot" aria-hidden="true"></i></button><button class="icon-btn" aria-label="${L('Help', 'المساعدة')}" ${T(L('Help and shortcuts (?)', 'المساعدة والاختصارات (?)'))}>${ic('help')}</button></div></header>`;
  const frame = (active, cr, main, cls = '') => `${side(active)}<div class="app">${topbar(cr)}<main class="main ${cls}">${main}</main></div>`;

  // ---------- dashboard
  const dashboard = () => {
    const act = (a, i) => `<article class="act tone-${a.tone}"><header><span class="act-ic">${ic(a.icon)}</span><div class="act-h"><h3 ${T(a.title)}>${a.title}</h3><p><b class="tnum">${num(a.count)}</b> ${a.unit}${a.total ? `<span class="act-total">${a.total}</span>` : ''}</p></div></header>
<ul class="act-list">${a.items.map(([t, r, m, v]) => `<li><div class="al-t"><b ${T(t)}>${t}</b><small ${T(r + ' · ' + m)}>${a.id === 'stock' ? m : `<bdi dir="ltr">${r}</bdi> · ${m}`}</small></div><div class="al-v">${typeof v === 'number' ? (a.id === 'stock' ? `<span class="mbar-wrap warn" ${T(L('Stock left against the reorder point', 'المخزون المتبقي مقابل حد الطلب'))}><span class="mbar"><i style="width:${v * 100}%"></i></span><bdi dir="ltr" class="mbar-v tnum">${c.fmt(v * 100, 0)}%</bdi></span>` : `<span class="pct-over" ${T(L('Credit used', 'الائتمان المستخدم'))}><bdi dir="ltr" class="tnum">${c.fmt(v * 100, 0)}%</bdi></span>`) : v}</div></li>`).join('')}</ul>
<footer>${btn(null, a.cta, { primary: i === 0, soft: i !== 0, sm: true, k: a.key, showK: true })}<a href="#" class="more">${L('See all', 'عرض الكل')} ${num(a.count)}</a></footer></article>`;
    const kpi = (k) => `<div class="kpi"><div class="kpi-h"><span ${T(k.label)}>${k.label}</span><span class="delta${k.good === true ? ' good' : k.good === false ? ' bad' : ''}">${k.dir === 'up' ? '↑' : k.dir === 'down' ? '↓' : ''}<bdi dir="ltr">${k.delta}</bdi></span></div><div class="kpi-v">${k.value}</div><div class="kpi-c">${c.bars(k.series, { w: 200, h: 30, gap: 3, label: k.label })}</div><div class="kpi-s">${k.sub}${k.subv ? ` <span class="kpi-sv">${k.subv}</span>` : ''}</div></div>`;
    const main = `<div class="dash">
<section class="dash-head"><div><p class="date">${c.TODAY.long}</p><h1>${c.TODAY.greet}</h1></div><p class="lede">${ic('check')}${L('4 things need you today. Sales, stock and cash are otherwise on track.', '4 أمور تحتاجك اليوم، وما عداها من مبيعات ومخزون ونقد يسير كما يجب.')}</p></section>
<div class="sec-t"><h2>${L('Needs you today', 'يحتاجك اليوم')}</h2><span class="sec-n">4</span><span class="sec-meta">${L('Sorted by urgency', 'مرتبة حسب الأولوية')}</span></div>
<section class="acts">${c.ACTIONS.map(act).join('')}</section>
<div class="sec-t"><h2>${L('Key figures', 'الأرقام الرئيسية')}</h2><span class="sec-meta">${L('Last 12 months · base currency AED', 'آخر 12 شهرًا · العملة الأساسية AED')}</span></div>
<section class="kpis">${c.KPIS.map(kpi).join('')}</section>
<div class="dash-low"><section class="card feed"><div class="sec-t in"><h2>${L('Recent activity', 'آخر النشاطات')}</h2><a href="#" class="more">${L('Audit log', 'سجل التدقيق')}</a></div>
<ol class="feed-list">${c.ACTIVITY.map(([t, i, who, what, ref, obj, amt]) => `<li><time class="tnum">${t}</time><span class="f-ic">${ic(i)}</span><p ${T(`${who} ${what} ${ref}${obj ? ' · ' + obj : ''}`)}><b>${who}</b> ${what} <bdi dir="ltr" class="ref">${ref}</bdi>${obj ? ` · ${obj}` : ''}</p><span class="f-amt">${amt}</span></li>`).join('')}</ol></section>
<section class="card empty"><span class="empty-ic">${ic(c.EMPTY.icon)}</span><p class="empty-k">${c.EMPTY.title}</p><h3>${c.EMPTY.head}</h3><p>${c.EMPTY.body}</p><div class="empty-a">${btn('plus', c.EMPTY.cta, { soft: true, sm: true })}<a href="#">${c.EMPTY.alt}</a></div></section></div></div>`;
    return page(L('Home · Bizora ERP', 'الرئيسية · بيزورا'), frame('home', [c.TODAY.co, L('Home', 'الرئيسية')], main));
  };

  // ---------- contacts: saved views, grouped rows with sticky subtotals, details pane
  const contacts = () => {
    const list = c.DATA.slice(0, 30);
    const groups = c.groupRows(list);
    const head = [['c-name', L('Name', 'الاسم'), 1], ['c-city', L('City', 'المدينة')], ['c-trn', L('TRN', 'الرقم الضريبي')], ['c-cr', L('Credit used', 'الائتمان')], ['c-od num', L('Overdue', 'المتأخر')], ['c-bal num', L('Balance', 'الرصيد')]];
    const row = (r) => { const x = X[r.ref]; const sel = r.ref === c.OPEN; return `<tr class="row${sel ? ' is-selected' : ''}" tabindex="0"${sel ? ' aria-selected="true"' : ''}><td class="c-sel"><input type="checkbox"${sel ? ' checked' : ''} aria-label="${L('Select', 'تحديد')} ${c.esc(c.ln(r))}"></td>
<td class="c-name"><div class="nm"><span class="avatar av-${r.kind}">${c.initials(r)}</span><b ${T(`${c.ln(r)} · ${r.ref}`)}>${c.ln(r)}</b>${r.tags.includes('VIP') ? `<span class="tag tag-vip">${L('VIP', 'مميّز')}</span>` : ''}</div></td><td class="c-city" ${T(c.city(r.city))}>${c.city(r.city)}</td><td class="c-trn tnum">${r.trn ? num(r.trn) : `<span class="dim" ${T(L('No TRN: not registered for VAT', 'لا يوجد رقم ضريبي: غير مسجل في ضريبة القيمة المضافة'))}>—</span>`}</td>
<td class="c-cr">${c.mbar(x.credit)}</td><td class="c-od num">${x.overdue ? money(x.overdue, r.currency, { cls: 'od' }) : '<span class="dim">—</span>'}</td><td class="c-bal num">${money(r.balanceCents, r.currency)}</td></tr>`; };
    const grp = (g) => `<tbody class="grp-${g.s}"><tr class="grp"><td colspan="5"><button class="grp-t" data-group-toggle aria-expanded="true">${ic('chevd', 'chev')}<span class="pill pill-${g.s}"><i class="dot"></i>${stLabel[g.s]}</span><span class="grp-n">${num(g.rows.length)} · ${L('subtotals in AED, base', 'المجاميع بالدرهم، العملة الأساسية')}</span></button></td><td class="num" ${T(L('Overdue subtotal, converted to AED at today\'s rates', 'إجمالي المتأخر محوّلًا إلى AED بأسعار اليوم'))}>${g.od ? money(g.od, 'AED') : '<span class="dim">—</span>'}</td><td class="num" ${T(L('Balance subtotal, converted to AED at today\'s rates', 'إجمالي الرصيد محوّلًا إلى AED بأسعار اليوم'))}>${money(g.base, 'AED')}</td></tr>${g.rows.map(row).join('')}</tbody>`;
    const grid = `<div class="grid-wrap"><table class="grid" aria-label="${L('Contacts', 'جهات الاتصال')}"><thead><tr><th class="c-sel"><input type="checkbox" aria-label="${L('Select all', 'تحديد الكل')}"></th>${head.map(([k, h, s]) => `<th class="${k}" scope="col" ${T(h)}${s ? ' aria-sort="ascending"' : ''}><span class="th-in">${h}${s ? ic('chevu', 'th-sort') : ''}</span><i class="rz" aria-hidden="true"></i></th>`).join('')}</tr></thead>${groups.map(grp).join('')}</table></div>`;
    const x = X[open.ref];
    const timeline = (o = {}) => `<ol class="tl${o.v ? ' tl-v' : ''}" aria-label="${L('Order status', 'حالة الطلب')}">${c.TIMELINE.map(([t, d, s]) => `<li class="tl-${s}"><i class="tl-dot">${s === 'done' ? ic('check') : ''}</i><span class="tl-t">${t}</span><small><bdi dir="ltr">${d.replace(/^(due|يستحق) /, '')}</bdi>${/^(due|يستحق)/.test(d) ? ` <span class="tl-due">${L('due', 'يستحق')}</span>` : ''}</small></li>`).join('')}</ol>`;
    const nba = (o = {}) => `<div class="nba${o.wide ? ' nba-wide' : ''}" role="note"><span class="nba-ic">${ic('warn')}</span><div class="nba-x"><b>${c.NBA.title} · ${money(1842000, 'AED')}</b><p>${c.NBA.body}</p></div><div class="nba-a">${btn('print', c.NBA.cta, { soft: true, sm: true, k: c.NBA.key, showK: true })}<button class="link">${c.NBA.alt}</button></div></div>`;
    const pane = `<aside class="pane" aria-label="${L('Contact details', 'تفاصيل جهة الاتصال')}">
<header class="pane-h"><span class="avatar lg av-company">${c.initials(open)}</span><div class="pane-title"><h2 ${T(c.ln(open))}>${c.ln(open)}</h2><p>${L('Customer', 'عميل')} · ${c.city(open.city)} · <bdi dir="ltr">${open.ref}</bdi></p></div><button class="icon-btn" aria-label="${L('Open full record', 'فتح السجل كاملًا')}" ${T(L('Open full record (Enter)', 'فتح السجل كاملًا (Enter)'))}>${ic('edit')}</button><button class="icon-btn" aria-label="${L('Close', 'إغلاق')}" ${T(L('Close (Esc)', 'إغلاق (Esc)'))}>${ic('close')}</button></header>
<div class="pane-body">${nba()}
<div class="figs"><div><span class="k">${L('Balance', 'الرصيد')}</span>${money(open.balanceCents, 'AED')}</div><div><span class="k">${L('Overdue', 'المتأخر')}</span>${money(x.overdue, 'AED', { cls: 'od' })}</div><div><span class="k">${L('Credit limit', 'حد الائتمان')}</span>${money(open.creditLimit * 100, 'AED')}</div></div>
<div class="credit">${c.mbar(x.credit)}<span class="dim">${L('used of the limit', 'من الحد مستخدم')}</span></div>
<h3 class="sub">${L('Latest order', 'آخر طلب')} <bdi dir="ltr" class="sub-ref">SO-2026-2187</bdi><span class="sub-end">${money(1862500, 'AED')}</span></h3>${timeline()}
<h3 class="sub">${L('Details', 'التفاصيل')}</h3>
<dl class="kv"><div><dt>${L('TRN', 'الرقم الضريبي')}</dt><dd class="tnum">${num(open.trn)}<span class="ok-mark" ${T(L('15 digits, checked', '15 رقمًا، تم التحقق'))}>${ic('check')}</span></dd></div><div><dt>${L('Payment terms', 'شروط الدفع')}</dt><dd>${L('30 days from invoice', '30 يومًا من تاريخ الفاتورة')}</dd></div>
<div><dt>${L('Phone', 'الهاتف')}</dt><dd class="tnum">${num(open.phone)}</dd></div><div><dt>${L('Email', 'البريد')}</dt><dd class="trunc" ${T(open.email)}>${num(open.email)}</dd></div>
<div class="wide"><dt>${L('Address', 'العنوان')}</dt><dd>${L('97 Road 20, Fujairah Free Zone, Fujairah', '97 شارع 20، المنطقة الحرة بالفجيرة، الفجيرة')}</dd></div>
<div><dt>${L('Trade licence', 'الرخصة التجارية')}</dt><dd><bdi dir="ltr">${open.licence}</bdi> <span class="warn-t" ${T(L('Expires in 44 days', 'تنتهي بعد 44 يومًا'))}>${ic('warn')}<bdi dir="ltr">31/12/2026</bdi></span></dd></div><div><dt>${L('Tags', 'الوسوم')}</dt><dd>${c.tagsHtml(open)}</dd></div></dl></div></aside>`;
    const main = `<div class="pagehead"><div class="ph-row"><h1>${L('Contacts', 'جهات الاتصال')}</h1><span class="count tnum">${num('100,000')}</span><div class="ph-end">${btn('upload', L('Import', 'استيراد'), { quiet: true, sm: true })}${btn('download', L('Export', 'تصدير'), { quiet: true, sm: true })}${c.overflow(c.moreItems)}${btn('plus', L('New contact', 'جهة اتصال جديدة'), { primary: true, k: 'Alt+N' })}</div></div>
<div class="views" role="tablist" aria-label="${L('Saved views', 'طرق العرض المحفوظة')}">${c.VIEWS.map(([t, n, on, tone]) => `<button role="tab" aria-selected="${!!on}">${t}<span class="vn${tone ? ' vn-' + tone : ''} tnum">${num(n)}</span></button>`).join('')}<button class="view-add">${ic('plus')}${L('Save view', 'حفظ طريقة العرض')}</button></div></div>
<div class="toolbar"><label class="search">${ic('search')}<input data-search placeholder="${L('Search name, TRN, phone or email', 'ابحث بالاسم أو الرقم الضريبي أو الهاتف أو البريد')}" aria-label="${L('Search contacts', 'البحث في جهات الاتصال')}">${kbd('/')}</label>
<span class="chip on">${L('Customers', 'العملاء')}<button aria-label="${L('Remove filter', 'إزالة التصفية')}">${ic('close')}</button></span><span class="chip">${ic('plus')}${L('Filter', 'تصفية')}</span>
<div class="tb-end"><button class="sel-btn">${ic('list')}${L('Group by', 'تجميع حسب')}: <b>${L('Status', 'الحالة')}</b>${ic('chevd')}</button><button class="sel-btn">${ic('sort')}${L('Sort', 'ترتيب')}: <b>${L('Name', 'الاسم')}</b>${ic('chevd')}</button>${btn('columns', L('Columns', 'الأعمدة'), { quiet: true, sm: true })}</div></div>
<div class="split"><section class="listcol">${grid}<div class="gridfoot"><span>${L('1–30 of', '1–30 من')} ${num('100,000')}</span><span>${L('1 selected', 'محدد: 1')}</span><span class="gf-tot">${L('Total balance', 'إجمالي الرصيد')} ${money(482615025, 'AED')}</span><span class="gf-k">${kbd('↑', '↓')}${L('move', 'تنقّل')} ${kbd('Enter')}${L('open', 'فتح')}</span></div></section>${pane}</div>`;
    return page(L('Contacts · Bizora ERP', 'جهات الاتصال · بيزورا'), frame('contacts', [L('Contacts', 'جهات الاتصال'), L('All contacts', 'كل جهات الاتصال')], main, 'is-list'));
  };

  // ---------- contact form
  const form = () => {
    const g = c.form2();
    const x = X[open.ref];
    const sec = (idd, t, ico, items, o = {}) => `<section class="fsec" id="${idd}"><header><h2>${ic(ico)}${t}</h2>${o.extra || ''}</header><div class="fgrid">${items.join('')}</div></section>`;
    const main = `<div class="rechead"><span class="avatar xl av-company">${c.initials(open)}</span><div class="rh-t"><h1 ${T(c.ln(open))}>${c.ln(open)}</h1><p>${L('Customer', 'عميل')} · <bdi dir="ltr">${open.ref}</bdi> · ${c.city(open.city)}</p></div><span class="pill pill-active"><i class="dot"></i>${L('Active', 'نشط')}</span>${c.tagsHtml(open)}
<div class="rh-end">${btn('print', L('Statement', 'كشف حساب'), { quiet: true, sm: true })}${btn('invoice', L('New invoice', 'فاتورة جديدة'), { quiet: true, sm: true })}${c.overflow([['archive', L('Archive', 'أرشفة'), ''], ['merge', L('Merge duplicates', 'دمج المكرر'), ''], ['trash', L('Delete', 'حذف'), 'Del']])}</div></div>
<dl class="rh-facts"><div><dt>${L('Balance', 'الرصيد')}</dt><dd>${money(open.balanceCents, 'AED')}</dd></div><div><dt>${L('Overdue', 'المتأخر')}</dt><dd>${money(x.overdue, 'AED', { cls: 'od' })}</dd></div><div><dt>${L('Credit limit', 'حد الائتمان')}</dt><dd>${money(open.creditLimit * 100, 'AED')}</dd></div><div><dt>${L('Sales this year', 'مبيعات هذا العام')}</dt><dd>${money(x.salesYtd, 'AED')}</dd></div><div><dt>${L('Last order', 'آخر طلب')}</dt><dd class="tnum"><bdi dir="ltr">14/11/2026</bdi></dd></div></dl>
<div class="ftabs" role="tablist">${[[L('General', 'عام'), 1], [L('Addresses', 'العناوين'), 0, 2], [L('Accounting', 'المحاسبة')], [L('People', 'الأشخاص'), 0, 2], [L('Documents', 'المستندات'), 0, 3], [L('History', 'السجل')]].map(([t, on, n], i) => `<button role="tab" aria-selected="${!!on}" ${T(`${t} (Alt+${i + 1})`)}>${t}${n ? ` <span class="cnt">${n}</span>` : ''}</button>`).join('')}</div>
<div class="formbody"><div class="fcol">${nbaBanner()}
${sec('s-id', L('Identity', 'الهوية'), 'building', g.identity)}${sec('s-ac', L('Accounting and credit', 'المحاسبة والائتمان'), 'coins', g.accounting)}${sec('s-ct', L('Contact and address', 'التواصل والعنوان'), 'pin', g.contact)}</div>
<aside class="faside"><section class="card"><h3>${L('Latest order', 'آخر طلب')} <bdi dir="ltr" class="sub-ref">SO-2026-2187</bdi></h3>${timelineV()}</section>
<section class="card"><h3>${L('Credit', 'الائتمان')}</h3><div class="cr-big">${money(open.balanceCents, 'AED')}<small>${L('of', 'من')} ${money(open.creditLimit * 100, 'AED')}</small></div>${c.mbar(x.credit)}${c.spark(x.trend, { w: 256, h: 40, area: true, cls: 'cr-spark' })}<p class="note">${L('Balance, last 12 months', 'الرصيد خلال آخر 12 شهرًا')}</p></section>
<section class="card"><h3>${L('Record', 'السجل')}</h3><dl class="audit"><div><dt>${L('Created', 'أُنشئ')}</dt><dd><bdi dir="ltr">14/03/2022</bdi> · ${L('Omar Al Suwaidi', 'عمر السويدي')}</dd></div><div><dt>${L('Last changed', 'آخر تغيير')}</dt><dd><bdi dir="ltr">17/11/2026 14:32</bdi> · ${L('Layla Al Mazrouei', 'ليلى المزروعي')}</dd></div><div><dt>${L('History', 'السجل')}</dt><dd><a href="#">${L('23 changes in the audit trail', '23 تغييرًا في سجل التدقيق')}</a></dd></div></dl></section></aside>
<div class="savebar" role="region" aria-label="${L('Save changes', 'حفظ التغييرات')}"><span class="sb-state"><i class="chg-dot"></i><b>${L('2 unsaved changes', 'تغييران غير محفوظين')}</b><span class="dim" ${T(L('Credit limit, Accounts email', 'حد الائتمان، بريد الحسابات'))}>${L('Credit limit, Accounts email', 'حد الائتمان، بريد الحسابات')}</span></span><span class="sb-err">${ic('warn')}${L('1 field to fix', 'حقل واحد للتصحيح')}</span><span class="sb-end">${btn(null, L('Discard', 'تجاهل'), { quiet: true, k: 'Esc' })}${btn('save', L('Save', 'حفظ'), { primary: true, k: 'Ctrl+S', showK: false })}</span></div></div>`;
    return page(c.ln(open) + ' · Bizora ERP', frame('contacts', [L('Contacts', 'جهات الاتصال'), L('All contacts', 'كل جهات الاتصال'), c.ln(open)], main, 'is-form'));
  };
  const nbaBanner = () => `<div class="nba nba-wide" role="note"><span class="nba-ic">${ic('warn')}</span><div class="nba-x"><b>${c.NBA.title} · ${money(1842000, 'AED')}</b><p>${c.NBA.body}</p></div><div class="nba-a">${btn('print', c.NBA.cta, { soft: true, sm: true, k: c.NBA.key, showK: true })}<button class="link">${c.NBA.alt}</button></div></div>`;
  const timelineV = () => `<ol class="tl tl-v" aria-label="${L('Order status', 'حالة الطلب')}">${c.TIMELINE.map(([t, d, s]) => `<li class="tl-${s}"><i class="tl-dot">${s === 'done' ? ic('check') : ''}</i><span class="tl-t">${t}</span><small><bdi dir="ltr">${d.replace(/^(due|يستحق) /, '')}</bdi>${/^(due|يستحق)/.test(d) ? ` <span class="tl-due">${L('due', 'يستحق')}</span>` : ''}</small></li>`).join('')}</ol>`;

  // ---------- settings: users and roles
  const settings = () => {
    const P = c.PERMS;
    const main = `<div class="pagehead"><div class="ph-row"><h1>${L('Users and roles', 'المستخدمون والأدوار')}</h1><span class="count tnum">${num('12 / 25')} ${L('seats used', 'مقعدًا مستخدمًا')}</span><div class="ph-end">${btn('download', L('Export', 'تصدير'), { quiet: true, sm: true })}${btn('plus', L('Invite user', 'دعوة مستخدم'), { primary: true, k: 'Alt+N' })}</div></div>
<div class="views" role="tablist">${[[L('Users', 'المستخدمون'), '12', 1], [L('Roles and permissions', 'الأدوار والصلاحيات'), '8'], [L('Sign-in policy', 'سياسة الدخول')], [L('API keys', 'مفاتيح الواجهة'), '2'], [L('Audit log', 'سجل التدقيق')]].map(([t, n, on]) => `<button role="tab" aria-selected="${!!on}">${t}${n ? `<span class="vn tnum">${n}</span>` : ''}</button>`).join('')}</div></div>
<div class="toolbar"><label class="search">${ic('search')}<input data-search placeholder="${L('Find a user by name, email or role', 'ابحث عن مستخدم بالاسم أو البريد أو الدور')}" aria-label="${L('Find a user', 'البحث عن مستخدم')}">${kbd('/')}</label><span class="chip on">${L('All users', 'كل المستخدمين')}</span><span class="chip">${L('Two-step off', 'التحقق غير مفعّل')} <b class="tnum">5</b></span><span class="chip">${L('Invited', 'مدعوون')} <b class="tnum">1</b></span><span class="chip">${L('Locked', 'مقفلون')} <b class="tnum">1</b></span></div>
<div class="split"><div class="setcol">${c.usersGrid()}
<div class="below"><section class="card"><div class="sec-t in"><h2>${L('Access changes this month', 'تغييرات الصلاحيات هذا الشهر')}</h2><a href="#" class="more">${L('Audit log', 'سجل التدقيق')}</a></div><ol class="feed-list acc">${c.ACCESS.map(([t, i, w, m]) => `<li><time class="tnum"><bdi dir="ltr">${t}</bdi></time><span class="f-ic">${ic(i)}</span><p ${T(w + ' · ' + m)}><b>${w}</b> · ${m}</p></li>`).join('')}</ol></section>
<section class="card"><div class="sec-t in"><h2>${L('Sign-in policy', 'سياسة الدخول')}</h2><a href="#" class="more">${L('Change', 'تغيير')}</a></div><dl class="policy">${c.POLICY.map(([k, v]) => `<div><dt>${k}</dt><dd>${ic('check')}${v}</dd></div>`).join('')}</dl></section></div></div>
<aside class="pane rolepane" aria-label="${L('Roles', 'الأدوار')}"><div class="rp-h"><h2>${L('Roles', 'الأدوار')}</h2>${btn('plus', L('New role', 'دور جديد'), { quiet: true, sm: true })}</div><ul class="rolelist">${c.rolesList()}</ul>
<section class="role-open"><header><span class="role-ic2">${ic('shield')}</span><div><h3>${L('Accountant', 'محاسب')}</h3><p>${L('2 members · changed 02/11/2026', 'عضوان · عُدّل في 02/11/2026')}</p></div>${btn('edit', L('Edit', 'تعديل'), { quiet: true, sm: true })}</header>
<table class="perm"><thead><tr><th scope="col">${L('Area', 'المجال')}</th>${P.cols.map((h) => `<th scope="col">${h}</th>`).join('')}</tr></thead><tbody>${P.modules.map((m, i) => `<tr><th scope="row">${m}</th>${P.accountant[i].map((v) => `<td><span class="pm ${v ? 'on' : 'off'}" role="img" aria-label="${v ? L('Allowed', 'مسموح') : L('Not allowed', 'غير مسموح')}">${v ? ic('check') : ''}</span></td>`).join('')}</tr>`).join('')}</tbody></table>
<p class="rp-note">${ic('info')}<span>${L('Approvals above 50,000.00 AED also need the Owner.', 'الاعتمادات التي تتجاوز 50,000.00 AED تحتاج أيضًا إلى المالك.')}</span></p>
<div class="members"><h4>${L('Members', 'الأعضاء')}</h4><ul>${[[L('OS', 'عس'), L('Omar Al Suwaidi', 'عمر السويدي'), L('signed in 17/11/2026', 'دخل في 17/11/2026')], [L('FK', 'فك'), L('Fatima Al Kaabi', 'فاطمة الكعبي'), L('signed in 14/11/2026', 'دخلت في 14/11/2026')]].map(([i, n, d]) => `<li><span class="avatar av-person">${i}</span><b>${n}</b><small>${d}</small></li>`).join('')}</ul></div></section></aside></div>`;
    return page(L('Users and roles · Bizora ERP', 'المستخدمون والأدوار · بيزورا'), frame('settings', [L('Settings', 'الإعدادات'), L('Users and roles', 'المستخدمون والأدوار')], main, 'is-settings'));
  };

  // ---------- sign-in
  const signin = () => {
    const body = `<div class="signin"><header class="si-top"><span></span><div class="top-end">${c.switches()}</div></header>
<main class="si-main"><form class="si-card" onsubmit="return false"><div class="si-logos">${lg.en}${lg.ar}</div><h1>${L('Welcome back', 'أهلًا بعودتك')}</h1><p class="si-sub">${L('Sign in with your work email to open your company.', 'سجّل الدخول ببريد العمل لفتح حساب شركتك.')}</p>
${c.F.text('s-co', L('Company code', 'رمز الشركة'), 'alwasl-trading', { ltr: true })}${c.F.text('s-em', L('Work email', 'بريد العمل'), 'layla@alwasl-trading.example', { ltr: true })}
<div class="fld"><label for="s-pw">${L('Password', 'كلمة المرور')}</label><div class="ctl"><input id="s-pw" type="password" dir="ltr" value="correct-horse-1"><button class="in-btn" type="button" aria-label="${L('Show password', 'إظهار كلمة المرور')}" ${T(L('Show password', 'إظهار كلمة المرور'))}>${ic('eye')}</button></div><div class="hint warn">${ic('warn')}${L('Caps Lock is on', 'مفتاح الأحرف الكبيرة مفعّل')}</div></div>
<div class="row-between"><label class="chk"><input type="checkbox" checked> ${L('Keep me signed in on this device', 'أبقِني مسجّلًا على هذا الجهاز')}</label><a href="#">${L('Forgot password?', 'نسيت كلمة المرور؟')}</a></div>
<button class="btn btn-primary btn-block">${L('Sign in', 'تسجيل الدخول')}</button>
<div class="or"><span>${L('or', 'أو')}</span></div><div class="alt2">${btn('key', L('Use a passkey', 'استخدام مفتاح المرور'), { quiet: true })}${btn('building', L('Company single sign-on', 'الدخول الموحّد للشركة'), { quiet: true })}</div></form>
<ul class="si-points"><li>${ic('shield')}${L('Two-step verification', 'التحقق بخطوتين')}</li><li>${ic('globe')}${L('Data kept in the UAE', 'البيانات محفوظة في الإمارات')}</li><li>${ic('book')}${L('English and Arabic, on screen and in print', 'العربية والإنجليزية على الشاشة وفي الطباعة')}</li></ul></main>
<footer class="si-foot"><span>Bizora ERP</span><a href="#">${L('Privacy', 'الخصوصية')}</a><a href="#">${L('Service status', 'حالة الخدمة')}</a><a href="#">${L('Help', 'المساعدة')}</a></footer></div>`;
    return page(L('Sign in · Bizora ERP', 'تسجيل الدخول · بيزورا'), body, 'is-signin');
  };

  return { dashboard: dashboard(), contacts: contacts(), form: form(), settings: settings(), signin: signin() };
}
