// Shared content for directions D, E and F (stage 1, round 2): everything in lib.mjs plus the
// dashboard, derived contact figures (trend, credit used, ageing), micro charts, the command
// palette, saved views, grouped rows, a status timeline and next-best-action hints.
// Markup carries semantic classes only; each direction styles them with its own tokens.
import { mk, DATA, RATES } from './lib.mjs';
export { DATA, RATES };

// Currencies with three minor digits (accountants in the Gulf expect KWD 1.250, not 1.25).
const DEC = { KWD: 3, BHD: 3, OMR: 3 };
const fmt = (v, d = 2) => v.toLocaleString('en-US', { minimumFractionDigits: d, maximumFractionDigits: d });

// Deterministic pseudo-random numbers from a string seed, so pages rebuild identically.
const rngFrom = (seed) => { let h = 2166136261; for (const ch of seed) { h ^= ch.charCodeAt(0); h = Math.imul(h, 16777619); } return () => { h ^= h << 13; h ^= h >>> 17; h ^= h << 5; return ((h >>> 0) % 10000) / 10000; }; };

export function mk2(lang) {
  const c = mk(lang);
  const { L, ar, ic, T } = c;
  const num = (v) => `<bdi dir="ltr">${v}</bdi>`;
  const esc = (s) => String(s).replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;');

  // Money: amount then currency code, the right minor digits per currency, negatives in brackets,
  // the integer and decimal parts in their own spans (directions may weight them differently).
  const money = (cents, cur, o = {}) => {
    const d = DEC[cur] || 2;
    const v = Math.abs(cents) / 100;
    const s = fmt(v, d);
    const [i, f] = s.split('.');
    const neg = cents < 0;
    const sign = o.sign && cents > 0 ? '+' : '';
    const amt = `${neg ? '(' : sign}<span class="int">${i}</span><span class="dec">.${f}</span>${neg ? ')' : ''}`;
    return `<span class="money${neg ? ' neg' : ''}${cents === 0 ? ' zero' : ''}${o.cls ? ' ' + o.cls : ''}" dir="ltr"${o.title ? ` ${T(o.title)}` : ''}><span class="amt">${amt}</span><span class="cur">${cur}</span></span>`;
  };
  const toBase = (cents, cur) => Math.round(cents * (RATES[cur] || 1));
  const pct = (v, d = 0) => `<bdi dir="ltr" class="tnum">${fmt(v * 100, d)}%</bdi>`;

  // ---- derived figures per contact
  const terms = (r) => [L('30 days', '30 يومًا'), L('45 days', '45 يومًا'), L('60 days', '60 يومًا'), L('Cash', 'نقدًا')][Math.floor(r * 4)];
  const X = {};
  for (const ct of DATA) {
    const r = rngFrom(ct.ref);
    const end = ct.balanceCents / 100;
    const scale = Math.max(Math.abs(end), (ct.creditLimit || 20000) * 0.08, 4000);
    const trend = [];
    let v = end - (r() - 0.35) * scale * 1.2;
    for (let k = 0; k < 11; k++) { trend.push(v); v += (r() - 0.5) * scale * 0.45 + (end - v) * 0.18; }
    trend.push(end);
    const credit = ct.creditLimit ? Math.max(0, end) / ct.creditLimit : null;
    const overdueShare = ct.balanceCents > 0 ? [0, 0, 0.12, 0.3, 0.55][Math.floor(r() * 5)] : 0;
    const overdue = Math.round(ct.balanceCents * overdueShare);
    const oldest = overdue ? 8 + Math.floor(r() * 50) : 0;
    const bal = Math.max(0, ct.balanceCents);
    const a90 = oldest > 45 ? Math.round(overdue * 0.4) : 0;
    const a60 = oldest > 30 ? Math.round((overdue - a90) * 0.5) : 0;
    const a30 = overdue - a90 - a60;
    const day = 1 + Math.floor(r() * 16);
    X[ct.ref] = { trend, credit, overdue, oldest, aging: [bal - overdue, a30, a60, a90], last: `${String(day).padStart(2, '0')}/11/2026`, terms: terms(r()), salesYtd: Math.round((ct.creditLimit || 30000) * (2 + r() * 5)) * 100, orders: 4 + Math.floor(r() * 40) };
  }
  // The open record gets hand-picked figures that tell one coherent story across all screens.
  const OPEN = 'C000003';
  Object.assign(X[OPEN], { overdue: 1842000, oldest: 41, aging: [1282667, 960000, 882000, 0], last: '14/11/2026', terms: L('30 days', '30 يومًا'), salesYtd: 128430000, orders: 37 });
  const open = DATA.find((x) => x.ref === OPEN);

  // ---- micro charts (colour comes from CSS through currentColor)
  const spark = (vals, o = {}) => {
    const w = o.w || 72, h = o.h || 20, p = 2;
    const min = Math.min(...vals), max = Math.max(...vals), span = max - min || 1;
    const pts = vals.map((v, i) => [p + (i * (w - 2 * p)) / (vals.length - 1), h - p - ((v - min) / span) * (h - 2 * p)]);
    const d = pts.map((q, i) => `${i ? 'L' : 'M'}${q[0].toFixed(1)} ${q[1].toFixed(1)}`).join('');
    const last = pts[pts.length - 1];
    const zero = min < 0 && max > 0 ? h - p - ((0 - min) / span) * (h - 2 * p) : null;
    const up = vals[vals.length - 1] >= vals[0];
    return `<svg class="spark ${up ? 'up' : 'down'} ${o.cls || ''}" viewBox="0 0 ${w} ${h}" width="${w}" height="${h}" role="img" aria-label="${esc(o.label || L('12-month trend', 'اتجاه 12 شهرًا'))}">${zero !== null ? `<path class="sp-zero" d="M0 ${zero.toFixed(1)}H${w}"/>` : ''}${o.area ? `<path class="sp-area" d="${d}L${last[0].toFixed(1)} ${h}L${p} ${h}Z"/>` : ''}<path class="sp-line" d="${d}"/><circle class="sp-dot" cx="${last[0].toFixed(1)}" cy="${last[1].toFixed(1)}" r="${o.r || 2}"/></svg>`;
  };
  const bars = (vals, o = {}) => {
    const w = o.w || 96, h = o.h || 28, n = vals.length, gap = o.gap ?? 2, bw = (w - gap * (n - 1)) / n, max = Math.max(...vals) || 1;
    return `<svg class="bars ${o.cls || ''}" viewBox="0 0 ${w} ${h}" width="${w}" height="${h}" role="img" aria-label="${esc(o.label || '')}">${vals.map((v, i) => { const bh = Math.max(1.5, (v / max) * (h - 1)); return `<rect class="${i === n - 1 ? 'b-last' : 'b'}" x="${(i * (bw + gap)).toFixed(1)}" y="${(h - bh).toFixed(1)}" width="${bw.toFixed(1)}" height="${bh.toFixed(1)}" rx="${o.rx ?? 1}"/>`; }).join('')}</svg>`;
  };
  // Credit used: a thin bar with the limit at the end; over the limit it turns to the danger tone.
  const mbar = (ratio, o = {}) => {
    if (ratio === null || ratio === undefined) return `<span class="mbar-none dim" ${T(L('No credit limit set', 'لا يوجد حد ائتماني'))}>—</span>`;
    const p = Math.min(1, ratio);
    const tone = ratio > 1 ? 'over' : ratio > 0.8 ? 'warn' : 'ok';
    return `<span class="mbar-wrap ${tone}" ${T(`${L('Credit used', 'الائتمان المستخدم')} ${fmt(ratio * 100, 0)}%`)}><span class="mbar" role="img" aria-label="${L('Credit used', 'الائتمان المستخدم')} ${fmt(ratio * 100, 0)}%"><i style="width:${(p * 100).toFixed(1)}%"></i></span>${o.label === false ? '' : `<bdi dir="ltr" class="mbar-v tnum">${fmt(ratio * 100, 0)}%</bdi>`}</span>`;
  };
  const AGE_T = [L('Current', 'جارية'), L('1–30 days', '1–30 يومًا'), L('31–60 days', '31–60 يومًا'), L('Over 60 days', 'أكثر من 60 يومًا')];
  const aging = (a, o = {}) => {
    const tot = a.reduce((s, v) => s + v, 0) || 1;
    return `<span class="aging" role="img" aria-label="${L('Ageing', 'أعمار الديون')}">${a.map((v, i) => (v > 0 ? `<i class="ag ag${i}" style="width:${((v / tot) * 100).toFixed(1)}%" ${T(AGE_T[i])}></i>` : '')).join('')}</span>`;
  };

  // ---- dashboard content (Al Wasl Trading LLC, Tuesday 17/11/2026, signed in as Layla)
  const TODAY = { long: L('Tuesday, 17 November 2026', 'الثلاثاء، 17 نوفمبر 2026'), short: '17/11/2026', greet: L('Good afternoon, Layla', 'مساء الخير يا ليلى'), co: L('Al Wasl Trading LLC', 'الوصل للتجارة ذ.م.م') };
  const ACTIONS = [
    { id: 'overdue', icon: 'invoice', tone: 'danger', key: 'R', title: L('Overdue invoices to chase', 'فواتير متأخرة تحتاج متابعة'), count: 14, unit: L('invoices', 'فاتورة'), total: money(18642050, 'AED'), cta: L('Send reminders', 'إرسال التذكيرات'),
      items: [
        [L('Dune Logistics FZCO', 'الكثبان للخدمات اللوجستية ش.م.ح'), 'INV-2026-01733', L('45 days late', 'متأخرة 45 يومًا'), money(1248000, 'USD')],
        [L('Pearl Cables Co.', 'شركة اللؤلؤة للكابلات'), 'INV-2026-01698', L('38 days late', 'متأخرة 38 يومًا'), money(4672447, 'AED')],
        [L('Al Fajr Paints LLC', 'الفجر للدهانات ذ.م.م'), 'INV-2026-01771', L('21 days late', 'متأخرة 21 يومًا'), money(1947225, 'AED')],
      ] },
    { id: 'approvals', icon: 'check', tone: 'accent', key: 'A', title: L('Approvals waiting for you', 'موافقات بانتظارك'), count: 5, unit: L('requests', 'طلبات'), total: money(13254000, 'AED'), cta: L('Review approvals', 'مراجعة الموافقات'),
      items: [
        [L('Purchase order · Gulf Aluminium Est.', 'أمر شراء · مؤسسة الخليج للألمنيوم'), 'PO-2026-0418', L('since 09:12', 'منذ 09:12'), money(8430000, 'AED')],
        [L('Credit limit raise · Horizon Logistics Co.', 'رفع حد الائتمان · شركة الأفق للخدمات اللوجستية'), 'CR-2026-0031', L('62,000 → 90,000', '62,000 ← 90,000'), money(9000000, 'AED')],
        [L('Vendor bill · Star Marine Supplies', 'فاتورة مورد · النجم للتوريدات البحرية'), 'BILL-2026-0932', L('due 20/11/2026', 'تستحق 20/11/2026'), money(124050, 'KWD')],
      ] },
    { id: 'stock', icon: 'box', tone: 'warning', key: 'O', title: L('Stock to reorder', 'مخزون يحتاج إعادة طلب'), count: 8, unit: L('items below reorder point', 'أصناف تحت حد الطلب'), cta: L('Create purchase orders', 'إنشاء أوامر الشراء'),
      items: [
        [L('XLPE cable 4×95 mm²', 'كابل XLPE ‏4×95 مم²'), 'SKU-CBL-495', L('120 m left · reorder at 500 m', 'المتبقي 120 م · الحد 500 م'), 0.24],
        [L('Copper lug 70 mm²', 'طرف نحاسي 70 مم²'), 'SKU-LUG-070', L('340 pcs left · reorder at 1,000', 'المتبقي 340 · الحد 1,000'), 0.34],
        [L('PVC conduit 25 mm', 'أنبوب PVC ‏25 مم'), 'SKU-PVC-025', L('60 lengths left · reorder at 200', 'المتبقي 60 · الحد 200'), 0.3],
      ] },
    { id: 'credit', icon: 'shield', tone: 'danger', key: 'C', title: L('Credit limits breached', 'تجاوز حدود الائتمان'), count: 3, unit: L('customers', 'عملاء'), cta: L('Review credit', 'مراجعة الائتمان'),
      items: [
        [L('Al Fajr Paints LLC', 'الفجر للدهانات ذ.م.م'), 'C000025', L('limit AED 4,000.00', 'الحد 4,000.00 AED'), 4.87],
        [L('Horizon Logistics Co.', 'شركة الأفق للخدمات اللوجستية'), 'C000026', L('order SO-2026-2210 on hold', 'الطلب SO-2026-2210 موقوف'), 1.12],
        [L('Coral Marine Supplies LLC', 'المرجان للتوريدات البحرية ذ.م.م'), 'C000004', L('credit stop since 03/11/2026', 'إيقاف ائتماني منذ 03/11/2026'), 1.04],
      ] },
  ];
  const EMPTY = { icon: 'refresh', title: L('Recurring invoices', 'الفواتير المتكررة'), head: L('Nothing set up yet', 'لا يوجد شيء بعد'), body: L('Bill rent, retainers and service contracts on the same day every month, without retyping them.', 'أصدر فواتير الإيجار والعقود والخدمات في اليوم نفسه من كل شهر دون إعادة إدخالها.'), cta: L('Set up a recurring invoice', 'إعداد فاتورة متكررة'), alt: L('How it works', 'كيف تعمل') };
  const S = (seed, n, base, vol, drift = 0) => { const r = rngFrom(seed); const out = []; let v = base; for (let i = 0; i < n; i++) { out.push(v); v = v * (1 + (r() - 0.5) * vol + drift); } return out; };
  const KPIS = [
    { id: 'ar', label: L('Receivables', 'الذمم المدينة'), value: money(482615025, 'AED'), delta: '+3.1%', dir: 'up', good: false, sub: L('of which overdue', 'منها متأخرة'), subv: money(18642050, 'AED'), series: S('ar', 12, 4300000, 0.06, 0.01) },
    { id: 'sales', label: L('Sales, November to date', 'المبيعات منذ بداية نوفمبر'), value: money(128431640, 'AED'), delta: '+8.4%', dir: 'up', good: true, sub: L('same day in October', 'اليوم نفسه من أكتوبر'), subv: money(118479000, 'AED'), series: S('sales', 12, 1000000, 0.12, 0.02) },
    { id: 'cash', label: L('Cash in bank', 'النقد في البنوك'), value: money(214588010, 'AED'), delta: '−2.6%', dir: 'down', good: false, sub: L('3 bank accounts', '3 حسابات بنكية'), subv: '', series: S('cash', 12, 2300000, 0.05, -0.004) },
    { id: 'ap', label: L('Payables due in 7 days', 'مستحقات الموردين خلال 7 أيام'), value: money(31294000, 'AED'), delta: '11', dir: 'flat', good: null, sub: L('bills', 'فاتورة'), subv: '', series: S('ap', 12, 300000, 0.2) },
    { id: 'margin', label: L('Gross margin, November', 'هامش الربح الإجمالي، نوفمبر'), value: `<span class="money pctv" dir="ltr"><span class="amt"><span class="int">23</span><span class="dec">.6</span></span><span class="cur">%</span></span>`, delta: L('+1.2 pts', '+1.2 نقطة'), dir: 'up', good: true, sub: L('October', 'أكتوبر'), subv: `<bdi dir="ltr" class="tnum">22.4%</bdi>`, series: S('mg', 12, 22, 0.04) },
  ];
  const ACTIVITY = [
    ['14:32', 'invoice', L('Omar Al Suwaidi', 'عمر السويدي'), L('posted invoice', 'رحّل الفاتورة'), 'INV-2026-01842', L('Al Wasl Cables Co.', 'شركة الوصل للكابلات'), money(1862500, 'AED')],
    ['13:58', 'coins', L('Payment received', 'دفعة مستلمة'), L('from', 'من'), 'RCT-2026-0745', L('Coral Paints FZCO', 'المرجان للدهانات ش.م.ح'), money(1049889, 'AED')],
    ['12:10', 'truck', L('Mansour Al Romaithi', 'منصور الرميثي'), L('received goods', 'استلم البضاعة'), 'GRN-2026-0611', L('480 m XLPE cable', '480 م كابل XLPE'), ''],
    ['11:45', 'file', L('Fatima Al Kaabi', 'فاطمة الكعبي'), L('issued credit note', 'أصدرت إشعارًا دائنًا'), 'CN-2026-0077', L('Summit Aluminium Co.', 'شركة القمة للألمنيوم'), money(-245000, 'AED')],
    ['10:20', 'mail', L('Hind Thomas', 'هند توماس'), L('sent quotation', 'أرسلت عرض السعر'), 'QT-2026-0915', L('Atlas Furniture Industries LLC', 'أطلس للأثاث صناعات ذ.م.م'), money(6420000, 'AED')],
    ['09:05', 'lock', L('Fatima Al Kaabi', 'فاطمة الكعبي'), L('closed the period', 'أقفلت الفترة'), L('October 2026', 'أكتوبر 2026'), '', ''],
  ];

  // ---- command palette
  const PALETTE = {
    query: L('al wa', 'الوصل'),
    groups: [
      { t: L('Records', 'السجلات'), items: [
        ['building', L('<mark>Al Wa</mark>sl Cables Co.', 'شركة <mark>الوصل</mark> للكابلات'), 'C000003 · ' + L('Customer · Fujairah', 'عميل · الفجيرة'), '↵', true],
        ['building', L('<mark>Al Wa</mark>sl Chemicals Est.', 'مؤسسة <mark>الوصل</mark> للكيماويات'), 'C000013 · ' + L('Customer · Abu Dhabi', 'عميل · أبوظبي'), ''],
        ['invoice', L('INV-2026-01842 · <mark>Al Wa</mark>sl Cables Co.', 'INV-2026-01842 · <mark>الوصل</mark> للكابلات'), L('Invoice · 18,625.00 AED · posted', 'فاتورة · 18,625.00 AED · مرحّلة'), ''],
      ] },
      { t: L('Actions', 'الإجراءات'), items: [
        ['print', L('Send statement to <mark>Al Wa</mark>sl Cables Co.', 'إرسال كشف حساب إلى <mark>الوصل</mark> للكابلات'), L('3 invoices overdue', '3 فواتير متأخرة'), 'S'],
        ['invoice', L('New invoice for <mark>Al Wa</mark>sl Cables Co.', 'فاتورة جديدة: شركة <mark>الوصل</mark> للكابلات'), '', 'N I'],
        ['coins', L('Record payment', 'تسجيل دفعة'), '', 'P'],
      ] },
      { t: L('Go to', 'انتقال إلى'), items: [
        ['users', L('Contacts', 'جهات الاتصال'), '', 'G C'],
        ['chart', L('Receivables ageing', 'أعمار الذمم المدينة'), '', 'G A'],
      ] },
    ],
  };

  // ---- saved views and grouped rows (F)
  const VIEWS = [[L('All contacts', 'كل جهات الاتصال'), '100,000', true], [L('My customers', 'عملائي'), '412'], [L('Overdue', 'متأخرة'), '14', false, 'danger'], [L('Credit stop', 'إيقاف ائتماني'), '3'], [L('Free zone', 'المناطق الحرة'), '2,318']];
  const groupRows = (list) => {
    const order = ['blocked', 'hold', 'active'];
    return order.map((s) => { const rows = list.filter((x) => x.status === s); const base = rows.reduce((t, x) => t + toBase(x.balanceCents, x.currency), 0); const od = rows.reduce((t, x) => t + toBase(X[x.ref].overdue, x.currency), 0); return { s, rows, base, od }; }).filter((g) => g.rows.length);
  };

  // ---- status timeline of the latest order, and the next best action on the open record
  const TIMELINE = [
    [L('Quoted', 'عرض سعر'), '02/11', 'done'],
    [L('Ordered', 'طلب'), '04/11', 'done'],
    [L('Delivered', 'تسليم'), '10/11', 'done'],
    [L('Invoiced', 'فوترة'), '14/11', 'now'],
    [L('Paid', 'سداد'), L('due 14/12', 'يستحق 14/12'), 'next'],
  ];
  const NBA = { icon: 'warn', title: L('3 invoices overdue', '3 فواتير متأخرة'), body: L('18,420.00 AED in total, the oldest 41 days. A statement usually brings payment within a week.', 'إجماليها 18,420.00 AED وأقدمها منذ 41 يومًا. كشف الحساب يأتي بالسداد غالبًا خلال أسبوع.'), cta: L('Send statement', 'إرسال كشف حساب'), key: 'S', alt: L('Remind me tomorrow', 'ذكّرني غدًا') };

  // ---- contact detail extras
  const INVOICES = [
    ['INV-2026-01842', '14/11/2026', '14/12/2026', money(1862500, 'AED'), 'open', L('Open', 'مفتوحة')],
    ['INV-2026-01790', '06/10/2026', '05/11/2026', money(960000, 'AED'), 'late', L('12 days late', 'متأخرة 12 يومًا')],
    ['INV-2026-01702', '22/09/2026', '22/10/2026', money(498000, 'AED'), 'late', L('26 days late', 'متأخرة 26 يومًا')],
    ['INV-2026-01655', '07/09/2026', '07/10/2026', money(384000, 'AED'), 'late', L('41 days late', 'متأخرة 41 يومًا')],
    ['RCT-2026-0702', '09/11/2026', '', money(-940000, 'AED'), 'paid', L('Payment', 'دفعة')],
  ];
  const PEOPLE = [[L('Mansour Al Romaithi', 'منصور الرميثي'), L('Purchasing manager', 'مدير المشتريات'), '+971 50 412 7781'], [L('Priya Nair', 'بريا ناير'), L('Accounts payable', 'الحسابات الدائنة'), '+971 9 228 4410']];

  // ---- users (extends lib's USERS with seats and permissions)
  const PERMS = { modules: [L('Contacts', 'جهات الاتصال'), L('Sales', 'المبيعات'), L('Purchasing', 'المشتريات'), L('Inventory', 'المخزون'), L('Finance', 'المالية'), L('Settings', 'الإعدادات')], cols: [L('View', 'عرض'), L('Create', 'إنشاء'), L('Approve', 'اعتماد'), L('Delete', 'حذف')],
    accountant: [[1, 1, 0, 0], [1, 1, 1, 0], [1, 1, 0, 0], [1, 0, 0, 0], [1, 1, 1, 0], [0, 0, 0, 0]] };

  // ---- access changes (settings screens) and sign-in policy summary
  const ACCESS = [
    ['17/11/2026 09:14', 'mail', L('Layla Al Mazrouei invited Yousef Al Hammadi', 'دعت ليلى المزروعي يوسف الحمادي'), L('role: Warehouse', 'الدور: مستودع')],
    ['12/11/2026 16:40', 'shield', L('Layla Al Mazrouei gave Fatima Al Kaabi the Auditor role', 'منحت ليلى المزروعي فاطمة الكعبي دور المدقق'), L('2 roles now', 'دوران الآن')],
    ['02/11/2026 11:05', 'lock', L('Layla Al Mazrouei suspended Rashid Al Nuaimi', 'أوقفت ليلى المزروعي راشد النعيمي'), L('left the company', 'غادر الشركة')],
    ['29/10/2026 08:51', 'warn', L('Aisha Al Shamsi was locked out', 'تم قفل حساب عائشة الشامسي'), L('5 failed sign-ins', '5 محاولات دخول فاشلة')],
  ];
  const POLICY = [[L('Two-step verification', 'التحقق بخطوتين'), L('required for Finance and Owner', 'إلزامي للمالية والمالك')], [L('Session timeout', 'مهلة الجلسة'), L('30 minutes idle', '30 دقيقة دون نشاط')], [L('Sign-in from', 'الدخول من'), L('UAE and GCC only', 'الإمارات ودول الخليج فقط')]];

  // ---- contact form fields (Latin digits throughout, Arabic address in Arabic, the error on one field)
  const F = c.F;
  const dateF = (id, label, v, o = {}) => `<div class="fld${o.warn ? ' has-warn' : ''}"><label for="${id}">${label}</label><div class="ctl"><input id="${id}" dir="ltr" class="num" value="${v}" placeholder="dd/mm/yyyy" inputmode="numeric"><button class="in-btn" aria-label="${L('Pick a date', 'اختيار تاريخ')}">${ic('calendar')}</button></div><div class="hint${o.warn ? ' warn' : ''}">${o.warn ? ic('warn') : ''}${o.hint || L('dd/mm/yyyy', 'يوم/شهر/سنة')}</div></div>`;
  const moneyF = (id, label, amount, cur) => `<div class="fld"><label for="${id}">${label}</label><div class="ctl money-in"><span class="cur-chip" tabindex="0" role="button" aria-label="${L('Currency', 'العملة')} ${cur}" ${T(L('Change currency', 'تغيير العملة'))}>${cur}${ic('chevd')}</span><input id="${id}" dir="ltr" class="num" value="${fmt(amount, DEC[cur] || 2)}" inputmode="decimal"></div><div class="hint tnum">${cur === 'AED' ? L('Base currency', 'العملة الأساسية') : `<bdi dir="ltr">≈ ${fmt(amount * RATES[cur])} AED</bdi> ${L('at', 'بسعر')} <bdi dir="ltr">${RATES[cur].toFixed(4)}</bdi>`}</div></div>`;
  const errEmail = `<div class="fld has-err"><label for="f-acc-em">${L('Accounts email', 'بريد الحسابات')}<span class="req" aria-hidden="true">*</span></label><div class="ctl"><input id="f-acc-em" dir="ltr" value="accounts@alwasl3" aria-invalid="true" aria-describedby="f-acc-em-e"><span class="err-mark">${ic('warn')}</span></div><div class="hint err" id="f-acc-em-e">${L('Add the domain, e.g. accounts@alwasl3.ae', 'أضف النطاق، مثل accounts@alwasl3.ae')}</div></div>`;
  const form2 = (ct = open) => ({
    identity: [
      F.text('f-name', L('Name', 'الاسم'), ct.name, { req: true, wide: true, ltr: true }),
      F.text('f-ref', L('Reference', 'الرقم المرجعي'), ct.ref, { ro: true, ltr: true }),
      F.text('f-name-ar', L('Name in Arabic', 'الاسم بالعربية'), ct.nameAr, { wide: true, rtl: true }),
      F.select('f-type', L('Type', 'النوع'), c.kindT(ct.kind), { req: true }),
      F.text('f-trn', L('Tax registration number (TRN)', 'رقم التسجيل الضريبي'), ct.trn, { ltr: true, ok: true, hint: L('15 digits, checked', '15 رقمًا، تم التحقق') }),
      F.text('f-lic', L('Trade licence no.', 'رقم الرخصة التجارية'), ct.licence, { ltr: true }),
      dateF('f-licexp', L('Licence expiry', 'انتهاء الرخصة'), '31/12/2026', { warn: true, hint: L('Expires in 44 days', 'تنتهي بعد 44 يومًا') }),
    ],
    accounting: [
      moneyF('f-credit', L('Credit limit', 'حد الائتمان'), ct.creditLimit, ct.currency),
      F.select('f-terms', L('Payment terms', 'شروط الدفع'), L('30 days from invoice', '30 يومًا من تاريخ الفاتورة')),
      F.select('f-cur', L('Invoice currency', 'عملة الفواتير'), L('AED, UAE dirham', 'AED، الدرهم الإماراتي')),
      errEmail,
      F.select('f-vat', L('VAT treatment', 'المعالجة الضريبية'), L('Standard rate, 5%', 'النسبة القياسية \u20665%\u2069')),
      F.select('f-acct', L('Receivable account', 'حساب الذمم المدينة'), '1210 · ' + L('Trade receivables', 'ذمم تجارية مدينة'), { look: true }),
      moneyF('f-avg', L('Average order value', 'متوسط قيمة الطلب'), 12480, 'USD'),
      dateF('f-since', L('Customer since', 'عميل منذ'), '14/03/2022'),
    ],
    contact: [
      F.text('f-email', L('Email', 'البريد الإلكتروني'), ct.email, { ltr: true }),
      F.text('f-phone', L('Phone', 'الهاتف'), ct.phone, { ltr: true }),
      F.text('f-web', L('Website', 'الموقع'), 'https://www.alwaslcables3.example', { ltr: true }),
      F.text('f-street', L('Street', 'الشارع'), L('97 Road 20', '97 شارع 20'), { wide: true }),
      F.text('f-area', L('Area', 'المنطقة'), c.area(ct.area)),
      F.select('f-city', L('City', 'المدينة'), c.city(ct.city), { look: true }),
    ],
  });

  return { ...c, ACCESS, POLICY, form2, dateF, num, esc, money, toBase, pct, X, open, OPEN, spark, bars, mbar, aging, AGE_T, TODAY, ACTIONS, EMPTY, KPIS, ACTIVITY, PALETTE, VIEWS, groupRows, TIMELINE, NBA, INVOICES, PEOPLE, PERMS, fmt };
}

// Page head for D, E, F: same contract as lib.headTags, plus the direction's default theme and density.
export const BRAND = '../../brand/';
export const head2 = (lang, title, o = {}) => `<!doctype html>
<html lang="${lang}" dir="${lang === 'ar' ? 'rtl' : 'ltr'}" data-theme="${o.theme || 'light'}" data-theme-default="${o.theme || 'light'}" data-density="${o.density || 'compact'}" data-density-default="${o.density || 'compact'}">
<head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>${title}</title>
<link rel="icon" type="image/svg+xml" href="${BRAND}bizora-app-icon.svg">
<link rel="stylesheet" href="tokens.css"><link rel="stylesheet" href="style.css">
<script>(function(){try{var q=new URLSearchParams(location.search),h=document.documentElement;if(q.get('theme'))h.dataset.theme=q.get('theme');if(q.get('density'))h.dataset.density=q.get('density');if(q.get('palette'))h.dataset.palette='open';}catch(e){}})();</script>
</head>`;
export const tail2 = `<script src="../shared/app.js"></script></body></html>`;
export const logos = () => ({
  en: `<span class="logo-en"><img class="lg-ink" src="${BRAND}bizora-wordmark.svg" alt="Bizora"><img class="lg-white" src="${BRAND}bizora-wordmark-white.svg" alt="Bizora"></span>`,
  ar: `<span class="logo-ar"><img class="lg-ink" src="${BRAND}bizora-arabic.svg" alt="بيزورا"><img class="lg-white" src="${BRAND}bizora-arabic-white.svg" alt="بيزورا"></span>`,
  icon: `<img class="app-icon" src="${BRAND}bizora-app-icon.svg" alt="">`,
});
