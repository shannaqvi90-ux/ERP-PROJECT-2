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
    // Sign-ins of the local reference rig (tools/odoo-reference/up.sh); not real credentials.
    users: {
      admin: { login: env.COMPARE_ODOO_ADMIN || 'admin', password: env.COMPARE_ODOO_ADMIN_PASSWORD || 'admin' },
      approver: { login: 'approver', password: 'approver' },
      buyer: { login: 'buyer', password: 'buyer' },
    },
  }),
  ours: Object.freeze({
    id: 'ours',
    baseUrl: (env.COMPARE_OURS_URL || 'http://localhost:8080').replace(/\/$/, ''),
    // Demo sign-ins printed by `./erp up` (local demo data, not real credentials).
    users: {
      admin: { login: env.COMPARE_OURS_ADMIN || 'admin@alnoor.example', password: env.COMPARE_OURS_PASSWORD || env.ERP_DEMO_PASSWORD || 'Demo-Pass-2026' },
      adminArabic: { login: 'admin.ar@alnoor.example', password: env.COMPARE_OURS_PASSWORD || env.ERP_DEMO_PASSWORD || 'Demo-Pass-2026' },
    },
    // Product name words to hide in blind screenshots, once the product has a name.
    brandWords: list(env.COMPARE_OURS_BRAND_WORDS),
  }),
});

export const VIEWPORT = Object.freeze({ width: 1600, height: 900 });
export const LOCALE = 'en-US';
export const TIMEZONE = 'Asia/Dubai';
