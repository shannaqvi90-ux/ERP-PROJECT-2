// Direction E layout: "Financial clarity". A masthead with a section bar (no rail), a dateline,
// heavy section rules, serif hero figures, grids with inline micro charts, statement-style panes.
import { mk2, head2, tail2, logos } from './lib2.mjs';
export const id = 'e';

const USERS = [
  ['Layla Al Mazrouei', 'ليلى المزروعي', 'layla@alwasl-trading.example', ['Owner'], 'active', '17/11/2026', true],
  ['Omar Al Suwaidi', 'عمر السويدي', 'omar.s@alwasl-trading.example', ['Accountant'], 'active', '17/11/2026', true],
  ['Hind Thomas', 'هند توماس', 'hind.t@alwasl-trading.example', ['Sales manager'], 'active', '16/11/2026', true],
  ['Karim Fernandes', 'كريم فرنانديز', 'karim.f@alwasl-trading.example', ['Sales'], 'active', '16/11/2026', false],
  ['Mansour Al Romaithi', 'منصور الرميثي', 'mansour.r@alwasl-trading.example', ['Warehouse'], 'active', '15/11/2026', false],
  ['Fatima Al Kaabi', 'فاطمة الكعبي', 'fatima.k@alwasl-trading.example', ['Accountant', 'Auditor'], 'active', '14/11/2026', true],
  ['Rashid Al Nuaimi', 'راشد النعيمي', 'rashid.n@alwasl-trading.example', ['Purchasing'], 'hold', '02/11/2026', false],
  ['Sara Mathew', 'سارة ماثيو', 'sara.m@alwasl-trading.example', ['Sales'], 'active', '17/11/2026', true],
  ['Yousef Al Hammadi', 'يوسف الحمادي', 'yousef.h@alwasl-trading.example', ['Warehouse'], 'invited', '—', false],
  ['Noura Al Falasi', 'نورة الفلاسي', 'noura.f@alwasl-trading.example', ['Payroll'], 'active', '13/11/2026', true],
  ['Tariq Hussain', 'طارق حسين', 'tariq.h@alwasl-trading.example', ['Purchasing'], 'active', '12/11/2026', false],
  ['Aisha Al Shamsi', 'عائشة الشامسي', 'aisha.s@alwasl-trading.example', ['Auditor'], 'locked', '29/10/2026', true],
];
const ROLE_AR = { Owner: 'المالك', Accountant: 'محاسب', 'Sales manager': 'مدير مبيعات', Sales: 'مبيعات', Warehouse: 'مستودع', Auditor: 'مدقق', Purchasing: 'مشتريات', Payroll: 'رواتب' };
const ROLES = [['Owner', 1, 'All areas, approvals above AED 50,000.00', 'كل المجالات، والاعتمادات فوق 50,000.00 AED'], ['Accountant', 2, 'Finance, sales and purchase posting', 'المالية وترحيل المبيعات والمشتريات'], ['Sales manager', 1, 'Sales, credit requests, price lists', 'المبيعات وطلبات الائتمان وقوائم الأسعار'], ['Sales', 2, 'Quotations, orders, contacts', 'عروض الأسعار والطلبات وجهات الاتصال'], ['Warehouse', 2, 'Stock moves, receipts, deliveries', 'حركات المخزون والاستلام والتسليم'], ['Purchasing', 2, 'Purchase orders, vendor bills', 'أوامر الشراء وفواتير الموردين'], ['Auditor', 2, 'Read only, audit log', 'قراءة فقط وسجل التدقيق'], ['Payroll', 1, 'Employees and payroll runs', 'الموظفون ودورات الرواتب']];

export function pages(lang) {
  const c = mk2(lang);
  const { L, ic, num, money, ar, T, X, open } = c;
  const lg = logos();
  const page = (title, body, cls = '') => `${head2(lang, title, { theme: 'light', density: 'comfortable' })}<body class="e ${cls}">${body}${tail2}`;
  const brand = () => `<a class="brand" href="#" aria-label="Bizora ERP">${ar ? `${lg.ar}<i class="sep"></i>${lg.en}` : `${lg.en}<i class="sep"></i>${lg.ar}`}</a>`;
  const btn = (icon, label, o = {}) => `<button class="btn${o.primary ? ' btn-primary' : ''}${o.quiet ? ' btn-quiet' : ''}${o.sm ? ' btn-sm' : ''}"${o.k ? ` ${T(`${label} (${o.k})`)}` : ''}>${icon ? ic(icon) : ''}<span>${label}</span>${o.k && o.showk ? `<kbd>${o.k}</kbd>` : ''}</button>`;
  const link = (t) => `<a href="#" class="more">${t}<span class="arr" aria-hidden="true">${ar ? '←' : '→'}</span></a>`;
  const SECTIONS = [['home', L('Today', 'اليوم')], ['contacts', L('Contacts', 'جهات الاتصال')], ['sales', L('Sales', 'المبيعات')], ['purch', L('Purchasing', 'المشتريات')], ['inv', L('Inventory', 'المخزون')], ['fin', L('Finance', 'المالية')], ['rep', L('Reports', 'التقارير')], ['set', L('Settings', 'الإعدادات')]];
  const masthead = (active) => `<header class="mast"><div class="mast-1">${brand()}<p class="dateline"><span>${c.TODAY.long}</span><span class="dl-sep" aria-hidden="true">·</span><span>${c.TODAY.co}</span></p>
<label class="search">${ic('search')}<input data-search placeholder="${L('Search contacts, documents, TRN', 'ابحث في جهات الاتصال والمستندات والأرقام الضريبية')}" aria-label="${L('Search', 'بحث')}"><kbd>/</kbd></label>
${c.switches()}<span class="avatar me" ${T(L('Layla Al Mazrouei · Owner', 'ليلى المزروعي · المالك'))}>${L('LM', 'لم')}</span></div>
<nav class="sections" aria-label="${L('Main navigation', 'التنقل الرئيسي')}">${SECTIONS.map(([k, t]) => `<a href="#" class="${k === active ? 'is-active' : ''}"${k === active ? ' aria-current="page"' : ''}>${t}${k === 'home' ? '<i class="ndot" aria-hidden="true"></i>' : ''}</a>`).join('')}<span class="sec-meta">${L('Base currency AED · FY 2026', 'العملة الأساسية AED · السنة المالية 2026')}</span></nav></header>`;
  const sh = (t, extra = '') => `<h2 class="rule-h"><span>${t}</span>${extra}</h2>`;
  const stLabel = (s) => { const m = { active: L('Active', 'نشط'), hold: L('On hold', 'موقوف مؤقتًا'), blocked: L('Credit stop', 'إيقاف ائتماني'), suspended: L('Suspended', 'موقوف'), invited: L('Invited', 'مدعو'), locked: L('Locked', 'مقفل') }; return `<span class="stl stl-${s}"><i></i>${m[s]}</span>`; };
  const stShort = (s) => { const t = { hold: [L('Hold', 'معلّق'), L('On hold', 'موقوف مؤقتًا')], blocked: [L('Stop', 'إيقاف'), L('Credit stop', 'إيقاف ائتماني')] }[s]; return `<span class="stl short stl-${s}" ${T(t[1])}><i></i>${t[0]}</span>`; };
  const delta = (k) => `<span class="delta ${k.dir}${k.good === true ? ' good' : k.good === false ? ' bad' : ''}">${k.dir === 'up' ? '▲' : k.dir === 'down' ? '▼' : ''}<bdi dir="ltr">${k.delta}</bdi></span>`;

  // ---------- dashboard: the morning briefing
  const dashboard = () => {
    const item = (a, i) => `<article class="brief">
<header><span class="bn">${i + 1}</span><div class="bt"><h3>${a.title}</h3><p class="bfig"><b class="fig-s">${num(a.count)}</b> <span>${a.unit}</span>${a.total ? `<span class="btot">${a.total}</span>` : ''}</p></div></header>
<ol class="ledger">${a.items.map(([t, r, m, v]) => `<li><div class="lt"><b ${T(t)}>${t}</b><small ${T(r + ' · ' + m)}><bdi dir="ltr">${r}</bdi> · ${m}</small></div><div class="lv">${typeof v === 'number' ? (a.id === 'stock' ? `<span class="mbar-wrap warn" ${T(L('Stock left against reorder point', 'المخزون المتبقي مقابل حد الطلب'))}><span class="mbar"><i style="width:${v * 100}%"></i></span><bdi dir="ltr" class="mbar-v">${c.fmt(v * 100, 0)}%</bdi></span>` : `<bdi dir="ltr" class="over tnum">${c.fmt(v * 100, 0)}%</bdi>`) : v}</div></li>`).join('')}</ol>
<footer>${i === 0 ? btn(null, a.cta, { primary: true, sm: true, k: a.key }) : link(a.cta)}<span class="ball">${L('All', 'الكل')} ${num(a.count)}</span></footer></article>`;
    const kpi = (k) => `<div class="kpi"><p class="kl">${k.label}</p><div class="kv">${k.value}</div><div class="kd">${delta(k)}<span class="ks" ${T(k.sub)}>${k.sub}${k.subv ? ` ${k.subv}` : ''}</span></div>${c.spark(k.series, { w: 220, h: 30, cls: 'kspark', label: k.label })}</div>`;
    const main = `<div class="page dash"><div class="titlerow"><div><p class="kicker">${L('Daily briefing · as at 14:32', 'الموجز اليومي · حتى الساعة 14:32')}</p><h1>${L('Four things need you today.', 'أربعة أمور تحتاجك اليوم.')}</h1></div><p class="standfirst">${L(`Everything else is on track. Receivables are up 3.1% this month and sales are 8.4% ahead of October.`, `وكل ما عداها يسير كما يجب. ارتفعت الذمم المدينة ${num('3.1%')} هذا الشهر، والمبيعات أعلى من أكتوبر بنسبة ${num('8.4%')}.`)}</p></div>
${sh(L('What needs doing', 'ما يجب إنجازه'), `<span class="rh-meta">${L('Sorted by money at risk', 'مرتبة حسب المبالغ المعرّضة للخطر')}</span>`)}
<section class="briefs">${c.ACTIONS.map(item).join('')}</section>
${sh(L('Key figures', 'الأرقام الرئيسية'), `<span class="rh-meta">${L('12 months · base currency AED', '12 شهرًا · العملة الأساسية AED')}</span>`)}
<section class="kpis">${c.KPIS.map(kpi).join('')}</section>
<div class="lower"><section>${sh(L('Today’s ledger', 'دفتر اليوم'), link(L('Audit log', 'سجل التدقيق')))}
<table class="ltable"><thead><tr><th scope="col">${L('Time', 'الوقت')}</th><th scope="col">${L('Document', 'المستند')}</th><th scope="col">${L('Entry', 'القيد')}</th><th scope="col" class="num">${L('Amount', 'المبلغ')}</th></tr></thead><tbody>${c.ACTIVITY.map(([t, i, who, what, ref, obj, amt]) => `<tr><td class="tnum dim">${t}</td><td><bdi dir="ltr" class="doc">${ref}</bdi></td><td class="ent" ${T(`${who} ${what} ${obj}`)}><b>${who}</b> ${what}${obj ? ` · ${obj}` : ''}</td><td class="num">${amt || '<span class="dim">—</span>'}</td></tr>`).join('')}</tbody></table></section>
<section class="empty"><p class="kicker">${c.EMPTY.title}</p><h3>${c.EMPTY.head}</h3><p>${c.EMPTY.body}</p><div class="empty-a">${btn('plus', c.EMPTY.cta, { sm: true })}${link(c.EMPTY.alt)}</div></section></div></div>`;
    return page(L('Today · Bizora ERP', 'اليوم · بيزورا'), `${masthead('home')}<main class="main">${main}</main>`);
  };

  // ---------- contacts: list with a statement-of-account pane
  const contacts = () => {
    const rows = c.DATA.slice(0, 30);
    const totBase = rows.reduce((s, r) => s + c.toBase(r.balanceCents, r.currency), 0);
    const totOd = rows.reduce((s, r) => s + c.toBase(X[r.ref].overdue, r.currency), 0);
    const head = [['c-name', L('Name', 'الاسم'), 1], ['c-city', L('City', 'المدينة')], ['c-trn', L('TRN', 'الرقم الضريبي')], ['c-cr', L('Credit used', 'الائتمان')], ['c-ag', L('Ageing', 'الأعمار')], ['c-tr', L('Trend', 'الاتجاه')], ['c-bal num', L('Balance', 'الرصيد')]];
    const grid = `<div class="grid-wrap"><table class="grid" aria-label="${L('Contacts', 'جهات الاتصال')}"><thead><tr><th class="c-sel"><input type="checkbox" aria-label="${L('Select all', 'تحديد الكل')}"></th>${head.map(([k, h, s]) => `<th class="${k}" scope="col" ${T(h)}${s ? ' aria-sort="ascending"' : ''}><span class="th-in">${h}${s ? ic('chevu', 'th-sort') : ''}</span><i class="rz" aria-hidden="true"></i></th>`).join('')}</tr></thead>
<tbody>${rows.map((r) => { const x = X[r.ref]; const sel = r.ref === c.OPEN; return `<tr class="row${sel ? ' is-selected' : ''}" tabindex="0"${sel ? ' aria-selected="true"' : ''}><td class="c-sel"><input type="checkbox"${sel ? ' checked' : ''} aria-label="${L('Select', 'تحديد')} ${c.esc(c.ln(r))}"></td>
<td class="c-name"><div class="nm"><b ${T(c.ln(r))}>${c.ln(r)}</b>${r.status !== 'active' ? stShort(r.status) : ''}</div></td><td class="c-city" ${T(c.city(r.city))}>${c.city(r.city)}</td><td class="c-trn tnum">${r.trn ? num(r.trn) : `<span class="dim" ${T(L('No TRN: not registered for VAT', 'لا يوجد رقم ضريبي: غير مسجل في ضريبة القيمة المضافة'))}>—</span>`}</td>
<td class="c-cr">${c.mbar(x.credit)}</td><td class="c-ag">${c.aging(x.aging)}</td><td class="c-tr">${c.spark(x.trend, { w: 64, h: 18 })}</td><td class="c-bal num">${money(r.balanceCents, r.currency)}</td></tr>`; }).join('')}</tbody>
<tfoot><tr><td></td><td colspan="5" class="tf-l">${L('Total of 30 shown, converted to base currency', 'إجمالي 30 جهة معروضة، محوّل إلى العملة الأساسية')}<span class="tf-od">${L('of which overdue', 'منها متأخرة')} ${money(totOd, 'AED')}</span></td><td></td><td class="num total">${money(totBase, 'AED')}</td></tr></tfoot></table></div>`;
    const x = X[open.ref];
    const tot = x.aging.reduce((s, v) => s + v, 0);
    const pane = `<aside class="pane" aria-label="${L('Statement of account', 'كشف الحساب')}">
<header class="pane-h"><div><p class="kicker">${L('Statement of account', 'كشف الحساب')} · <bdi dir="ltr">${open.ref}</bdi></p><h2 ${T(c.ln(open))}>${c.ln(open)}</h2><p class="meta">${L('Customer', 'عميل')} · ${c.city(open.city)} · ${stLabel('active')}</p></div><button class="icon-btn" aria-label="${L('Close', 'إغلاق')}" ${T(L('Close pane (Esc)', 'إغلاق اللوحة (Esc)'))}>${ic('close')}</button></header>
<div class="due"><p class="kl">${L('Balance due', 'الرصيد المستحق')}</p><div class="hero">${money(open.balanceCents, 'AED')}</div>${c.spark(x.trend, { w: 140, h: 34, cls: 'due-spark' })}
<dl class="duefacts"><div><dt>${L('Credit limit', 'حد الائتمان')}</dt><dd>${money(open.creditLimit * 100, 'AED')}</dd></div><div><dt>${L('Credit used', 'الائتمان المستخدم')}</dt><dd>${c.mbar(x.credit)}</dd></div><div><dt>${L('Terms', 'الشروط')}</dt><dd>${L('30 days', '30 يومًا')}</dd></div></dl></div>
<div class="pane-body">
<h3 class="sub">${L('Ageing', 'أعمار الديون')}</h3>
<div class="agebar">${c.aging(x.aging)}</div><dl class="agecols">${x.aging.map((v, i) => `<div><dt><i class="ag ag${i}"></i>${c.AGE_T[i]}</dt><dd class="${i >= 2 && v ? 'late' : ''}">${money(v, 'AED')}</dd></div>`).join('')}</dl>
<h3 class="sub">${L('Open items', 'البنود المفتوحة')}${link(L('Full statement', 'الكشف الكامل'))}</h3>
<table class="stmt items"><thead><tr><th scope="col">${L('Document', 'المستند')}</th><th scope="col">${L('Due', 'الاستحقاق')}</th><th scope="col">${L('Status', 'الحالة')}</th><th scope="col" class="num">${L('Amount', 'المبلغ')}</th></tr></thead><tbody>${c.INVOICES.map(([n, d, due, amt, s, st]) => `<tr><td><bdi dir="ltr" class="doc">${n}</bdi></td><td class="tnum dim">${due ? num(due) : num(d)}</td><td class="s-${s}">${st}</td><td class="num">${amt}</td></tr>`).join('')}</tbody>
<tfoot><tr><td colspan="3">${L('Balance due', 'الرصيد المستحق')}</td><td class="num total">${money(open.balanceCents, 'AED')}</td></tr></tfoot></table>
<dl class="facts"><div><dt>${L('TRN', 'الرقم الضريبي')}</dt><dd class="tnum">${num(open.trn)}<span class="ok-mark" ${T(L('15 digits, checked', '15 رقمًا، تم التحقق'))}>${ic('check')}</span></dd></div><div><dt>${L('Phone', 'الهاتف')}</dt><dd class="tnum">${num(open.phone)}</dd></div><div><dt>${L('Email', 'البريد')}</dt><dd class="trunc" ${T(open.email)}>${num(open.email)}</dd></div><div><dt>${L('Trade licence', 'الرخصة التجارية')}</dt><dd class="tnum">${num(open.licence)} <span class="warn-t" ${T(L('Expires in 44 days', 'تنتهي بعد 44 يومًا'))}>${L('exp.', 'تنتهي')} ${num('31/12/2026')}</span></dd></div></dl>
</div><footer class="pane-f">${btn('print', L('Send statement', 'إرسال كشف الحساب'), { sm: true, k: 'S' })}${btn('edit', L('Open record', 'فتح السجل'), { sm: true, quiet: true, k: 'Enter' })}${c.overflow([['invoice', L('New invoice', 'فاتورة جديدة'), ''], ['mail', L('Email', 'بريد'), ''], ['archive', L('Archive', 'أرشفة'), '']])}</footer></aside>`;
    const views = [[L('All contacts', 'كل جهات الاتصال'), '100,000', 1], [L('Customers', 'العملاء'), '61,204'], [L('Vendors', 'الموردون'), '38,796'], [L('Overdue', 'متأخرة'), '14'], [L('Credit stop', 'إيقاف ائتماني'), '3']];
    const main = `<div class="listtop"><div class="lt-title"><p class="kicker">${L('Contacts', 'جهات الاتصال')}</p><h1>${L('All contacts', 'كل جهات الاتصال')}</h1></div>
<dl class="lt-figs"><div><dt>${L('Contacts', 'جهات الاتصال')}</dt><dd class="fig-m">${num('100,000')}</dd></div><div><dt>${L('Receivable', 'الذمم المدينة')}</dt><dd class="fig-m">${money(482615025, 'AED')}</dd></div><div><dt>${L('Over credit limit', 'تجاوزوا حد الائتمان')}</dt><dd class="fig-m late">${num('37')}</dd></div></dl>
<div class="lt-end">${btn('download', L('Export', 'تصدير'), { quiet: true })}${btn('plus', L('New contact', 'جهة اتصال جديدة'), { primary: true, k: 'Alt+N' })}</div></div>
<div class="viewbar"><div class="views" role="tablist">${views.map(([t, n, on]) => `<button role="tab" aria-selected="${!!on}">${t}<span class="vn tnum">${num(n)}</span></button>`).join('')}</div><span class="vb-end">${btn('filter', L('Filter', 'تصفية'), { quiet: true, sm: true })}${btn('columns', L('Columns', 'الأعمدة'), { quiet: true, sm: true })}${c.overflow(c.moreItems)}</span></div>
<div class="split"><section class="listcol">${grid}</section>${pane}</div>`;
    return page(L('Contacts · Bizora ERP', 'جهات الاتصال · بيزورا'), `${masthead('contacts')}<main class="main is-list">${main}</main>`);
  };

  // ---------- contact form
  const form = () => {
    const g = c.form2();
    const x = X[open.ref];
    const sec = (n, t, items, o = {}) => `<section class="fsec"><h2 class="rule-h"><span><i class="secn">${n}</i>${t}</span>${o.extra || ''}</h2><div class="fgrid">${items.join('')}</div></section>`;
    const people = `<table class="stmt people"><thead><tr><th scope="col">${L('Name', 'الاسم')}</th><th scope="col">${L('Role', 'الدور')}</th><th scope="col">${L('Phone', 'الهاتف')}</th></tr></thead><tbody>${c.PEOPLE.map(([n, r, p]) => `<tr><td><b>${n}</b></td><td class="dim">${r}</td><td class="tnum"><bdi dir="ltr">${p}</bdi></td></tr>`).join('')}</tbody></table>`;
    const tabs = [[L('General', 'عام'), 1], [L('Addresses', 'العناوين'), 0, 2], [L('Accounting', 'المحاسبة')], [L('People', 'الأشخاص'), 0, 2], [L('Documents', 'المستندات'), 0, 3], [L('History', 'السجل')]];
    const main = `<div class="page formpage"><div class="rechead"><div class="rh-t"><p class="kicker">${L('Customer', 'عميل')} · <bdi dir="ltr">${open.ref}</bdi> · ${stLabel('active')}</p><h1 ${T(c.ln(open))}>${c.ln(open)}</h1><p class="meta">${c.tagsHtml(open)}<span>${L('Fujairah Free Zone, Fujairah', 'المنطقة الحرة بالفجيرة، الفجيرة')}</span></p></div>
<dl class="rh-figs"><div><dt>${L('Balance', 'الرصيد')}</dt><dd class="fig-m">${money(open.balanceCents, 'AED')}</dd></div><div><dt>${L('Overdue', 'المتأخر')}</dt><dd class="fig-m late">${money(x.overdue, 'AED')}</dd></div><div><dt>${L('Sales this year', 'مبيعات هذا العام')}</dt><dd class="fig-m">${money(x.salesYtd, 'AED')}</dd></div></dl>
<div class="rh-end">${btn('print', L('Statement', 'كشف حساب'), { quiet: true, sm: true })}${btn('invoice', L('New invoice', 'فاتورة جديدة'), { quiet: true, sm: true })}${c.overflow([['archive', L('Archive', 'أرشفة'), ''], ['merge', L('Merge duplicates', 'دمج المكرر'), ''], ['trash', L('Delete', 'حذف'), 'Del']])}</div></div>
<div class="tabs" role="tablist">${tabs.map(([t, on, n]) => `<button role="tab" aria-selected="${!!on}">${t}${n ? `<span class="vn tnum">${n}</span>` : ''}</button>`).join('')}</div>
<div class="formbody"><div class="fcol">${sec('1', L('Identity', 'الهوية'), g.identity)}${sec('2', L('Accounting and credit', 'المحاسبة والائتمان'), g.accounting)}${sec('3', L('Contact and address', 'التواصل والعنوان'), g.contact)}${sec('4', L('People', 'الأشخاص'), [people], { extra: btn('plus', L('Add person', 'إضافة شخص'), { quiet: true, sm: true }) })}</div>
<aside class="aside"><p class="kicker">${L('Credit position', 'الوضع الائتماني')}</p><div class="hero">${money(open.balanceCents, 'AED')}</div><p class="meta">${L('of', 'من حدّ')} ${money(open.creditLimit * 100, 'AED')}${L(' limit', '')}</p>${c.mbar(x.credit)}${c.spark(x.trend, { w: 280, h: 48, cls: 'aside-spark' })}<p class="note">${L('Balance over 12 months', 'الرصيد خلال 12 شهرًا')}</p>
<h3 class="sub">${L('Ageing', 'أعمار الديون')}</h3><table class="stmt agev"><tbody>${x.aging.map((v, i) => `<tr><th scope="row">${c.AGE_T[i]}</th><td class="num${i >= 2 && v ? ' late' : ''}">${money(v, 'AED')}</td></tr>`).join('')}</tbody><tfoot><tr><th scope="row">${L('Total', 'الإجمالي')}</th><td class="num total">${money(open.balanceCents, 'AED')}</td></tr></tfoot></table>
<h3 class="sub">${L('Record', 'السجل')}</h3><dl class="facts audit"><div><dt>${L('Created', 'أُنشئ')}</dt><dd><bdi dir="ltr" class="tnum">14/03/2022</bdi> · ${L('Omar Al Suwaidi', 'عمر السويدي')}</dd></div><div><dt>${L('Last changed', 'آخر تغيير')}</dt><dd><bdi dir="ltr" class="tnum">17/11/2026 14:32</bdi> · ${L('Layla Al Mazrouei', 'ليلى المزروعي')}</dd></div></dl></aside></div></div>
<div class="savebar" role="region" aria-label="${L('Save changes', 'حفظ التغييرات')}"><span class="sb-state"><b>${L('2 unsaved changes', 'تغييران غير محفوظين')}</b> · ${L('Credit limit, Accounts email', 'حد الائتمان، بريد الحسابات')}</span><span class="sb-err">${ic('warn')}${L('Accounts email needs a domain', 'بريد الحسابات يحتاج إلى نطاق')}</span><span class="sb-end">${btn(null, L('Discard', 'تجاهل'), { quiet: true, k: 'Esc' })}${btn('save', L('Save', 'حفظ'), { primary: true, k: 'Ctrl+S', showk: true })}</span></div>`;
    return page(c.ln(open) + ' · Bizora ERP', `${masthead('contacts')}<main class="main is-form">${main}</main>`);
  };

  // ---------- settings: users and roles
  const settings = () => {
    const P = c.PERMS;
    const idx = [[L('Users', 'المستخدمون'), '12', 1], [L('Roles and permissions', 'الأدوار والصلاحيات'), '8'], [L('Sign-in policy', 'سياسة الدخول')], [L('API keys', 'مفاتيح الواجهة'), '2'], [L('Audit log', 'سجل التدقيق')], [L('Company', 'الشركة')], [L('Taxes', 'الضرائب')], [L('Currencies', 'العملات'), '7']];
    const ini = (u) => (ar ? u[1].slice(0, 1) + u[1].split(' ')[1].replace(/^ال/, '').slice(0, 1) : u[0].split(' ').map((w) => w[0]).slice(0, 2).join(''));
    const users = `<table class="grid users" aria-label="${L('Users', 'المستخدمون')}"><thead><tr><th class="c-sel"><input type="checkbox" aria-label="${L('Select all', 'تحديد الكل')}"></th>${[[L('User', 'المستخدم'), 'u-n'], [L('Email', 'البريد'), 'u-e'], [L('Roles', 'الأدوار'), 'u-r'], [L('Two-step', 'التحقق'), 'u-2'], [L('Status', 'الحالة'), 'u-s'], [L('Last sign-in', 'آخر دخول'), 'u-l']].map(([h, k]) => `<th scope="col" class="${k}" ${T(h)}><span class="th-in">${h}</span></th>`).join('')}</tr></thead><tbody>${USERS.map((u, i) => `<tr class="row${i === 1 ? ' is-selected' : ''}" tabindex="0"><td class="c-sel"><input type="checkbox"${i === 1 ? ' checked' : ''} aria-label="${L('Select', 'تحديد')} ${ar ? u[1] : u[0]}"></td><td class="u-n"><div class="nm"><span class="avatar">${ini(u)}</span><b ${T(ar ? u[1] : u[0])}>${ar ? u[1] : u[0]}</b></div></td><td class="u-e trunc" ${T(u[2])}>${num(u[2])}</td><td class="u-r" ${T(u[3].map((r) => (ar ? ROLE_AR[r] : r)).join(', '))}>${u[3].map((r) => (ar ? ROLE_AR[r] : r)).join(ar ? '، ' : ', ')}</td><td class="u-2">${u[6] ? `<span class="ok-inline">${ic('check')}${L('On', 'مفعّل')}</span>` : `<span class="dim">${L('Off', 'غير مفعّل')}</span>`}</td><td class="u-s">${stLabel(u[4] === 'hold' ? 'suspended' : u[4])}</td><td class="u-l tnum">${u[5] === '—' ? '<span class="dim">—</span>' : num(u[5])}</td></tr>`).join('')}</tbody></table>`;
    const main = `<div class="page setpage"><nav class="setidx" aria-label="${L('Settings', 'الإعدادات')}"><p class="kicker">${L('Settings', 'الإعدادات')}</p>${idx.map(([t, n, a]) => `<a href="#" class="${a ? 'is-active' : ''}"${a ? ' aria-current="page"' : ''}><span>${t}</span>${n ? `<span class="vn tnum">${num(n)}</span>` : ''}</a>`).join('')}</nav>
<div class="setmain"><div class="listtop"><div class="lt-title"><p class="kicker">${L('Settings · Access', 'الإعدادات · الوصول')}</p><h1>${L('Users and roles', 'المستخدمون والأدوار')}</h1></div>
<dl class="lt-figs"><div><dt>${L('Seats used', 'المقاعد المستخدمة')}</dt><dd class="fig-m">${num('12<small> / 25</small>')}</dd></div><div><dt>${L('Two-step on', 'التحقق بخطوتين')}</dt><dd class="fig-m">${num('7<small> / 12</small>')}</dd></div><div><dt>${L('Invited', 'مدعوون')}</dt><dd class="fig-m">${num('1')}</dd></div><div><dt>${L('Locked', 'مقفلون')}</dt><dd class="fig-m late">${num('1')}</dd></div></dl>
<div class="lt-end">${btn('download', L('Export', 'تصدير'), { quiet: true })}${btn('plus', L('Invite user', 'دعوة مستخدم'), { primary: true, k: 'Alt+N' })}</div></div>
<div class="setgrid"><section class="setusers">${sh(L('Users', 'المستخدمون'), `<span class="rh-meta">${L('12 people · last access review 10/11/2026', '12 شخصًا · آخر مراجعة للصلاحيات 10/11/2026')}</span>`)}<div class="grid-wrap">${users}</div>
${sh(L('Access changes this month', 'تغييرات الصلاحيات هذا الشهر'), link(L('Audit log', 'سجل التدقيق')))}<table class="ltable acc"><tbody>${c.ACCESS.map(([t, i, w, m]) => `<tr><td class="tnum dim"><bdi dir="ltr">${t}</bdi></td><td class="ent" ${T(w + ' · ' + m)}><b>${w}</b> · ${m}</td></tr>`).join('')}</tbody></table></section>
<aside class="setroles">${sh(L('Roles', 'الأدوار'), link(L('New role', 'دور جديد')))}<ol class="roles">${ROLES.map(([r, n, en, a], i) => `<li class="${i === 1 ? 'is-on' : ''}"><div><b>${ar ? ROLE_AR[r] : r}</b><small ${T(ar ? a : en)}>${ar ? a : en}</small></div><span class="tnum">${num(n)}</span></li>`).join('')}</ol>
${sh(L('Accountant can', 'صلاحيات المحاسب'))}<table class="perm"><thead><tr><th scope="col"></th>${P.cols.map((h) => `<th scope="col">${h}</th>`).join('')}</tr></thead><tbody>${P.modules.map((m, i) => `<tr><th scope="row">${m}</th>${P.accountant[i].map((v) => `<td>${v ? `<span class="pm on" role="img" aria-label="${L('Allowed', 'مسموح')}">${ic('check')}</span>` : `<span class="pm off" role="img" aria-label="${L('Not allowed', 'غير مسموح')}">–</span>`}</td>`).join('')}</tr>`).join('')}</tbody></table></aside></div></div></div>`;
    return page(L('Users and roles · Bizora ERP', 'المستخدمون والأدوار · بيزورا'), `${masthead('set')}<main class="main is-settings">${main}</main>`);
  };

  // ---------- sign-in
  const signin = () => {
    const body = `<div class="signin"><header class="si-top">${brand()}${c.switches()}</header>
<section class="si-ed" aria-hidden="true"><p class="kicker">${L('Bizora ERP · for UAE traders and manufacturers', 'بيزورا · لتجار ومصنّعي الإمارات')}</p><h2>${L('Know where every dirham stands, before your first coffee.', 'اعرف أين يقف كل درهم، قبل قهوتك الأولى.')}</h2>
<div class="specimen"><p class="kl">${L('Receivables, 17/11/2026', 'الذمم المدينة، 17/11/2026')}</p><div class="hero xl">${money(482615025, 'AED')}</div>${c.spark(c.KPIS[0].series, { w: 420, h: 56, cls: 'si-spark' })}
<table class="stmt"><tbody><tr><th scope="row">${L('Current', 'جارية')}</th><td class="num">${money(463972975, 'AED')}</td></tr><tr><th scope="row">${L('Overdue', 'متأخرة')}</th><td class="num late">${money(18642050, 'AED')}</td></tr></tbody><tfoot><tr><th scope="row">${L('Total', 'الإجمالي')}</th><td class="num total">${money(482615025, 'AED')}</td></tr></tfoot></table></div></section>
<main class="si-main"><form class="si-card" onsubmit="return false"><p class="kicker">${L('Welcome back', 'مرحبًا بعودتك')}</p><h1>${L('Sign in', 'تسجيل الدخول')}</h1><p class="sub">${L('Use your work email to open your company.', 'استخدم بريد العمل لفتح حساب شركتك.')}</p>
${c.F.text('s-co', L('Company code', 'رمز الشركة'), 'alwasl-trading', { ltr: true })}${c.F.text('s-em', L('Work email', 'بريد العمل'), 'layla@alwasl-trading.example', { ltr: true })}
<div class="fld"><label for="s-pw">${L('Password', 'كلمة المرور')}</label><div class="ctl"><input id="s-pw" type="password" dir="ltr" value="correct-horse-1"><button class="in-btn" type="button" aria-label="${L('Show password', 'إظهار كلمة المرور')}">${ic('eye')}</button></div><div class="hint warn">${ic('warn')}${L('Caps Lock is on', 'مفتاح الأحرف الكبيرة مفعّل')}</div></div>
<div class="row-between"><label class="chk"><input type="checkbox" checked> ${L('Keep me signed in on this device', 'أبقِني مسجّلًا على هذا الجهاز')}</label><a href="#">${L('Forgot password?', 'نسيت كلمة المرور؟')}</a></div>
<button class="btn btn-primary btn-block">${L('Sign in', 'دخول')}</button>
<div class="alt2">${btn('key', L('Use a passkey', 'استخدام مفتاح المرور'), { quiet: true })}<span class="dot-sep" aria-hidden="true">·</span>${btn('building', L('Company single sign-on', 'الدخول الموحّد للشركة'), { quiet: true })}</div></form></main>
<footer class="si-foot"><span>${L('Data kept in the UAE', 'البيانات محفوظة في الإمارات')}</span><span>${L('Two-step verification', 'التحقق بخطوتين')}</span><span>${L('English and Arabic', 'العربية والإنجليزية')}</span><span class="grow"><bdi dir="ltr">© 2026 Bizora ERP</bdi></span></footer></div>`;
    return page(L('Sign in · Bizora ERP', 'تسجيل الدخول · بيزورا'), body, 'is-signin');
  };

  return { dashboard: dashboard(), contacts: contacts(), form: form(), settings: settings(), signin: signin() };
}
