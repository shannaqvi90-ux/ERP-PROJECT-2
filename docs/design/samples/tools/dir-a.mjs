// Direction A layout: top app bar on ink, tree navigation, command bar, factbox / details pane (Fluent manner).
import { mk, headTags, tail, logoPair, brandRow } from './lib.mjs';
export const id = 'a';
export function pages(lang) {
  const c = mk(lang);
  const { L, ic, num, money } = c;
  const lg = logoPair('');
  const appbar = (o = {}) => `<header class="appbar on-dark"><button class="icon-btn" data-nav-toggle aria-label="${L('Toggle navigation', 'إظهار القائمة')}" title="${L('Toggle navigation', 'إظهار القائمة')}">${ic('sidebar')}</button>
<a class="brand" href="#" aria-label="Bizora">${brandRow(c)}</a>
<label class="search">${ic('search')}<input data-search placeholder="${L('Search contacts, invoices, TRN…', 'ابحث عن جهة اتصال أو فاتورة أو رقم ضريبي…')}" aria-label="${L('Search', 'بحث')}"><kbd>/</kbd></label>
<div class="appbar-end">${c.switches()}<button class="icon-btn" aria-label="${L('Notifications', 'الإشعارات')}">${ic('bell')}</button><div class="me"><span class="avatar av-person">${L('LM', 'ل م')}</span></div></div></header>`;
  const nav = (active) => `<nav class="nav" aria-label="${L('Main navigation', 'التنقل الرئيسي')}">${c.navHtml(active)}<div class="nav-foot"><button data-nav-toggle>${ic('sidebar')}<span>${L('Collapse navigation', 'طيّ القائمة')}</span></button></div></nav>`;
  const facts = (arr) => `<div class="facts">${arr.map(([k, v]) => `<div class="fact"><span class="k">${k}</span><span class="v">${v}</span></div>`).join('')}</div>`;
  const page = (name, title, body) => `${headTags(lang, title, 'a')}<body class="a">${body}${tail}`;

  const contacts = () => {
    const body = `${appbar()}<div class="shell">${nav('contacts')}<main class="main">
<div class="cmdbar" role="toolbar" aria-label="${L('Commands', 'الأوامر')}">${c.btn('plus', L('New contact', 'جهة اتصال جديدة'), { primary: true, k: 'Alt+N' })}${c.btn('edit', L('Edit', 'تعديل'), { quiet: true, k: 'Enter' })}${c.btn('mail', L('Email', 'بريد'), { quiet: true })}${c.btn('trash', L('Delete', 'حذف'), { quiet: true, k: 'Del' })}<i class="cmd-sep"></i>${c.btn('refresh', L('Refresh', 'تحديث'), { quiet: true, k: 'F5' })}${c.btn('download', L('Export', 'تصدير'), { quiet: true })}${c.overflow(c.moreItems)}
<div class="cmd-end">${c.btn('filter', L('Filter', 'تصفية'), { quiet: true })}${c.btn('columns', L('Columns', 'الأعمدة'), { quiet: true })}</div></div>
<div class="pagehead"><div class="row1"><span class="view-pick">${L('All contacts', 'كل جهات الاتصال')}${ic('chevd')}</span><span class="pill pill-active"><i class="dot"></i>${L('Synced 14:32', 'تمت المزامنة 14:32')}</span></div>
${facts([[L('Contacts', 'جهات الاتصال'), num('100,000')], [L('Customers with balance', 'عملاء لديهم رصيد'), num('1,842')], [L('Receivable', 'الذمم المدينة'), money(482615025, 'AED')], [L('Over credit limit', 'تجاوزوا حد الائتمان'), `<span style="color:var(--danger)">${num('37')}</span>`]])}</div>
<div class="content"><div class="stack"><div class="filterbar"><span class="chip on">${L('Active', 'نشط')}</span><span class="chip">${L('Customers', 'عملاء')}</span><span class="chip">${L('Vendors', 'موردون')}</span><span class="chip">${L('Free zone', 'منطقة حرة')}</span><span class="chip">${ic('plus')}${L('Add filter', 'إضافة تصفية')}</span></div>
${c.gridContacts({ cols: ['name', 'trn', 'city', 'tags', 'status', 'balance'], rows: 30 })}
<div class="statusbar"><span>${L('1–30 of', '١–٣٠ من')} ${num('100,000')}</span><span>${L('1 selected', 'محدد: 1')}</span><span style="margin-inline-start:auto">${L('Arrow keys move, Enter opens, / searches', 'الأسهم للتنقل، Enter للفتح، / للبحث')}</span></div></div>
${c.detailsPane()}</div></main></div>`;
    return page('contacts', L('Contacts · Bizora ERP', 'جهات الاتصال · بيزورا'), body);
  };

  const form = () => {
    const g = c.formGeneral();
    const sec = (t, ico, items) => `<section class="sec"><h3>${ic(ico)}${t}</h3><div class="sec-grid">${items.join('')}</div></section>`;
    const body = `${appbar()}<div class="shell">${nav('contacts')}<main class="main">
<div class="cmdbar" role="toolbar">${c.btn('save', L('Save', 'حفظ'), { primary: true, k: 'Ctrl+S' })}${c.btn('check', L('Save & close', 'حفظ وإغلاق'), { quiet: true })}${c.btn('plus', L('New', 'جديد'), { quiet: true })}<i class="cmd-sep"></i>${c.btn('invoice', L('New invoice', 'فاتورة جديدة'), { quiet: true })}${c.btn('print', L('Statement', 'كشف حساب'), { quiet: true })}${c.btn('mail', L('Email', 'بريد'), { quiet: true })}${c.overflow([['archive', L('Archive', 'أرشفة'), ''], ['trash', L('Delete', 'حذف'), 'Del'], ['merge', L('Merge duplicates', 'دمج المكرر'), '']])}<div class="cmd-end">${c.btn('chevl', L('Back to list', 'العودة للقائمة'), { quiet: true, k: 'Esc' })}</div></div>
<div class="pagehead"><div class="row1"><span class="avatar lg av-company">${c.initials(c.open)}</span><div><h1>${c.ln(c.open)}</h1></div><span class="pill pill-active"><i class="dot"></i>${L('Active', 'نشط')}</span><span class="tag tag-vip">${L('Export', 'تصدير')}</span><span class="tag">${L('Contractor', 'مقاول')}</span></div>
${facts([[L('Balance', 'الرصيد'), money(c.open.balanceCents, 'AED')], [L('Credit limit', 'حد الائتمان'), money(31000000, 'AED')], [L('Open invoices', 'فواتير مفتوحة'), num('7')], [L('Last order', 'آخر طلب'), num('14/11/2026')], [L('TRN', 'الرقم الضريبي'), num(c.open.trn)]])}</div>
<div class="formwrap"><div class="formcol"><div class="tabs" role="tablist"><button role="tab" aria-selected="true">${L('General', 'عام')}</button><button role="tab" aria-selected="false">${L('Addresses', 'العناوين')}</button><button role="tab" aria-selected="false">${L('Accounting', 'المحاسبة')}</button><button role="tab" aria-selected="false">${L('Documents', 'المستندات')} <span class="tag">3</span></button><button role="tab" aria-selected="false">${L('History', 'السجل')}</button></div>
<div class="sections">${sec(L('Identity', 'الهوية'), 'building', g.identity)}${sec(L('Contact and address', 'التواصل والعنوان'), 'pin', g.contact)}${sec(L('Accounting', 'المحاسبة'), 'coins', g.accounting)}</div></div>
<aside class="factbox"><div class="block"><h3>${L('Credit', 'الائتمان')}</h3><div class="det-kpis" style="border:0;padding:0"><div><span class="k">${L('Balance', 'الرصيد')}</span>${money(c.open.balanceCents, 'AED')}</div><div><span class="k">${L('Limit', 'الحد')}</span>${money(31000000, 'AED')}</div></div><div class="meter"><i style="width:${Math.round(Math.abs(c.open.balanceCents)/31000000*100)}%"></i></div></div>
<div class="block"><h3>${L('Recent activity', 'آخر النشاطات')}</h3><ul class="activity" style="padding:0"><li><i class="adot"></i><div><b>${L('Invoice INV-2026-01842', 'الفاتورة INV-2026-01842')}</b><small>${num('14/11/2026')} · ${money(1862500, 'AED')}</small></div></li><li><i class="adot"></i><div><b>${L('Payment received', 'دفعة مستلمة')}</b><small>${num('09/11/2026')} · ${money(-940000, 'AED')}</small></div></li><li><i class="adot"></i><div><b>${L('Credit limit reviewed', 'مراجعة حد الائتمان')}</b><small>${num('28/10/2026')}</small></div></li></ul></div>
<div class="block" style="border:0"><h3>${L('Contacts', 'جهات الاتصال')}</h3><div class="nm"><span class="avatar av-person">${c.ar ? 'من' : 'ML'}</span> ${c.ar ? 'منصور الرميثي' : 'Mansour Al Romaithi'}</div></div></aside></div>
<div class="formfoot"><span class="grow">${ic('check')} ${L('All changes saved 14:32 · Edited by Layla Al Mazrouei', 'تم حفظ كل التغييرات 14:32 · آخر تعديل: ليلى المزروعي')}</span><button class="btn">${L('Discard', 'تجاهل')}</button><button class="btn btn-primary">${ic('save')}${L('Save', 'حفظ')}</button></div></main></div>`;
    return page('form', c.ln(c.open) + ' · Bizora ERP', body);
  };

  const settings = () => {
    const body = `${appbar()}<div class="shell">${nav('settings')}<main class="main">
<div class="cmdbar" role="toolbar">${c.btn('plus', L('Invite user', 'دعوة مستخدم'), { primary: true, k: 'Alt+N' })}${c.btn('edit', L('Edit roles', 'تعديل الأدوار'), { quiet: true })}${c.btn('key', L('Reset password', 'إعادة تعيين كلمة المرور'), { quiet: true })}${c.btn('lock', L('Suspend', 'إيقاف'), { quiet: true })}<i class="cmd-sep"></i>${c.btn('download', L('Export', 'تصدير'), { quiet: true })}${c.overflow([['mail', L('Resend invitation', 'إعادة إرسال الدعوة'), ''], ['trash', L('Remove user', 'إزالة المستخدم'), 'Del']])}</div>
<div class="pagehead"><div class="row1"><h1>${L('Users', 'المستخدمون')}</h1><span class="pill pill-active"><i class="dot"></i>${L('9 active', '9 نشطون')}</span></div>${facts([[L('Seats used', 'المقاعد المستخدمة'), num('12 / 25')], [L('Two-step on', 'التحقق بخطوتين'), num('7 / 12')], [L('Pending invitations', 'دعوات معلّقة'), num('1')], [L('Last audit', 'آخر تدقيق'), num('10/11/2026')]])}</div>
<div class="content"><div class="stack"><div class="tabs" role="tablist"><button role="tab" aria-selected="true">${L('Users', 'المستخدمون')}</button><button role="tab" aria-selected="false">${L('Roles and permissions', 'الأدوار والصلاحيات')}</button><button role="tab" aria-selected="false">${L('Sign-in policy', 'سياسة الدخول')}</button><button role="tab" aria-selected="false">${L('API keys', 'مفاتيح الواجهة')}</button><button role="tab" aria-selected="false">${L('Audit log', 'سجل التدقيق')}</button></div><div class="filterbar"><span class="chip on">${L('All users', 'كل المستخدمين')}</span><span class="chip">${L('Active', 'نشط')}</span><span class="chip">${L('Invited', 'مدعو')}</span><span class="chip">${L('Locked', 'مقفل')}</span></div>${c.usersGrid()}<div class="statusbar"><span>${L('12 users', '12 مستخدمًا')}</span><span>${L('1 selected', 'محدد: 1')}</span></div></div>
<aside class="roles"><h3>${L('Roles', 'الأدوار')}</h3><ul>${c.rolesList()}</ul><button class="btn btn-sm" style="margin-top:var(--sp-5)">${ic('plus')}${L('New role', 'دور جديد')}</button></aside></div></main></div>`;
    return page('settings', L('Users · Bizora ERP', 'المستخدمون · بيزورا'), body);
  };

  const signin = () => {
    const body = `<header class="appbar on-dark"><a class="brand" href="#">${brandRow(c)}</a><div class="appbar-end">${c.switches()}</div></header>
<main class="signin-bg"><form class="card" onsubmit="return false"><div class="logos">${brandRow(c)}</div><h1>${L('Sign in', 'تسجيل الدخول')}</h1><p class="sub">${L('Use your work email to open your company.', 'استخدم بريد العمل لفتح حساب شركتك.')}</p>
${c.F.text('s-co', L('Company code', 'رمز الشركة'), 'alwasl-trading', { ltr: true })}${c.F.text('s-em', L('Email', 'البريد الإلكتروني'), 'layla@alwasl-trading.example', { ltr: true })}${c.F.text('s-pw', L('Password', 'كلمة المرور'), '••••••••••••', { ltr: true, hint: L('Caps Lock is on', 'مفتاح الحروف الكبيرة مفعّل') })}
<div class="row-between"><label class="chk"><input type="checkbox" checked> ${L('Keep me signed in on this device', 'أبقِني مسجّلًا على هذا الجهاز')}</label><a href="#">${L('Forgot password?', 'نسيت كلمة المرور؟')}</a></div>
<button class="btn btn-primary">${ic('lock')}${L('Sign in', 'دخول')}</button><div class="or">${L('or', 'أو')}</div><button class="btn">${ic('key')}${L('Continue with company single sign-on', 'المتابعة عبر الدخول الموحّد للشركة')}</button></form></main>
<footer class="bottombar"><span>${L('Protected by two-step verification · UAE data residency', 'محمي بالتحقق بخطوتين · بيانات مقيمة في الإمارات')}</span><span>Bizora ERP</span></footer>`;
    return page('signin', L('Sign in · Bizora ERP', 'تسجيل الدخول · بيزورا'), body);
  };
  return { contacts: contacts(), form: form(), settings: settings(), signin: signin() };
}
