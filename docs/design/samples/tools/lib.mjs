// Shared content for the three sample directions: demo data, icons, strings, fragments.
// Markup here carries semantic classes only; each direction styles them with its own tokens.
import fs from 'node:fs';
export const DATA = JSON.parse(fs.readFileSync(new URL('./data.json', import.meta.url), 'utf8'));

const ICONS = {
  home: 'M3 11l9-7 9 7M5 10v10h5v-6h4v6h5V10',
  users: 'M9 11a3.5 3.5 0 100-7 3.5 3.5 0 000 7zM2.5 20c0-3.6 2.9-6 6.5-6s6.5 2.4 6.5 6M16 4.5a3.2 3.2 0 010 6M18 14.2c2 .6 3.5 2.4 3.5 5.8',
  user: 'M12 12a4 4 0 100-8 4 4 0 000 8zM4.5 21c0-4 3.4-7 7.5-7s7.5 3 7.5 7',
  building: 'M5 21V4h9v17M14 9h5v12M3 21h18M8.5 8h2M8.5 12h2M8.5 16h2',
  cart: 'M3 4h2.5l2 11h10l2-8H7M9.5 18.5a1.3 1.3 0 100 2.6 1.3 1.3 0 000-2.6zM16.5 18.5a1.3 1.3 0 100 2.6 1.3 1.3 0 000-2.6z',
  truck: 'M2 6h12v10H2zM14 10h4l3 3v3h-7M6.5 16.5a2 2 0 100 4 2 2 0 000-4zM17.5 16.5a2 2 0 100 4 2 2 0 000-4z',
  box: 'M12 3l8 4v10l-8 4-8-4V7zM4 7l8 4 8-4M12 11v10',
  bank: 'M3 9l9-5 9 5M5 9v9M9.5 9v9M14.5 9v9M19 9v9M3 21h18',
  book: 'M5 4h11a3 3 0 013 3v13H8a3 3 0 01-3-3zM5 17a3 3 0 013-3h11',
  gear: 'M12 15a3 3 0 100-6 3 3 0 000 6zM19 12c0-.4 0-.8-.1-1.2l2-1.5-2-3.4-2.3.9a7 7 0 00-2-1.2L14.3 3h-4.6l-.4 2.6a7 7 0 00-2 1.2l-2.3-.9-2 3.4 2 1.5A7 7 0 005 12c0 .4 0 .8.1 1.2l-2 1.5 2 3.4 2.3-.9a7 7 0 002 1.2l.4 2.6h4.6l.4-2.6a7 7 0 002-1.2l2.3.9 2-3.4-2-1.5c.1-.4.1-.8.1-1.2z',
  shield: 'M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6zM8.5 12l2.5 2.5 4.5-5',
  chart: 'M4 20V4M4 20h16M8 16v-5M12 16V8M16 16v-8',
  search: 'M10.5 17a6.5 6.5 0 100-13 6.5 6.5 0 000 13zM15.5 15.5L20 20',
  plus: 'M12 5v14M5 12h14',
  edit: 'M4 20h4L19 9l-4-4L4 16zM13.5 6.5l4 4',
  trash: 'M5 7h14M9 7V4h6v3M7 7l1 13h8l1-13M10 11v6M14 11v6',
  mail: 'M3 6h18v12H3zM3 7l9 6 9-6',
  phone: 'M6 3h3l1.5 4.5-2 1.5a11 11 0 006 6l1.5-2 4.5 1.5v3a2 2 0 01-2 2A16 16 0 014 5a2 2 0 012-2z',
  print: 'M7 9V3h10v6M7 17H4v-7h16v7h-3M7 14h10v7H7z',
  download: 'M12 4v11M7.5 10.5L12 15l4.5-4.5M5 20h14',
  upload: 'M12 15V4M7.5 8.5L12 4l4.5 4.5M5 20h14',
  refresh: 'M20 11a8 8 0 00-14-4L4 9M4 4v5h5M4 13a8 8 0 0014 4l2-2M20 20v-5h-5',
  filter: 'M3 5h18l-7 8v6l-4-2v-4z',
  columns: 'M4 5h16v14H4zM9.5 5v14M14.5 5v14',
  more: 'M5 12h.01M12 12h.01M19 12h.01',
  chevd: 'M6 9l6 6 6-6',
  chevr: 'M9 6l6 6-6 6',
  chevl: 'M15 6l-6 6 6 6',
  chevu: 'M6 15l6-6 6 6',
  check: 'M5 12.5l4.5 4.5L19 7.5',
  lock: 'M6 11h12v9H6zM8.5 11V8a3.5 3.5 0 017 0v3',
  globe: 'M12 21a9 9 0 100-18 9 9 0 000 18zM3 12h18M12 3c3 3 3 15 0 18M12 3c-3 3-3 15 0 18',
  sun: 'M12 16a4 4 0 100-8 4 4 0 000 8zM12 3v2M12 19v2M3 12h2M19 12h2M5.6 5.6L7 7M17 17l1.4 1.4M5.6 18.4L7 17M17 7l1.4-1.4',
  moon: 'M20 14.5A8 8 0 019.5 4 8 8 0 1020 14.5z',
  contrast: 'M12 21a9 9 0 100-18 9 9 0 000 18zM12 3v18M12 3a9 9 0 010 18z',
  list: 'M8 6h12M8 12h12M8 18h12M4 6h.01M4 12h.01M4 18h.01',
  calendar: 'M4 6h16v14H4zM4 10h16M8 3v4M16 3v4',
  key: 'M8 15a4 4 0 110-8 4 4 0 010 8zM11 11h10M18 11v3M15 11v2',
  close: 'M6 6l12 12M18 6L6 18',
  tag: 'M3 12V4h8l10 10-8 8zM7.5 8h.01',
  file: 'M6 3h8l4 4v14H6zM14 3v4h4',
  clip: 'M18 11l-7 7a4.5 4.5 0 01-6.4-6.4l8-8a3 3 0 014.3 4.3l-8 8a1.5 1.5 0 01-2.1-2.1l7-7',
  pin: 'M12 21s7-6 7-11a7 7 0 10-14 0c0 5 7 11 7 11zM12 12a2.5 2.5 0 100-5 2.5 2.5 0 000-5z',
  eye: 'M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12zM12 15a3 3 0 100-6 3 3 0 000 6z',
  invoice: 'M6 3h12v18l-3-2-3 2-3-2-3 2zM9 8h6M9 12h6',
  save: 'M5 4h12l3 3v13H5zM8 4v5h7V4M8 20v-6h8v6',
  rows: 'M4 7h16M4 12h16M4 17h16',
  sidebar: 'M4 5h16v14H4zM9 5v14',
  help: 'M12 21a9 9 0 100-18 9 9 0 000 18zM9.5 9.5a2.5 2.5 0 114 2c-.9.6-1.5 1-1.5 2M12 17h.01',
  merge: 'M6 4v6a6 6 0 006 6h6M18 12l3 4-3 4M6 4L3 8M6 4l3 4',
  archive: 'M3 5h18v4H3zM5 9v11h14V9M9.5 13h5',
  sort: 'M8 5v14M4.5 15.5L8 19l3.5-3.5M16 19V5M12.5 8.5L16 5l3.5 3.5',
  warn: 'M12 4l9 16H3zM12 10v5M12 17.5h.01',
  info: 'M12 21a9 9 0 100-18 9 9 0 000 18zM12 11v6M12 7.5h.01',
  bell: 'M6 16V11a6 6 0 1112 0v5l2 2H4zM10 20a2 2 0 004 0',
  logout: 'M10 4H5v16h5M14 8l4 4-4 4M18 12H9',
  dashboard: 'M4 4h7v9H4zM13 4h7v5h-7zM13 11h7v9h-7zM4 15h7v5H4z',
  percent: 'M6 18L18 6M7.5 9a1.5 1.5 0 100-3 1.5 1.5 0 000 3zM16.5 18a1.5 1.5 0 100-3 1.5 1.5 0 000 3z',
  coins: 'M9 13a5 3 0 100-6 5 3 0 000 6zM4 10v4c0 1.7 2.2 3 5 3s5-1.3 5-3v-4M14 8.5c2.8 0 5 1.3 5 3v4c0 1.7-2.2 3-5 3',
};
export const ic = (n, cls = '') =>
  `<svg class="ic ${cls}" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="${ICONS[n]}"/></svg>`;

const TAG_AR = { VIP: 'مميّز', Export: 'تصدير', Supplier: 'مورّد', Customer: 'عميل', Distributor: 'موزّع', Contractor: 'مقاول', Government: 'حكومي', Retail: 'تجزئة' };
const CITY_AR = { Dubai: 'دبي', 'Abu Dhabi': 'أبوظبي', Sharjah: 'الشارقة', Ajman: 'عجمان', Fujairah: 'الفجيرة', 'Ras al-Khaimah': 'رأس الخيمة', 'Umm al-Quwain': 'أم القيوين', Riyadh: 'الرياض', Muscat: 'مسقط', Jeddah: 'جدة', Dammam: 'الدمام', Sohar: 'صحار', Salalah: 'صلالة', Doha: 'الدوحة', 'Kuwait City': 'مدينة الكويت', Manama: 'المنامة', Shenzhen: 'شنتشن', Chennai: 'تشيناي', Mumbai: 'مومباي', London: 'لندن', Kochi: 'كوتشي', Shanghai: 'شنغهاي', Yiwu: 'ييوو', Manchester: 'مانشستر' };
const AREA_AR = { 'Al Quoz': 'القوز', 'Ajman Free Zone': 'المنطقة الحرة بعجمان', 'Fujairah Free Zone': 'المنطقة الحرة بالفجيرة', 'Saif Zone': 'منطقة سيف', 'Al Rashidiya': 'الراشدية', 'Business Bay': 'الخليج التجاري', Deira: 'ديرة', 'Al Hamra': 'الحمرا', 'Al Reem Island': 'جزيرة الريم', 'Al Majaz': 'المجاز', 'Jebel Ali': 'جبل علي', RAKEZ: 'راكز', 'Bur Dubai': 'بر دبي', 'Al Salamah': 'السلامة', 'Al Nakheel': 'النخيل', 'Al Jurf': 'الجرف' };

export const dateFmt = (d, m, y) => `${String(d).padStart(2, '0')}/${String(m).padStart(2, '0')}/${y}`;
const fmtNum = (n) => n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
export const RATES = { AED: 1, USD: 3.6725, SAR: 0.9793, OMR: 9.5405, KWD: 11.9632, QAR: 1.009, BHD: 9.743 };

export function mk(lang) {
  const ar = lang === 'ar';
  const L = (en, a) => (ar ? a : en);
  const ln = (c) => (ar ? c.nameAr : c.name);
  const city = (c) => (ar ? CITY_AR[c] || c : c);
  const area = (c) => (ar ? AREA_AR[c] || c : c);
  const tagL = (t) => (ar ? TAG_AR[t] || t : t);
  const initials = (c) => {
    let s = (ar ? c.nameAr : c.name).replace(/[^\p{L}\s]/gu, '').split(/\s+/).filter(Boolean);
    if (ar) { s = s.filter((w) => !/^(شركة|مؤسسة)$/.test(w)).map((w) => w.replace(/^ال/, '')); return (s[0] || '').slice(0, 1) + (s[1] ? s[1].slice(0, 1) : ''); }
    return (s[0][0] + (s[1] ? s[1][0] : '')).toUpperCase();
  };
  // Money: number then currency code; negatives in brackets and red; always dir=ltr so brackets stay put.
  const money = (cents, cur, o = {}) => {
    const neg = cents < 0;
    const n = fmtNum(Math.abs(cents) / 100);
    return `<span class="money${neg ? ' neg' : ''}${cents === 0 ? ' zero' : ''}" dir="ltr"><span class="amt">${neg ? `(${n})` : n}</span><span class="cur">${cur}</span></span>`;
  };
  const moneyPre = (amount, cur) => `<span class="money" dir="ltr"><span class="cur">${cur}</span><span class="amt">${fmtNum(amount)}</span></span>`;
  const tagsHtml = (c) => c.tags.map((t) => `<span class="tag tag-${t.toLowerCase()}">${tagL(t)}</span>`).join('');
  const statusT = { active: L('Active', 'نشط'), hold: L('On hold', 'موقوف مؤقتًا'), blocked: L('Credit stop', 'إيقاف ائتماني') };
  const status = (s) => `<span class="pill pill-${s}"><i class="dot"></i>${statusT[s]}</span>`;
  const kindT = (k) => (k === 'company' ? L('Company', 'شركة') : L('Person', 'فرد'));
  const num = (v) => `<bdi dir="ltr">${v}</bdi>`;
  const T = (txt) => `title="${txt.replace(/"/g, '&quot;')}"`;

  const COLS = {
    name: { h: L('Name', 'الاسم'), cls: 'c-name' },
    type: { h: L('Type', 'النوع'), cls: 'c-type' },
    city: { h: L('City', 'المدينة'), cls: 'c-city' },
    trn: { h: L('TRN', 'الرقم الضريبي'), cls: 'c-trn' },
    phone: { h: L('Phone', 'الهاتف'), cls: 'c-phone' },
    tags: { h: L('Tags', 'الوسوم'), cls: 'c-tags' },
    status: { h: L('Status', 'الحالة'), cls: 'c-status' },
    balance: { h: L('Balance', 'الرصيد'), cls: 'c-bal num' },
  };
  const cell = (k, c) => {
    switch (k) {
      case 'name': return `<td class="c-name trunc"><span class="avatar av-${c.kind}">${initials(c)}</span><span class="nm"><b ${T(ln(c))}>${ln(c)}</b><small>${num(c.ref)}</small></span></td>`;
      case 'type': return `<td class="c-type">${kindT(c.kind)}</td>`;
      case 'city': return `<td class="c-city trunc" ${T(city(c.city) + ' · ' + area(c.area || ''))}>${city(c.city)}</td>`;
      case 'trn': return `<td class="c-trn tnum">${c.trn ? num(c.trn) : '<span class="dim">—</span>'}</td>`;
      case 'phone': return `<td class="c-phone tnum">${c.phone ? num(c.phone) : '<span class="dim">—</span>'}</td>`;
      case 'tags': return `<td class="c-tags" ${T(c.tags.map(tagL).join(', '))}>${c.tags.slice(0, 1).map((t) => `<span class="tag tag-${t.toLowerCase()}">${tagL(t)}</span>`).join('')}${c.tags.length > 1 ? `<span class="tag more">+${c.tags.length - 1}</span>` : ''}</td>`;
      case 'status': return `<td class="c-status">${status(c.status)}</td>`;
      case 'balance': return `<td class="c-bal num">${money(c.balanceCents, c.currency)}</td>`;
    }
  };
  const OPEN = 'C000003';
  const gridContacts = ({ cols, rows = 28 }) => {
    const list = DATA.slice(0, rows);
    return `<div class="grid-wrap" tabindex="-1"><table class="grid" aria-label="${L('Contacts', 'جهات الاتصال')}">
<thead><tr><th class="c-sel"><input type="checkbox" aria-label="${L('Select all', 'تحديد الكل')}"></th>${cols.map((k) => `<th class="${COLS[k].cls}" scope="col"><span class="th-in">${COLS[k].h}${k === 'name' ? ic('sort', 'th-sort') : ''}</span><i class="rz" aria-hidden="true"></i></th>`).join('')}</tr></thead>
<tbody>${list.map((c) => `<tr class="row${c.ref === OPEN ? ' is-selected' : ''}" tabindex="0" data-ref="${c.ref}"${c.ref === OPEN ? ' aria-selected="true"' : ''}><td class="c-sel"><input type="checkbox"${c.ref === OPEN ? ' checked' : ''} aria-label="${L('Select', 'تحديد')} ${ln(c)}"></td>${cols.map((k) => cell(k, c)).join('')}</tr>`).join('')}</tbody></table></div>`;
  };
  const open = DATA.find((c) => c.ref === OPEN);
  const used = Math.min(1, Math.abs(open.balanceCents) / 100 / open.creditLimit);

  const detailsPane = (c = open) => `<aside class="details" aria-label="${L('Details', 'التفاصيل')}">
<div class="det-head"><span class="avatar lg av-${c.kind}">${initials(c)}</span><div class="det-title"><h2 ${T(ln(c))}>${ln(c)}</h2><p>${kindT(c.kind)} · ${city(c.city)} · ${num(c.ref)}</p></div><button class="icon-btn" aria-label="${L('Close', 'إغلاق')}" ${T(L('Close pane (Esc)', 'إغلاق اللوحة (Esc)'))}>${ic('close')}</button></div>
<div class="det-actions"><button class="btn btn-sm">${ic('phone')}<span>${L('Call', 'اتصال')}</span></button><button class="btn btn-sm">${ic('mail')}<span>${L('Email', 'بريد')}</span></button><button class="btn btn-sm btn-quiet">${ic('edit')}<span>${L('Open', 'فتح')}</span></button></div>
<div class="det-kpis"><div><span class="k">${L('Balance', 'الرصيد')}</span>${money(c.balanceCents, c.currency)}</div><div><span class="k">${L('Credit limit', 'حد الائتمان')}</span>${money(c.creditLimit * 100, c.currency)}</div></div>
<div class="meter" role="img" aria-label="${L('Credit used', 'الائتمان المستخدم')} ${Math.round(used * 100)}%"><i style="width:${Math.round(used * 100)}%"></i></div>
<dl class="det-list">
<dt>${L('TRN', 'الرقم الضريبي')}</dt><dd class="tnum">${num(c.trn)}<span class="ok-mark" ${T(L('15 digits, valid', '١٥ رقمًا، صالح'))}>${ic('check')}</span></dd>
<dt>${L('Phone', 'الهاتف')}</dt><dd class="tnum">${num(c.phone)}</dd>
<dt>${L('Email', 'البريد')}</dt><dd class="trunc" ${T(c.email)}>${num(c.email)}</dd>
<dt>${L('Address', 'العنوان')}</dt><dd>${num(c.street)}, ${area(c.area)}, ${city(c.city)}</dd>
<dt>${L('Trade licence', 'الرخصة التجارية')}</dt><dd class="tnum">${num(c.licence)} · ${L('expires', 'تنتهي')} ${num('31/12/2026')}</dd>
<dt>${L('Tags', 'الوسوم')}</dt><dd>${tagsHtml(c)}</dd></dl>
<h3 class="det-sub">${L('Recent activity', 'آخر النشاطات')}</h3>
<ul class="activity">
<li><i class="adot"></i><div><b>${L('Invoice INV-2026-01842 posted', 'تم ترحيل الفاتورة INV-2026-01842')}</b><small>${num('14/11/2026')} · ${money(1862500, c.currency)}</small></div></li>
<li><i class="adot"></i><div><b>${L('Payment received', 'تم استلام دفعة')}</b><small>${num('09/11/2026')} · ${money(-940000, c.currency)}</small></div></li>
<li><i class="adot"></i><div><b>${L('Credit limit reviewed', 'تمت مراجعة حد الائتمان')}</b><small>${num('28/10/2026')} · ${L('Layla Al Mazrouei', 'ليلى المزروعي')}</small></div></li></ul>
</aside>`;

  // ---- form fields
  const F = {
    text: (id, label, v, o = {}) => `<div class="fld${o.wide ? ' wide' : ''}${o.err ? ' has-err' : ''}"><label for="${id}">${label}${o.req ? '<span class="req" aria-hidden="true">*</span>' : ''}</label><div class="ctl"><input id="${id}" value="${v}"${o.ltr ? ' dir="ltr"' : ''}${o.rtl ? ' dir="rtl"' : ''}${o.ro ? ' readonly' : ''}${o.err ? ' aria-invalid="true"' : ''}>${o.ok ? `<span class="ok-mark">${ic('check')}</span>` : ''}</div>${o.hint ? `<div class="hint${o.err ? ' err' : ''}">${o.err ? ic('warn') : ''}${o.hint}</div>` : ''}</div>`,
    select: (id, label, v, o = {}) => `<div class="fld${o.wide ? ' wide' : ''}"><label id="${id}-l">${label}${o.req ? '<span class="req" aria-hidden="true">*</span>' : ''}</label><div class="ctl"><div class="select" id="${id}" tabindex="0" role="combobox" aria-labelledby="${id}-l" aria-expanded="false"><span class="trunc" ${T(v)}>${v}</span>${o.look ? ic('search') : ic('chevd')}</div></div>${o.hint ? `<div class="hint">${o.hint}</div>` : ''}</div>`,
    date: (id, label, v, o = {}) => `<div class="fld${o.warn ? ' has-warn' : ''}"><label for="${id}">${label}</label><div class="ctl"><input id="${id}" dir="ltr" class="num" value="${v}" placeholder="dd/mm/yyyy" inputmode="numeric"><button class="in-btn" aria-label="${L('Pick a date', 'اختيار تاريخ')}">${ic('calendar')}</button></div><div class="hint${o.warn ? ' warn' : ''}">${o.warn ? ic('warn') : ''}${o.hint || 'dd/mm/yyyy'}</div></div>`,
    money: (id, label, amount, cur, o = {}) => `<div class="fld"><label for="${id}">${label}</label><div class="ctl money-in"><span class="cur-chip" tabindex="0" role="button" aria-label="${L('Currency', 'العملة')} ${cur}">${cur}${ic('chevd')}</span><input id="${id}" dir="ltr" class="num" value="${fmtNum(amount)}" inputmode="decimal"></div><div class="hint tnum" dir="ltr">${cur === 'AED' ? `${L('Base currency', 'العملة الأساسية')}` : `≈ AED ${fmtNum(amount * RATES[cur])} @ ${RATES[cur].toFixed(4)}`}</div></div>`,
  };
  const formGeneral = (c = open) => ({
    identity: [
      F.text('f-name', L('Name', 'الاسم'), c.name, { req: true, wide: true, ltr: true }),
      F.text('f-name-ar', L('Name in Arabic', 'الاسم بالعربية'), c.nameAr, { wide: true, rtl: true }),
      F.select('f-type', L('Type', 'النوع'), kindT(c.kind), { req: true }),
      F.text('f-ref', L('Reference', 'الرقم المرجعي'), c.ref, { ro: true, ltr: true }),
      F.text('f-trn', L('Tax registration number (TRN)', 'رقم التسجيل الضريبي'), c.trn, { ltr: true, ok: true, hint: L('15 digits, checked', '١٥ رقمًا، تم التحقق') }),
      F.text('f-lic', L('Trade licence no.', 'رقم الرخصة التجارية'), c.licence, { ltr: true }),
      F.date('f-licexp', L('Licence expiry', 'انتهاء الرخصة'), '31/12/2026', { warn: true, hint: L('Expires in 44 days', 'تنتهي بعد ٤٤ يومًا') }),
      F.date('f-since', L('Customer since', 'عميل منذ'), '14/03/2022'),
    ],
    contact: [
      F.text('f-email', L('Email', 'البريد الإلكتروني'), c.email, { ltr: true, wide: true }),
      F.text('f-phone', L('Phone', 'الهاتف'), c.phone, { ltr: true }),
      F.text('f-web', L('Website', 'الموقع'), 'https://www.alwaslcables3.example', { ltr: true }),
      F.text('f-street', L('Street', 'الشارع'), c.street, { wide: true }),
      F.text('f-area', L('Area', 'المنطقة'), area(c.area)),
      F.select('f-city', L('City', 'المدينة'), city(c.city), { look: true }),
    ],
    accounting: [
      F.select('f-cur', L('Currency', 'العملة'), L('AED, UAE dirham', 'AED، الدرهم الإماراتي')),
      F.money('f-credit', L('Credit limit', 'حد الائتمان'), c.creditLimit, c.currency),
      F.money('f-avg', L('Average order value', 'متوسط قيمة الطلب'), 12480, 'USD'),
      F.select('f-terms', L('Payment terms', 'شروط الدفع'), L('30 days from invoice', '٣٠ يومًا من تاريخ الفاتورة')),
      F.select('f-vat', L('VAT treatment', 'المعالجة الضريبية'), L('Standard rate, 5%', 'النسبة القياسية 5٪')),
      F.select('f-acct', L('Receivable account', 'حساب الذمم المدينة'), '1210 · ' + L('Trade receivables', 'ذمم تجارية مدينة'), { look: true }),
    ],
  });

  // ---- navigation
  const NAV = [
    { id: 'home', ic: 'dashboard', t: L('Dashboard', 'لوحة المعلومات') },
    { g: 'contacts', ic: 'users', t: L('Contacts', 'جهات الاتصال'), items: [[L('All contacts', 'كل جهات الاتصال'), 'contacts'], [L('Customers', 'العملاء')], [L('Vendors', 'الموردون')]] },
    { g: 'sales', ic: 'cart', t: L('Sales', 'المبيعات'), items: [[L('Quotations', 'عروض الأسعار')], [L('Sales orders', 'أوامر البيع')], [L('Invoices', 'الفواتير')]] },
    { g: 'purch', ic: 'truck', t: L('Purchasing', 'المشتريات'), items: [[L('Purchase orders', 'أوامر الشراء')], [L('Vendor bills', 'فواتير الموردين')]] },
    { g: 'inv', ic: 'box', t: L('Inventory', 'المخزون'), items: [[L('Products', 'المنتجات')], [L('Stock movements', 'حركات المخزون')], [L('Warehouses', 'المستودعات')]] },
    { g: 'fin', ic: 'bank', t: L('Finance', 'المالية'), items: [[L('Journal entries', 'قيود اليومية')], [L('Chart of accounts', 'دليل الحسابات')], [L('VAT returns', 'الإقرارات الضريبية')]] },
    { g: 'set', ic: 'gear', t: L('Settings', 'الإعدادات'), items: [[L('Users and roles', 'المستخدمون والأدوار'), 'settings'], [L('Companies', 'الشركات')], [L('Taxes', 'الضرائب')], [L('Currencies', 'العملات')]] },
  ];
  const navHtml = (active, o = {}) => NAV.map((n) => {
    if (!n.items) return `<a class="nav-item nav-top${active === n.id ? ' is-active' : ''}" href="#">${ic(n.ic)}<span>${n.t}</span></a>`;
    const open = n.items.some((i) => i[1] === active) || n.g === 'sales' || (o.openAll);
    return `<details class="nav-group"${open ? ' open' : ''}><summary>${ic(n.ic)}<span>${n.t}</span>${ic('chevd', 'chev')}</summary><div class="nav-items">${n.items.map((i) => `<a class="nav-item${i[1] === active ? ' is-active' : ''}" href="#"${i[1] === active ? ' aria-current="page"' : ''}>${i[0]}</a>`).join('')}</div></details>`;
  }).join('');

  // ---- switches
  const switches = (o = {}) => `<div class="switches">
<div class="seg" role="group" aria-label="${L('Density', 'الكثافة')}" data-switch="density"><button data-v="compact" ${T(L('Compact: more rows', 'مدمج: صفوف أكثر'))}>${ic('rows')}<span>${L('Compact', 'مدمج')}</span></button><button data-v="comfortable" ${T(L('Comfortable: more air', 'مريح: مساحة أكبر'))}><span>${L('Comfortable', 'مريح')}</span></button></div>
<div class="seg seg-icons" role="group" aria-label="${L('Theme', 'المظهر')}" data-switch="theme"><button data-v="light" aria-label="${L('Light', 'فاتح')}" ${T(L('Light', 'فاتح'))}>${ic('sun')}</button><button data-v="dark" aria-label="${L('Dark', 'داكن')}" ${T(L('Dark', 'داكن'))}>${ic('moon')}</button><button data-v="hc" aria-label="${L('High contrast', 'تباين عالٍ')}" ${T(L('High contrast', 'تباين عالٍ'))}>${ic('contrast')}</button></div>
<a class="lang-link" data-keep data-lang-swap href="#" lang="${ar ? 'en' : 'ar'}" ${T(L('العربية', 'English'))}>${ic('globe')}<span>${ar ? 'English' : 'العربية'}</span></a></div>`;

  // ---- commands
  const kbd = (k) => `<kbd>${k}</kbd>`;
  const btn = (icon, label, o = {}) => `<button class="btn${o.primary ? ' btn-primary' : ''}${o.quiet ? ' btn-quiet' : ''}"${o.k ? ` ${T(label + ' (' + o.k + ')')}` : ''}>${ic(icon)}<span class="lbl">${label}</span></button>`;
  const overflow = (items) => `<div class="menu-wrap"><button class="btn btn-quiet btn-icon" aria-haspopup="menu" aria-label="${L('More actions', 'المزيد من الإجراءات')}" ${T(L('More actions', 'المزيد من الإجراءات'))}>${ic('more')}</button><div class="menu" role="menu" hidden>${items.map(([i, t, k]) => `<button role="menuitem">${ic(i)}<span>${t}</span>${k ? kbd(k) : ''}</button>`).join('')}</div></div>`;
  const moreItems = [['merge', L('Merge duplicates', 'دمج المكرر'), ''], ['upload', L('Import from file', 'استيراد من ملف'), ''], ['archive', L('Archive', 'أرشفة'), ''], ['print', L('Print list', 'طباعة القائمة'), 'Ctrl P']];

  // ---- users
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
  const UST = { active: L('Active', 'نشط'), hold: L('Suspended', 'موقوف'), invited: L('Invited', 'مدعو'), locked: L('Locked', 'مقفل') };
  const UCLS = { active: 'active', hold: 'hold', invited: 'info', locked: 'blocked' };
  const usersGrid = () => `<div class="grid-wrap"><table class="grid grid-users" aria-label="${L('Users', 'المستخدمون')}"><thead><tr><th class="c-sel"><input type="checkbox" aria-label="${L('Select all', 'تحديد الكل')}"></th>${[[L('User', 'المستخدم'), 'c-name'], [L('Email', 'البريد'), 'c-email'], [L('Roles', 'الأدوار'), 'c-roles'], [L('Two-step', 'التحقق'), 'c-2fa'], [L('Status', 'الحالة'), 'c-status'], [L('Last sign-in', 'آخر دخول'), 'c-last']].map(([h, k]) => `<th scope="col" class="${k}"><span class="th-in">${h}</span><i class="rz"></i></th>`).join('')}</tr></thead><tbody>${USERS.map((u, i) => `<tr class="row${i === 1 ? ' is-selected' : ''}" tabindex="0"><td class="c-sel"><input type="checkbox"${i === 1 ? ' checked' : ''} aria-label="${L('Select', 'تحديد')}"></td><td class="c-name trunc"><span class="avatar av-person">${ar ? u[1].slice(0, 1) + u[1].split(' ')[1].replace(/^ال/, '').slice(0, 1) : u[0].split(' ').map((w) => w[0]).slice(0, 2).join('')}</span><span class="nm"><b ${T(ar ? u[1] : u[0])}>${ar ? u[1] : u[0]}</b></span></td><td class="trunc" ${T(u[2])}>${num(u[2])}</td><td class="c-roles" ${T(u[3].map((r) => (ar ? ROLE_AR[r] : r)).join(', '))}>${u[3].slice(0, 1).map((r) => `<span class="tag">${ar ? ROLE_AR[r] : r}</span>`).join('')}${u[3].length > 1 ? `<span class="tag more">+${u[3].length - 1}</span>` : ''}</td><td>${u[6] ? `<span class="ok-inline">${ic('shield')}${L('On', 'مفعّل')}</span>` : `<span class="dim">${L('Off', 'غير مفعّل')}</span>`}</td><td><span class="pill pill-${UCLS[u[4]]}"><i class="dot"></i>${UST[u[4]]}</span></td><td class="tnum">${u[5] === '—' ? '<span class="dim">—</span>' : num(u[5])}</td></tr>`).join('')}</tbody></table></div>`;
  const ROLES = [['Owner', 1], ['Accountant', 2], ['Sales manager', 1], ['Sales', 2], ['Warehouse', 2], ['Purchasing', 2], ['Auditor', 2], ['Payroll', 1]];
  const rolesList = () => ROLES.map(([r, n]) => `<li class="role"><span class="role-ic">${ic('shield')}</span><span class="role-nm">${ar ? ROLE_AR[r] : r}</span><span class="role-n tnum">${n}</span></li>`).join('');

  const settingsNav = [[L('Users', 'المستخدمون'), 'user', 1], [L('Roles and permissions', 'الأدوار والصلاحيات'), 'shield'], [L('Sign-in policy', 'سياسة الدخول'), 'lock'], [L('API keys', 'مفاتيح الواجهة'), 'key'], [L('Audit log', 'سجل التدقيق'), 'book']];
  const settingsSubnav = () => settingsNav.map(([t, i, a]) => `<a class="sub-item${a ? ' is-active' : ''}" href="#">${ic(i)}<span>${t}</span></a>`).join('');

  return { ar, L, ln, city, area, money, moneyPre, status, tagsHtml, num, T, ic, gridContacts, detailsPane, open, F, formGeneral, navHtml, switches, kbd, btn, overflow, moreItems, usersGrid, rolesList, settingsSubnav, initials, kindT, DATA };
}

export const BRAND = '../../brand/';
export const headTags = (lang, title, dirName) => `<!doctype html>
<html lang="${lang}" dir="${lang === 'ar' ? 'rtl' : 'ltr'}" data-theme="light" data-density="${dirName === 'a' ? 'compact' : 'comfortable'}">
<head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>${title}</title>
<link rel="icon" type="image/svg+xml" href="${BRAND}bizora-app-icon.svg">
<link rel="stylesheet" href="tokens.css"><link rel="stylesheet" href="style.css">
<script>(function(){try{var q=new URLSearchParams(location.search),h=document.documentElement;if(q.get('theme'))h.dataset.theme=q.get('theme');if(q.get('density'))h.dataset.density=q.get('density');}catch(e){}})();</script>
</head>`;
export const tail = `<script src="../shared/app.js"></script></body></html>`;
export const brandRow = (c, cls = '') => { const p = logoPair(cls); return c.ar ? `${p.ar}<i class="sep"></i>${p.en}` : `${p.en}<i class="sep"></i>${p.ar}`; };
export const logoPair = (cls, o = {}) => {
  // Light-theme file is chosen by tokens (--logo-ink / --logo-white display); both always in the DOM.
  const en = `<span class="logo-en ${cls}"><img class="lg-ink" src="${BRAND}bizora-wordmark.svg" alt="Bizora"><img class="lg-white" src="${BRAND}bizora-wordmark-white.svg" alt="Bizora"></span>`;
  const ar = `<span class="logo-ar ${cls}"><img class="lg-ink" src="${BRAND}bizora-arabic.svg" alt="بيزورا"><img class="lg-white" src="${BRAND}bizora-arabic-white.svg" alt="بيزورا"></span>`;
  return { en, ar };
};
