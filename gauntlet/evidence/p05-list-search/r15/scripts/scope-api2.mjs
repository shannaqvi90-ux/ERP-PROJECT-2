import { api } from './pw.mjs';
const A = await api(process.argv[2], 'admin@alnoor.example');
const t = async f => (await A.get('/api/identity/users?take=1&filter=' + encodeURIComponent(f))).body.total;
console.log('isActive eq false', await t('isActive eq false'), '| language eq ar', await t("language eq 'ar'"), '| ar and inactive', await t("language eq 'ar' and isActive eq false"), '| ar or inactive', await t("language eq 'ar' or isActive eq false"), '| en or ar', await t("language eq 'en' or language eq 'ar'"));
const g = (await A.get('/api/identity/roles?take=5&groupBy=isSystem')).body;
console.log('roles groupBy isSystem', JSON.stringify(g.groups), 'totals', JSON.stringify(g.totals ?? null));
