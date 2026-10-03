# Reference rig seed for Odoo Community. Runs inside `odoo shell` (see up.sh), where `env` exists.
#
# Idempotent: every step works out what is missing and adds only that, so running it again
# tops lists back up (Odoo's own vacuum deletes cron progress rows older than a week).
#
# Small configuration records go through the ORM. Volume (100,000 rows per list) is loaded
# with set-based SQL that clones one ORM-created template row per list, so every column Odoo
# expects is filled the way Odoo itself fills it, and loading takes minutes instead of hours.
# Contacts, users and exchange rates come from the shared dataset (gauntlet/compare/data/),
# so our product is measured on exactly the same records.
import csv
import json
import os
import time
from decimal import Decimal

from odoo import Command
from psycopg2.extras import execute_values

TARGET = int(os.environ.get('RIG_TARGET', '100000'))
DATA = os.environ.get('RIG_DATA', '/data')
LOG_PREFIX = 'RIG'

env = env(context=dict(env.context, lang='en_US', tz='Asia/Dubai', tracking_disable=True,  # noqa: F821 (env from odoo shell)
                       mail_create_nolog=True, mail_notrack=True, no_reset_password=True))
cr = env.cr


def log(msg):
    print(f'{LOG_PREFIX} {msg}', flush=True)


def timed(name):
    def deco(fn):
        def run():
            t = time.time()
            added = fn()
            env.flush_all()
            cr.commit()
            log(f'step {name}: +{added} in {time.time() - t:.1f}s')
        return run
    return deco


def columns(table, exclude):
    cr.execute("""SELECT column_name FROM information_schema.columns
                  WHERE table_schema = 'public' AND table_name = %s ORDER BY ordinal_position""", [table])
    return [c for (c,) in cr.fetchall() if c not in exclude]


def clone_sql(table, template_id, overrides, from_clause, where=''):
    """INSERT INTO table: every column copied from the template row except `overrides`,
    which are SQL expressions over `from_clause` (aliases available: t = template)."""
    keep = columns(table, set(overrides) | {'id'})
    cols = keep + list(overrides)
    select = [f't."{c}"' for c in keep] + [overrides[c] for c in overrides]
    sql = (f'INSERT INTO "{table}" ({", ".join(chr(34) + c + chr(34) for c in cols)}) '
           f'SELECT {", ".join(select)} FROM {from_clause} '
           f'CROSS JOIN (SELECT * FROM "{table}" WHERE id = {int(template_id)}) t {where}')
    cr.execute(sql)
    return cr.rowcount


def commit_template():
    """Commit the ORM-made template row before bulk cloning. Odoo builds bus notification
    payloads lazily at commit time; building them after 100,000 cloned rows exist exhausts memory."""
    env.flush_all()
    cr.commit()


def read_csv(name):
    with open(os.path.join(DATA, name), newline='', encoding='utf-8') as f:
        return list(csv.DictReader(f))


def xmlid_record(xmlid, model, values):
    rec = env.ref(xmlid, raise_if_not_found=False)
    if rec:
        return rec
    rec = env[model].create(values)
    module, name = xmlid.split('.')
    env['ir.model.data'].create({'module': module, 'name': name, 'model': model, 'res_id': rec.id, 'noupdate': True})
    return rec


@timed('setup')
def setup():
    # Installing with --load-language=ar_001 leaves Arabic as the only active language and the
    # default for new partners; the reference works in English and switches to Arabic on demand.
    env['res.lang']._activate_lang('en_US')
    env['res.lang']._activate_lang('ar_001')
    env['ir.default'].set('res.partner', 'lang', 'en_US')
    company = env.ref('base.main_company')
    aed = env.ref('base.AED')
    aed.active = True
    company.write({
        'name': 'Demo Trading LLC', 'country_id': env.ref('base.ae').id, 'city': 'Dubai',
        'state_id': env.ref('base.state_ae_du', raise_if_not_found=False).id if env.ref('base.state_ae_du', raise_if_not_found=False) else False,
        'email': 'info@demo-trading.example', 'phone': '+971 4 000 0000',
        # Purchase two-step approval: the nearest Community feature to a generic approval flow.
        'po_double_validation': 'two_step', 'po_double_validation_amount': 5000,
    })
    if company.currency_id != aed:
        company.currency_id = aed
    # Currencies used by the shared rate dataset.
    codes = sorted({r['currency'] for r in read_csv('rates.csv')})
    env['res.currency'].with_context(active_test=False).search([('name', 'in', codes)]).write({'active': True})
    # Branches and a second company (Odoo models branches as child companies).
    xmlid_record('reference_rig.branch_ad', 'res.company', {'name': 'Demo Trading LLC - Abu Dhabi Branch', 'parent_id': company.id})
    xmlid_record('reference_rig.branch_sh', 'res.company', {'name': 'Demo Trading LLC - Sharjah Branch', 'parent_id': company.id})
    second = xmlid_record('reference_rig.company_mfg', 'res.company', {'name': 'Demo Manufacturing FZE', 'currency_id': aed.id, 'country_id': env.ref('base.ae').id})
    xmlid_record('reference_rig.branch_mfg_rak', 'res.company', {'name': 'Demo Manufacturing FZE - RAK Branch', 'parent_id': second.id})
    for tag in ['Supplier', 'Customer', 'Distributor', 'Contractor', 'Government', 'VIP', 'Export', 'Retail']:
        if not env['res.partner.category'].search([('name', '=', tag)], limit=1):
            env['res.partner.category'].create({'name': tag})
    admin = env.ref('base.user_admin')
    all_companies = env['res.company'].search([])
    admin.write({'lang': 'en_US', 'tz': 'Asia/Dubai', 'company_ids': [Command.set(all_companies.ids)], 'company_id': company.id})
    admin.partner_id.write({'name': 'Administrator', 'email': 'admin@demo-trading.example'})
    purchase_user = env.ref('purchase.group_purchase_user')
    purchase_manager = env.ref('purchase.group_purchase_manager')
    internal = env.ref('base.group_user')
    for login, name, group in [('approver', 'Amal Approver', purchase_manager), ('buyer', 'Bilal Buyer', purchase_user)]:
        user = env['res.users'].with_context(active_test=False).search([('login', '=', login)], limit=1)
        if not user:
            user = env['res.users'].create({'name': name, 'login': login, 'email': f'{login}@demo-trading.example'})
        user.write({'active': True, 'password': login, 'lang': 'en_US', 'tz': 'Asia/Dubai'})
        # Only when they differ: rewriting groups makes Odoo re-check channel subscriptions.
        if set(user.group_ids.ids) != {internal.id, group.id}:
            user.write({'group_ids': [Command.set([internal.id, group.id])]})
    xmlid_record('reference_rig.product_chair', 'product.product', {
        'name': 'Office Chair', 'purchase_ok': True, 'standard_price': 750.0, 'list_price': 990.0, 'supplier_taxes_id': [Command.clear()],
    })
    # No onboarding tours or bot conversations in the measured screens.
    users = env['res.users'].search([('login', 'in', ['admin', 'approver', 'buyer'])])
    if 'tour_enabled' in users._fields:
        users.write({'tour_enabled': False})
    if 'odoobot_state' in users._fields:
        users.write({'odoobot_state': 'disabled'})
    return 1


@timed('contacts')
def contacts():
    rows = read_csv('contacts.csv')[:TARGET]
    cr.execute("SELECT ref FROM res_partner WHERE ref ~ '^C[0-9]{6}$'")
    have = {r for (r,) in cr.fetchall()}
    rows = [r for r in rows if r['ref'] not in have]
    if not rows:
        return 0
    states = {s.code: s.id for s in env['res.country.state'].search([('country_id.code', '=', 'AE')])}
    countries = {c.code: c.id for c in env['res.country'].search([])}
    template = env['res.partner'].create({'name': 'RIG TEMPLATE', 'is_company': True, 'lang': 'en_US', 'country_id': countries['AE']})
    commit_template()
    cr.execute("""CREATE TEMP TABLE rig_contacts (ref text, kind text, name text, parent_ref text, email text, phone text,
                  street text, street2 text, city text, state_id int, country_id int, website text, vat text, tags text, n int)
                  ON COMMIT DROP""")
    execute_values(cr._obj, 'INSERT INTO rig_contacts VALUES %s', [(
        r['ref'], r['kind'], r['name'], r['parent_ref'], r['email'], r['phone'] or r['mobile'],
        r['street'], ', '.join(x for x in [r['area'], f"PO Box {r['po_box']}" if r['po_box'] else ''] if x), r['city'],
        states.get(r['emirate_code']), countries.get(r['country_code']), r['website'] or None, r['trn'] or None, r['tags'],
        int(r['ref'][1:]),
    ) for r in rows], page_size=5000)
    added = clone_sql('res_partner', template.id, {
        'name': 'c.name', 'complete_name': 'c.name', 'ref': 'c.ref', 'email': 'c.email',
        'email_normalized': 'lower(c.email)', 'phone': 'c.phone',
        'phone_sanitized': "regexp_replace(c.phone, '[^+0-9]', '', 'g')",
        'street': 'c.street', 'street2': 'NULLIF(c.street2, \'\')', 'city': 'c.city', 'state_id': 'c.state_id',
        'country_id': 'c.country_id', 'website': 'c.website', 'vat': 'c.vat',
        'is_company': "c.kind = 'company'", 'commercial_company_name': "CASE WHEN c.kind = 'company' THEN c.name END",
        'create_date': "now() - (c.n || ' minutes')::interval", 'write_date': "now() - (c.n || ' minutes')::interval",
    }, 'rig_contacts c', 'ORDER BY c.n')
    cr.execute("""UPDATE res_partner p SET commercial_partner_id = p.id
                  FROM rig_contacts c WHERE p.ref = c.ref""")
    cr.execute("""UPDATE res_partner p SET parent_id = par.id, commercial_partner_id = par.id,
                         complete_name = par.name || ', ' || p.name, commercial_company_name = par.name
                  FROM rig_contacts c JOIN res_partner par ON par.ref = c.parent_ref
                  WHERE p.ref = c.ref AND c.parent_ref <> ''""")
    cr.execute("""INSERT INTO res_partner_res_partner_category_rel (category_id, partner_id)
                  SELECT DISTINCT cat.id, p.id FROM rig_contacts c
                  CROSS JOIN LATERAL unnest(string_to_array(NULLIF(c.tags, ''), ';')) AS tag(name)
                  JOIN res_partner_category cat ON cat.name->>'en_US' = tag.name
                  JOIN res_partner p ON p.ref = c.ref
                  ON CONFLICT DO NOTHING""")
    cr.execute('DELETE FROM mail_message WHERE model = %s AND res_id = %s', ['res.partner', template.id])
    template.unlink()
    return added


@timed('users')
def users():
    rows = read_csv('users.csv')[:TARGET]
    cr.execute("SELECT login FROM res_users")
    have = {r for (r,) in cr.fetchall()}
    rows = [r for r in rows if r['login'] not in have]
    if not rows:
        return 0
    template = env['res.users'].with_context(active_test=False).search([('login', '=', 'rig.template@reference.example')], limit=1)
    if not template:
        template = env['res.users'].create({'name': 'RIG TEMPLATE USER', 'login': 'rig.template@reference.example',
                                            'password': 'demo', 'lang': 'en_US',
                                            'group_ids': [Command.set([env.ref('base.group_user').id])]})
        template.write({k: v for k, v in {'tour_enabled': False, 'odoobot_state': 'disabled'}.items() if k in template._fields})
    commit_template()
    cr.execute('CREATE TEMP TABLE rig_users (ref text, name text, login text, lang text, n int) ON COMMIT DROP')
    execute_values(cr._obj, 'INSERT INTO rig_users VALUES %s', [
        (r['ref'], r['name'], r['login'], 'ar_001' if r['lang'] == 'ar' else 'en_US', int(r['ref'][1:])) for r in rows], page_size=5000)
    clone_sql('res_partner', template.partner_id.id, {
        'name': 'u.name', 'complete_name': 'u.name', 'ref': 'u.ref', 'email': 'u.login', 'email_normalized': 'u.login',
        'lang': 'u.lang', 'active': 'true',
    }, 'rig_users u', 'ORDER BY u.n')
    cr.execute("UPDATE res_partner p SET commercial_partner_id = p.id FROM rig_users u WHERE p.ref = u.ref")
    added = clone_sql('res_users', template.id, {
        'login': 'u.login', 'partner_id': 'p.id', 'active': 'true',
        'create_date': "now() - (u.n || ' minutes')::interval", 'write_date': "now() - (u.n || ' minutes')::interval",
    }, 'rig_users u JOIN res_partner p ON p.ref = u.ref', 'ORDER BY u.n')
    cr.execute("""INSERT INTO res_groups_users_rel (gid, uid)
                  SELECT r.gid, nu.id FROM res_groups_users_rel r
                  CROSS JOIN (SELECT id FROM res_users WHERE login IN (SELECT login FROM rig_users)) nu
                  WHERE r.uid = %s ON CONFLICT DO NOTHING""", [template.id])
    cr.execute("""INSERT INTO res_company_users_rel (cid, user_id)
                  SELECT r.cid, nu.id FROM res_company_users_rel r
                  CROSS JOIN (SELECT id FROM res_users WHERE login IN (SELECT login FROM rig_users)) nu
                  WHERE r.user_id = %s ON CONFLICT DO NOTHING""", [template.id])
    template.active = False
    return added


@timed('user-channels')
def user_channels():
    # Odoo makes every user a member of the channels that auto-subscribe one of their groups
    # (General for internal users). Bulk users were cloned in SQL, so add those memberships the
    # same way, cloning a member row Odoo made for that channel. Without them, the next change to
    # an internal user in the UI makes Odoo subscribe all 100,000 at once, which takes minutes and
    # would slow the reference unfairly.
    cr.execute("""SELECT r.discuss_channel_id, r.res_groups_id, min(m.id)
                  FROM discuss_channel_res_groups_rel r
                  JOIN discuss_channel_member m ON m.channel_id = r.discuss_channel_id AND m.partner_id IS NOT NULL
                  GROUP BY 1, 2""")
    added = 0
    for channel_id, group_id, member_id in cr.fetchall():
        added += clone_sql('discuss_channel_member', member_id, {
            'partner_id': 'u.partner_id', 'create_date': 'now()', 'write_date': 'now()',
            'seen_message_id': 'NULL', 'new_message_separator': '0', 'last_seen_dt': 'NULL',
        }, f"""(SELECT DISTINCT u.partner_id FROM res_users u
                  JOIN res_groups_users_rel g ON g.uid = u.id AND g.gid = {int(group_id)}
                  WHERE u.active AND u.login LIKE '%@staff.example'
                    AND NOT EXISTS (SELECT 1 FROM discuss_channel_member x
                                    WHERE x.channel_id = {int(channel_id)} AND x.partner_id = u.partner_id)) u""")
    return added


@timed('rates')
def rates():
    rows = read_csv('rates.csv')[:TARGET]
    company = env.ref('base.main_company')
    currencies = {c.name: c.id for c in env['res.currency'].with_context(active_test=False).search([])}
    cr.execute('CREATE TEMP TABLE rig_rates (currency_id int, name date, rate numeric) ON COMMIT DROP')
    # Odoo stores units of the currency per one unit of the company currency (AED).
    # Inverted in decimal arithmetic; the dataset itself carries AED per unit with six decimals.
    execute_values(cr._obj, 'INSERT INTO rig_rates VALUES %s',
                   [(currencies[r['currency']], r['date'], str((Decimal(1) / Decimal(r['rate'])).quantize(Decimal('1e-12'))))
                    for r in rows if r['currency'] in currencies],
                   page_size=5000)
    cr.execute("""INSERT INTO res_currency_rate (currency_id, company_id, name, rate, create_uid, write_uid, create_date, write_date)
                  SELECT r.currency_id, %s, r.name, r.rate, 1, 1, now(), now() FROM rig_rates r
                  WHERE NOT EXISTS (SELECT 1 FROM res_currency_rate x
                                    WHERE x.currency_id = r.currency_id AND x.name = r.name AND x.company_id = %s)""",
               [company.id, company.id])
    return cr.rowcount


def spread_join(alias, where, first, last):
    """FROM clause: generate_series(first..last) as g, each g joined to one row of res_partner
    matching `where`, round-robin. The row numbers live in an indexed temp table and the
    modulus is a literal, so PostgreSQL hash-joins instead of comparing every pair."""
    table = f'rig_spread_{alias}'
    cr.execute(f'DROP TABLE IF EXISTS {table}')
    cr.execute(f'CREATE TEMP TABLE {table} ON COMMIT DROP AS '
               f'SELECT row_number() OVER (ORDER BY id)::int AS rn, id FROM res_partner WHERE {where}')
    cr.execute(f'CREATE UNIQUE INDEX ON {table} (rn)')
    cr.execute(f'SELECT count(*) FROM {table}')
    total = cr.fetchone()[0]
    if not total:
        raise RuntimeError(f'no rows to spread over: {where}')
    return f'generate_series({int(first)}, {int(last)}) g JOIN {table} {alias} ON {alias}.rn = (g % {total}) + 1'


CONTACTS = "ref ~ '^C[0-9]{6}$'"


@timed('audit-messages')
def audit_messages():
    cr.execute("SELECT count(*) FROM mail_message WHERE message_id LIKE '<rig-audit-%%'")
    have = cr.fetchone()[0]
    if have >= TARGET:
        return 0
    # A real tracking message produced by Odoo itself, used as the template row.
    partner = env['res.partner'].search([('ref', '=', 'C000001')], limit=1)
    old_email = partner.email
    partner.with_context(tracking_disable=False, mail_notrack=False).write({'email': 'changed@template.example'})
    env.flush_all()
    cr.precommit.run()  # Odoo writes tracking messages in its pre-commit hook
    partner.with_context(tracking_disable=True).write({'email': old_email})
    env.flush_all()
    cr.execute("SELECT max(id) FROM mail_message WHERE model = 'res.partner' AND res_id = %s AND message_type = 'tracking'", [partner.id])
    template_id = cr.fetchone()[0]
    commit_template()
    added = clone_sql('mail_message', template_id, {
        'res_id': 'c.id',
        'body': "'<div>old' || g || '@mail.example → <b>new' || g || '@mail.example</b> <i>(Email)</i></div>'",
        'message_id': "'<rig-audit-' || g || '@reference>'",
        'date': "now() - (g * 3 || ' minutes')::interval",
        'create_date': "now() - (g * 3 || ' minutes')::interval",
        'write_date': "now() - (g * 3 || ' minutes')::interval",
    }, spread_join('c', CONTACTS, have + 1, TARGET))
    cr.execute('DELETE FROM mail_message WHERE id = %s', [template_id])
    return added


@timed('audit-realism')
def audit_realism():
    """Each cloned change log entry reads like a real one: the new value is the contact's actual
    e-mail, the old one a plausible earlier address, and the author is one of the company's
    users (not the system bot). Repairs rows of earlier rigs too; rows already right are left."""
    cr.execute("""CREATE TEMP TABLE rig_authors ON COMMIT DROP AS
                  SELECT row_number() OVER (ORDER BY u.id)::int AS rn, u.id AS uid, u.partner_id
                  FROM res_users u WHERE NOT u.share AND u.active AND u.login LIKE '%%@staff.example'""")
    cr.execute('CREATE UNIQUE INDEX ON rig_authors (rn)')
    cr.execute('SELECT count(*) FROM rig_authors')
    authors = cr.fetchone()[0]
    if not authors:
        return 0
    cr.execute(f"""UPDATE mail_message m
                   SET body = '<div>' || split_part(c.email, '@', 1) || '@previous-' || split_part(c.email, '@', 2)
                              || ' → <b>' || c.email || '</b> <i>(Email)</i></div>',
                       author_id = a.partner_id, create_uid = a.uid, write_uid = a.uid
                   FROM res_partner c, rig_authors a
                   WHERE m.message_id LIKE '<rig-audit-%%' AND c.id = m.res_id AND c.email IS NOT NULL
                     AND a.rn = (m.id % {int(authors)}) + 1
                     AND (m.author_id IS DISTINCT FROM a.partner_id OR position(c.email IN m.body::text) = 0)""")
    return cr.rowcount


@timed('attachments')
def attachments():
    cr.execute("SELECT count(*) FROM ir_attachment WHERE name ~ '^rig-document-[0-9]+\\.pdf$'")
    have = cr.fetchone()[0]
    if have >= TARGET:
        return 0
    partner = env['res.partner'].search([('ref', '=', 'C000002')], limit=1)
    template = env['ir.attachment'].create({
        'name': 'rig-template.pdf', 'res_model': 'res.partner', 'res_id': partner.id, 'mimetype': 'application/pdf',
        'raw': b'%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj 2 0 obj<</Type/Pages/Kids[]/Count 0>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n',
    })
    commit_template()
    added = clone_sql('ir_attachment', template.id, {
        'name': "'rig-document-' || g || '.pdf'", 'res_id': 'c.id',
        'create_date': "now() - (g * 4 || ' minutes')::interval", 'write_date': "now() - (g * 4 || ' minutes')::interval",
    }, spread_join('c', CONTACTS, have + 1, TARGET))
    # The clones share the template's stored file (same checksum), so the filestore holds one blob.
    cr.execute("UPDATE ir_attachment SET name = 'rig-document-0.pdf', res_id = %s WHERE id = %s", [partner.id, template.id])
    return added


@timed('attachment-content')
def attachment_content():
    """Every bulk attachment holds its own small document (kept in the database), so a search or
    download touches 100,000 distinct files, not one shared blob. Repairs rows of earlier rigs."""
    cr.execute("""UPDATE ir_attachment a
                  SET db_datas = convert_to(
                        chr(37) || 'PDF-1.4' || chr(10) || '1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj 2 0 obj<</Type/Pages/Kids[]/Count 0>>endobj' || chr(10)
                        || chr(37) || ' ' || a.name || ' for ' || coalesce(p.name, '') || ' ' || coalesce(p.ref, '') || chr(10)
                        || 'trailer<</Root 1 0 R>>' || chr(10) || repeat(chr(37), 2) || 'EOF' || chr(10), 'UTF8'),
                      store_fname = NULL
                  FROM res_partner p
                  WHERE a.name ~ '^rig-document-[0-9]+\\.pdf$' AND a.res_model = 'res.partner' AND p.id = a.res_id
                    AND (a.store_fname IS NOT NULL OR a.db_datas IS NULL
                         OR substr(a.db_datas, 1, 5) <> convert_to(chr(37) || 'PDF-', 'UTF8'))""")
    changed = cr.rowcount
    if changed:
        cr.execute("""UPDATE ir_attachment SET file_size = octet_length(db_datas),
                             checksum = substr(encode(sha256(db_datas), 'hex'), 1, 40)
                      WHERE name ~ '^rig-document-[0-9]+\\.pdf$' AND db_datas IS NOT NULL""")
    return changed


@timed('job-runs')
def job_runs():
    # Odoo Community keeps no per-run job history beyond ir.cron.progress, which its own vacuum
    # trims to one week; runs are spread over the last six days and topped up on each up.sh.
    cr.execute("DELETE FROM ir_cron_progress WHERE create_date < now() - interval '6 days 12 hours'")
    cr.execute("SELECT count(*) FROM ir_cron_progress")
    have = cr.fetchone()[0]
    if have >= TARGET:
        return 0
    n = TARGET - have
    cr.execute('SELECT count(*) FROM ir_cron')
    cron_total = cr.fetchone()[0]
    cr.execute(f"""INSERT INTO ir_cron_progress (cron_id, remaining, done, timed_out_counter, deactivate, create_uid, write_uid, create_date, write_date)
                   SELECT j.id, 0, (g * 7) % 50, 0, false, 1, 1,
                          now() - ((g * 5) || ' seconds')::interval, now() - ((g * 5) || ' seconds')::interval
                   FROM generate_series(1, {n}) g
                   JOIN (SELECT id, row_number() OVER (ORDER BY id) AS rn FROM ir_cron) j
                     ON j.rn = (g % {cron_total}) + 1""")
    return cr.rowcount


@timed('purchase-orders')
def purchase_orders():
    # Earlier rigs left confirmed history unreceived, which makes Odoo's purchase dashboard
    # query take minutes; bring such rows in line with what this step now creates.
    cr.execute("""UPDATE purchase_order_line l SET qty_received = l.product_qty, qty_received_manual = l.product_qty,
                         qty_invoiced = l.product_qty, qty_to_invoice = 0, qty_to_invoice_raw = 0,
                         date_planned = o.date_order + interval '7 days'
                  FROM purchase_order o WHERE o.id = l.order_id AND o.name ~ '^PO-H[0-9]{6}$'
                    AND o.state = 'purchase' AND l.qty_received < l.product_qty""")
    if cr.rowcount:
        cr.execute("""UPDATE purchase_order SET date_planned = date_order + interval '7 days',
                             receipt_status = CASE WHEN state = 'purchase' THEN 'full' END,
                             invoice_status = CASE WHEN state = 'purchase' THEN 'invoiced' ELSE 'no' END,
                             date_approve = CASE WHEN state = 'purchase' THEN date_order END,
                             locked = (state = 'purchase')
                      WHERE name ~ '^PO-H[0-9]{6}$'""")
        log(f'repaired {cr.rowcount} historical purchase orders')
    cr.execute("SELECT count(*) FROM purchase_order WHERE name ~ '^PO-H[0-9]{6}$'")
    have = cr.fetchone()[0]
    if have >= TARGET:
        return 0
    vendor = env['res.partner'].search([('ref', '=', 'C000003')], limit=1)
    product = env.ref('reference_rig.product_chair')
    po = env['purchase.order'].create({'partner_id': vendor.id, 'order_line': [Command.create({
        'product_id': product.id, 'product_qty': 2, 'price_unit': 750.0, 'tax_ids': [Command.clear()],
    })]})
    po.button_confirm()
    commit_template()
    line = po.order_line[:1]
    # Mostly confirmed orders, some drafts and cancellations, and one in 500 waiting for approval.
    state = "CASE WHEN g % 500 = 0 THEN 'to approve' WHEN g % 10 = 1 THEN 'cancel' WHEN g % 10 = 2 THEN 'draft' ELSE 'purchase' END"
    clone_sql('purchase_order', po.id, {
        'name': "'PO-H' || lpad(g::text, 6, '0')", 'partner_id': 'c.id', 'state': state, 'access_token': 'NULL',
        'date_order': "now() - (g * 7 || ' minutes')::interval",
        'create_date': "now() - (g * 7 || ' minutes')::interval", 'write_date': "now() - (g * 7 || ' minutes')::interval",
    }, spread_join('c', CONTACTS + ' AND is_company', have + 1, TARGET))
    added = cr.rowcount
    # History looks like a working company's: confirmed orders were received and billed.
    received = "CASE WHEN o.state = 'purchase' THEN t.product_qty ELSE 0 END"
    clone_sql('purchase_order_line', line.id, {
        'order_id': 'o.id', 'partner_id': 'o.partner_id', 'date_planned': "o.date_order + interval '7 days'",
        'qty_received': received, 'qty_received_manual': received, 'qty_invoiced': received,
        'qty_to_invoice': '0', 'qty_to_invoice_raw': '0',
    }, "purchase_order o", "WHERE o.name ~ '^PO-H[0-9]{6}$' AND NOT EXISTS (SELECT 1 FROM purchase_order_line l WHERE l.order_id = o.id)")
    cr.execute("""UPDATE purchase_order SET date_planned = date_order + interval '7 days',
                         receipt_status = CASE WHEN state = 'purchase' THEN 'full' END,
                         invoice_status = CASE WHEN state = 'purchase' THEN 'invoiced' ELSE 'no' END,
                         date_approve = CASE WHEN state = 'purchase' THEN date_order END,
                         locked = (state = 'purchase')
                  WHERE name ~ '^PO-H[0-9]{6}$'""")
    po.button_cancel()
    po.unlink() if po.state in ('draft', 'cancel') else None
    return added


@timed('purchase-buyer')
def purchase_buyer():
    # Bulk orders were cloned from a template the shell created as the superuser; a working
    # company's orders belong to its buyer, and Odoo's purchase screens group by buyer.
    buyer = env['res.users'].search([('login', '=', 'buyer')], limit=1)
    cr.execute("""UPDATE purchase_order SET user_id = %s
                  WHERE name ~ '^PO-H[0-9]{6}$' AND user_id IS DISTINCT FROM %s""", [buyer.id, buyer.id])
    return cr.rowcount


def volume():
    queries = {
        'contacts': ("res.partner", "SELECT count(*) FROM res_partner WHERE ref ~ '^C[0-9]{6}$'", 'Contacts from the shared dataset (contacts.csv)'),
        'contacts_all': ("res.partner", "SELECT count(*) FROM res_partner", 'All partner rows, including the partners behind users and companies'),
        'users': ("res.users", "SELECT count(*) FROM res_users WHERE share = false", 'Internal users (users.csv plus the task users)'),
        'currency_rates': ("res.currency.rate", "SELECT count(*) FROM res_currency_rate", 'Exchange rates (rates.csv)'),
        'audit_messages': ("mail.message", "SELECT count(*) FROM mail_message WHERE message_type = 'tracking'", 'Field-change tracking messages in the chatter (closest Odoo counterpart of an audit trail)'),
        'attachments': ("ir.attachment", "SELECT count(*) FROM ir_attachment WHERE res_model = 'res.partner'", 'Attachments on contacts'),
        'job_runs': ("ir.cron.progress", "SELECT count(*) FROM ir_cron_progress", 'Scheduled-action run records (closest Odoo Community counterpart of a job history; Odoo vacuums them after a week)'),
        'approvals': ("purchase.order", "SELECT count(*) FROM purchase_order", 'Purchase orders, the documents behind Odoo Community\'s only approval flow (two-step order approval)'),
        'approvals_pending': ("purchase.order", "SELECT count(*) FROM purchase_order WHERE state = 'to approve'", 'Purchase orders waiting for approval'),
        'companies': ("res.company", "SELECT count(*) FROM res_company", 'Companies and branches (not a 100,000-row list in either product)'),
        'roles': ("res.groups", "SELECT count(*) FROM res_groups", 'Access groups, the Odoo counterpart of roles (not a 100,000-row list)'),
    }
    out = {}
    for key, (model, sql, note) in queries.items():
        cr.execute(sql)
        out[key] = {'model': model, 'count': cr.fetchone()[0], 'note': note}
    return out


for step in (user_channels, setup, contacts, users, user_channels, rates, audit_messages, audit_realism, attachments, attachment_content, job_runs, purchase_orders, purchase_buyer):
    step()
# Planner statistics for the bulk-loaded tables. Without them PostgreSQL plans for empty tables
# and some Odoo screens (the purchase dashboard) take minutes instead of milliseconds, which
# would handicap the reference. Committed explicitly: `odoo shell` rolls back on exit.
cr.execute('ANALYZE')
cr.commit()
import odoo  # noqa: E402
log('VOLUME ' + json.dumps({'odoo_version': odoo.release.version, 'lists': volume()}))
