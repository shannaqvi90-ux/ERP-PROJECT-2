// Direction C layout: floating ink navigation, header band with star lattice, ivory sheets, serif titles.
import { mk, headTags, tail, logoPair, brandRow } from './lib.mjs';
export const id = 'c';
export function pages(lang) {
  const c = mk(lang);
  const { L, ic, num, money } = c;
  const lg = logoPair('');
  const brandStack = () => (c.ar ? `${lg.ar}${lg.en}` : `${lg.en}${lg.ar}`);
  const nav = (active) => `<aside class="navpanel on-dark"><div class="brandblock">${lg.en}${lg.ar}</div>
<label class="navsearch">${ic('search')}<input data-search placeholder="${L('Search…', 'بحث…')}" aria-label="${L('Search', 'بحث')}"><kbd>/</kbd></label>
<nav aria-label="${L('Main navigation', 'التنقل الرئيسي')}">${c.navHtml(active)}</nav>
<div class="navfoot"><span class="avatar av-person">${L('LM', 'ل م')}</span><div><b>${L('Layla Al Mazrouei', 'ليلى المزروعي')}</b><small>${L('Al Wasl Trading LLC', 'الوصل للتجارة ذ.م.م')}</small></div></div></aside>`;
  const facts = (arr) => `<div class="facts">${arr.map(([k, v]) => `<div class="fact"><span class="k">${k}</span><span class="v">${v}</span></div>`).join('')}</div>`;
  const band = (crumb, title, extra, factsArr, primary) => `<header class="band"><div class="bandtop"><div class="crumbs"><span>${crumb[0]}</span>${ic('chevr')}<span>${crumb[1]}</span></div>${c.switches()}</div><div class="titlerow">${title}${extra || ''}<div class="grow">${primary}</div></div>${facts(factsArr)}</header>`;
  const page = (title, body, cls = '') => `${headTags(lang, title, 'c')}<body class="c ${cls}">${body}${tail}`;

  const contacts = () => page(L('Contacts · Bizora ERP', 'جهات الاتصال · بيزورا'), `${nav('contacts')}<div class="stage">
${band([L('Contacts', 'جهات الاتصال'), L('All contacts', 'كل جهات الاتصال')], `<h1>${L('All contacts', 'كل جهات الاتصال')}</h1>`, `<span class="pill pill-active"><i class="dot"></i>${L('Live · 14:32', 'مباشر · 14:32')}</span>`,
    [[L('Contacts', 'جهات الاتصال'), num('100,000')], [L('Customers with balance', 'عملاء لديهم رصيد'), num('1,842')], [L('Receivable', 'الذمم المدينة'), money(482615025, 'AED')], [L('Over credit limit', 'تجاوزوا الحد'), `<span style="color:var(--danger)">${num('37')}</span>`]],
    c.btn('plus', L('New contact', 'جهة اتصال جديدة'), { primary: true, k: 'Alt+N' }))}
<section class="sheet"><div class="ribbon" role="toolbar" aria-label="${L('Commands', 'الأوامر')}">${c.btn('edit', L('Edit', 'تعديل'), { k: 'Enter' })}${c.btn('mail', L('Email', 'بريد'))}${c.btn('trash', L('Delete', 'حذف'), { k: 'Del' })}<i class="cmd-sep"></i>${c.btn('refresh', L('Refresh', 'تحديث'), { k: 'F5' })}${c.btn('download', L('Export', 'تصدير'))}<div class="cmd-end">${c.btn('filter', L('Filter', 'تصفية'))}${c.btn('columns', L('Columns', 'الأعمدة'))}${c.overflow(c.moreItems)}</div></div>
<div class="body"><div class="stack"><div class="filterbar"><span class="chip on">${L('Active', 'نشط')}</span><span class="chip">${L('Customers', 'عملاء')}</span><span class="chip">${L('Vendors', 'موردون')}</span><span class="chip">${L('Free zone', 'منطقة حرة')}</span><span class="chip">${ic('plus')}${L('Filter', 'تصفية')}</span></div>
${c.gridContacts({ cols: ['name', 'city', 'trn', 'status', 'balance'], rows: 30 })}<div class="statusbar"><span>${L('1–30 of', '١–٣٠ من')} ${num('100,000')}</span><span>${L('1 selected', 'محدد: 1')}</span><span style="margin-inline-start:auto">${L('Arrows move · Enter opens · / searches', 'الأسهم للتنقل · Enter للفتح · / للبحث')}</span></div></div>${c.detailsPane()}</div></section></div>`);

  const form = () => {
    const g = c.formGeneral();
    const bal = c.open.balanceCents;
    const sec = (t, ico, items) => `<section class="sec"><h3>${ic(ico)}${t}</h3><div class="sec-grid">${items.join('')}</div></section>`;
    return page(c.ln(c.open) + ' · Bizora ERP', `${nav('contacts')}<div class="stage">
${band([L('Contacts', 'جهات الاتصال'), L('All contacts', 'كل جهات الاتصال')], `<span class="avatar lg av-company">${c.initials(c.open)}</span><h1 title="${c.ln(c.open)}">${c.ln(c.open)}</h1>`, `<span class="pill pill-active"><i class="dot"></i>${L('Active', 'نشط')}</span><span class="tag tag-vip">${L('Export', 'تصدير')}</span><span class="tag">${L('Contractor', 'مقاول')}</span>`,
    [[L('Balance', 'الرصيد'), money(bal, 'AED')], [L('Credit limit', 'حد الائتمان'), money(31000000, 'AED')], [L('Open invoices', 'فواتير مفتوحة'), num('7')], [L('Last order', 'آخر طلب'), num('14/11/2026')], [L('TRN', 'الرقم الضريبي'), num(c.open.trn)]],
    c.btn('save', L('Save', 'حفظ'), { primary: true, k: 'Ctrl+S' }))}
<section class="sheet"><div class="ribbon" role="toolbar">${c.btn('invoice', L('New invoice', 'فاتورة جديدة'))}${c.btn('print', L('Statement', 'كشف حساب'))}${c.btn('mail', L('Email', 'بريد'))}<i class="cmd-sep"></i>${c.btn('chevl', L('Back to list', 'العودة للقائمة'), { k: 'Esc' })}<div class="cmd-end">${c.overflow([['archive', L('Archive', 'أرشفة'), ''], ['merge', L('Merge duplicates', 'دمج المكرر'), ''], ['trash', L('Delete', 'حذف'), 'Del']])}</div></div>
<div class="tabs" role="tablist"><button role="tab" aria-selected="true">${L('General', 'عام')}</button><button role="tab" aria-selected="false">${L('Addresses', 'العناوين')}</button><button role="tab" aria-selected="false">${L('Accounting', 'المحاسبة')}</button><button role="tab" aria-selected="false">${L('Documents', 'المستندات')}</button><button role="tab" aria-selected="false">${L('History', 'السجل')}</button></div>
<div class="formwrap"><div class="formcol"><div class="formscroll"><div class="sections">${sec(L('Identity', 'الهوية'), 'building', g.identity)}${sec(L('Contact and address', 'التواصل والعنوان'), 'pin', g.contact)}${sec(L('Accounting', 'المحاسبة'), 'coins', g.accounting)}</div></div>
<div class="formfoot"><span class="grow">${ic('check')}${L('Saved 14:32 · edited by Layla Al Mazrouei', 'تم الحفظ 14:32 · آخر تعديل: ليلى المزروعي')}</span><button class="btn">${L('Discard', 'تجاهل')}</button></div></div>
<aside class="aside"><div class="block"><h3>${L('Credit', 'الائتمان')}</h3><div class="det-kpis" style="border:0;padding:0"><div><span class="k">${L('Balance', 'الرصيد')}</span>${money(bal, 'AED')}</div><div><span class="k">${L('Limit', 'الحد')}</span>${money(31000000, 'AED')}</div></div><div class="meter"><i style="width:${Math.round(bal / 31000000 * 100)}%"></i></div></div>
<div class="block"><h3>${L('Recent activity', 'آخر النشاطات')}</h3><ul class="activity"><li><i class="adot"></i><div><b>${L('Invoice INV-2026-01842', 'الفاتورة INV-2026-01842')}</b><small>${num('14/11/2026')} · ${money(1862500, 'AED')}</small></div></li><li><i class="adot"></i><div><b>${L('Payment received', 'دفعة مستلمة')}</b><small>${num('09/11/2026')} · ${money(-940000, 'AED')}</small></div></li><li><i class="adot"></i><div><b>${L('Credit limit reviewed', 'مراجعة حد الائتمان')}</b><small>${num('28/10/2026')}</small></div></li></ul></div></aside></div></section></div>`);
  };

  const settings = () => page(L('Users · Bizora ERP', 'المستخدمون · بيزورا'), `${nav('settings')}<div class="stage">
${band([L('Settings', 'الإعدادات'), L('Users and roles', 'المستخدمون والأدوار')], `<h1>${L('Users', 'المستخدمون')}</h1>`, `<span class="pill pill-active"><i class="dot"></i>${L('9 active', '9 نشطون')}</span>`,
    [[L('Seats used', 'المقاعد المستخدمة'), num('12 / 25')], [L('Two-step on', 'التحقق بخطوتين'), num('7 / 12')], [L('Pending invitations', 'دعوات معلّقة'), num('1')], [L('Last audit', 'آخر تدقيق'), num('10/11/2026')]],
    c.btn('plus', L('Invite user', 'دعوة مستخدم'), { primary: true, k: 'Alt+N' }))}
<section class="sheet"><div class="ribbon" role="toolbar">${c.btn('edit', L('Edit roles', 'تعديل الأدوار'))}${c.btn('key', L('Reset password', 'إعادة تعيين كلمة المرور'))}${c.btn('lock', L('Suspend', 'إيقاف'))}<i class="cmd-sep"></i>${c.btn('download', L('Export', 'تصدير'))}<div class="cmd-end">${c.overflow([['mail', L('Resend invitation', 'إعادة إرسال الدعوة'), ''], ['trash', L('Remove user', 'إزالة المستخدم'), 'Del']])}</div></div>
<div class="body"><div class="master"><h3>${L('Roles', 'الأدوار')}</h3><ul>${c.rolesList()}</ul><button class="btn btn-sm" style="margin-top:var(--sp-5)">${ic('plus')}${L('New role', 'دور جديد')}</button></div>
<div class="stack"><div class="filterbar"><span class="chip on">${L('All users', 'كل المستخدمين')}</span><span class="chip">${L('Active', 'نشط')}</span><span class="chip">${L('Invited', 'مدعو')}</span><span class="chip">${L('Locked', 'مقفل')}</span></div>${c.usersGrid()}<div class="statusbar"><span>${L('12 users', '12 مستخدمًا')}</span><span>${L('1 selected', 'محدد: 1')}</span></div></div></div></section></div>`);

  const signin = () => page(L('Sign in · Bizora ERP', 'تسجيل الدخول · بيزورا'), `<main class="signin"><div class="top">${c.switches()}</div><div class="logos">${c.ar ? lg.ar : lg.en}<i class="sep"></i>${c.ar ? lg.en : lg.ar}</div>
<form class="logincard" onsubmit="return false"><h1>${L('Welcome back', 'مرحبًا بعودتك')}</h1><p class="sub">${L('Sign in to Al Wasl Trading', 'سجّل الدخول إلى الوصل للتجارة')}</p>
${c.F.text('s-em', L('Work email', 'بريد العمل'), 'layla@alwasl-trading.example', { ltr: true })}${c.F.text('s-pw', L('Password', 'كلمة المرور'), '••••••••••••', { ltr: true, hint: L('Caps Lock is on', 'مفتاح الحروف الكبيرة مفعّل') })}
<div class="row-between"><label class="chk"><input type="checkbox" checked> ${L('Keep me signed in', 'أبقِني مسجّلًا')}</label><a href="#">${L('Forgot password?', 'نسيت كلمة المرور؟')}</a></div>
<button class="btn btn-primary">${ic('lock')}${L('Sign in', 'دخول')}</button><div class="or">${L('or', 'أو')}</div><button class="btn">${ic('key')}${L('Single sign-on', 'الدخول الموحّد')}</button></form>
<div class="legal">${ic('shield')}${L('Protected by two-step verification · UAE data residency', 'محمي بالتحقق بخطوتين · البيانات داخل الإمارات')}</div></main>`);
  return { contacts: contacts(), form: form(), settings: settings(), signin: signin() };
}
