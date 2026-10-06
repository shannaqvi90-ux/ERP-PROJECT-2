// Where the two products live and who signs in. Everything can be overridden from the
// environment so critics can point the harness at their own copies.
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const HARNESS_DIR = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
export const REPO_ROOT = path.resolve(HARNESS_DIR, '..', '..');
export const BASELINE_DIR = path.join(REPO_ROOT, 'gauntlet', 'reference', 'odoo');

const env = process.env;
const list = v => (v || '').split(',').map(s => s.trim()).filter(Boolean);

export const PRODUCTS = Object.freeze({
  odoo: Object.freeze({
    id: 'odoo',
    baseUrl: (env.COMPARE_ODOO_URL || 'http://localhost:8069').replace(/\/$/, ''),
    db: env.COMPARE_ODOO_DB || 'reference',
    // Start screens the runner opens itself (lib/start.mjs): the screen after sign-in, and the
    // bookmarked sign-in address (Odoo serves several databases on the rig's port, so it names one).
    homePath: '/odoo',
    // Where the home start may land once loaded: the client's default app (Discuss in Community).
    // A user whose home action was changed in set-up lands elsewhere, and the run is refused.
    homeLanding: /^\/odoo(\/discuss)?\/?$/,
    signInPath: product => `/web/login?db=${encodeURIComponent(product.db)}`,
    // Sign-ins of the local reference rig (tools/odoo-reference/up.sh); not real credentials.
    users: {
      admin: { login: env.COMPARE_ODOO_ADMIN || 'admin', password: env.COMPARE_ODOO_ADMIN_PASSWORD || 'admin' },
      approver: { login: env.COMPARE_ODOO_APPROVER || 'approver', password: env.COMPARE_ODOO_APPROVER_PASSWORD || 'approver' },
      buyer: { login: env.COMPARE_ODOO_BUYER || 'buyer', password: env.COMPARE_ODOO_BUYER_PASSWORD || 'buyer' },
      // A purchase administrator who works in Arabic (print-list-arabic); set-up creates it when missing.
      arabic: { login: env.COMPARE_ODOO_ARABIC || 'arabic.reporter', password: env.COMPARE_ODOO_ARABIC_PASSWORD || 'arabic.reporter' },
    },
  }),
  ours: Object.freeze({
    id: 'ours',
    baseUrl: (env.COMPARE_OURS_URL || 'http://localhost:8080').replace(/\/$/, ''),
    homePath: '/',
    homeLanding: /^\/$/,
    signInPath: '/',
    // The product allows 30 sign-ins a minute per client (Erp:RateLimits:SignInPerMinute). The
    // harness paces every sign-in it makes to this address under that limit and waits out a 429
    // (lib/sign-in-limit.mjs); never inside a measured part.
    signInLimit: Object.freeze({ method: 'POST', path: '/api/auth/sign-in' }),
    // Demo sign-ins printed by `./erp up` (local demo data, not real credentials).
    users: {
      admin: { login: env.COMPARE_OURS_ADMIN || 'admin@alnoor.example', password: env.COMPARE_OURS_PASSWORD || env.ERP_DEMO_PASSWORD || 'Demo-Pass-2026' },
      adminArabic: { login: env.COMPARE_OURS_ADMIN_ARABIC || 'admin.ar@alnoor.example', password: env.COMPARE_OURS_PASSWORD || env.ERP_DEMO_PASSWORD || 'Demo-Pass-2026' },
    },
    // Product name words to hide in blind screenshots, once the product has a name.
    brandWords: list(env.COMPARE_OURS_BRAND_WORDS),
  }),
});

export const VIEWPORT = Object.freeze({ width: 1600, height: 900 });
export const LOCALE = 'en-US';
export const TIMEZONE = 'Asia/Dubai';
