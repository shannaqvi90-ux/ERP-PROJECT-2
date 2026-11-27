\set A '0190a000-0000-7000-8000-000000000001'
\set B '0190a000-0000-7000-8000-000000000002'
BEGIN;
SET LOCAL ROLE erp_app;
SELECT set_config('app.tenant_id', :'A', true), set_config('app.tenant_tx', extract(epoch from now())::text, true);
SELECT 'bound tenant', erp.current_tenant_id();
SELECT 'A sees own definitions', count(*) FROM customfields.definitions;
SELECT 'A sees B definitions', count(*) FROM customfields.definitions WHERE tenant_id = :'B';
SELECT 'A sees B values', count(*) FROM customfields.field_values WHERE tenant_id = :'B';
SELECT 'value_text on B rows (as A)', count(customfields.value_text(v.field_id, v.record_id)) FROM (SELECT field_id, record_id FROM customfields.field_values WHERE false) v;
WITH u AS (UPDATE customfields.field_values SET text_value = 'pwn' WHERE tenant_id = :'B' RETURNING 1) SELECT 'update B values rows', count(*) FROM u;
WITH d AS (DELETE FROM customfields.definitions WHERE tenant_id = :'B' RETURNING 1) SELECT 'delete B definitions rows', count(*) FROM d;
SAVEPOINT s;
INSERT INTO customfields.definitions (id, tenant_id, record_type, code, name_en, name_ar, field_type, decimals, sort_order, searchable) VALUES (gen_random_uuid(), :'B', 'contacts.contacts', 'cfPwn', 'pwn', 'pwn', 1, 0, 1, false);
ROLLBACK TO s;
ROLLBACK;
BEGIN;
SET LOCAL ROLE erp_app;
SELECT set_config('app.tenant_id', :'B', true), set_config('app.tenant_tx', extract(epoch from now())::text, true);
SELECT 'control B sees own definitions', count(*) FROM customfields.definitions;
SELECT 'control B sees own values', count(*) FROM customfields.field_values;
ROLLBACK;
