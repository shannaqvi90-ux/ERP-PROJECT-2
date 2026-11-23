\pset pager off
\set ON_ERROR_STOP off
SELECT current_user;
-- no tenant bound
SELECT count(*) AS rows_no_tenant FROM audit.entries;
BEGIN;
SELECT set_config('app.tenant_id','0190a000-0000-7000-8000-000000000002', true), set_config('app.tenant_tx', extract(epoch from now())::text, true), set_config('app.company_scope','all',true), set_config('app.company_tx', extract(epoch from now())::text, true);
SELECT tenant_id, count(*) AS bound_to_B FROM audit.entries GROUP BY 1;
ROLLBACK;
BEGIN;
SELECT set_config('app.tenant_id','0190a000-0000-7000-8000-000000000001', true), set_config('app.tenant_tx', extract(epoch from now())::text, true);
INSERT INTO audit.entries (tenant_id, occurred_at, actor_id, actor_kind, table_schema, table_name, record_id, action, changes) VALUES ('0190a000-0000-7000-8000-000000000001', now() - interval '400 days', gen_random_uuid(), 'user', 'identity','users', gen_random_uuid(), 'update', '{}'::jsonb);
ROLLBACK;
BEGIN;
SELECT set_config('app.tenant_id','0190a000-0000-7000-8000-000000000001', true), set_config('app.tenant_tx', extract(epoch from now())::text, true);
UPDATE audit.entries SET actor_kind='x' WHERE id IN (SELECT id FROM audit.entries LIMIT 1);
ROLLBACK;
BEGIN;
SELECT set_config('app.tenant_id','0190a000-0000-7000-8000-000000000001', true), set_config('app.tenant_tx', extract(epoch from now())::text, true);
DELETE FROM audit.entries WHERE id IN (SELECT id FROM audit.entries LIMIT 1);
ROLLBACK;
TRUNCATE audit.entries;
SELECT audit.capture();
CREATE TEMP TABLE t(id uuid, tenant_id uuid);
CREATE TRIGGER x AFTER INSERT ON t FOR EACH ROW EXECUTE FUNCTION audit.capture();
-- A tenant-A change makes an audit row in A, with the trigger as writer
BEGIN;
SELECT set_config('app.tenant_id','0190a000-0000-7000-8000-000000000001', true), set_config('app.tenant_tx', extract(epoch from now())::text, true), set_config('app.company_scope','all',true), set_config('app.company_tx', extract(epoch from now())::text, true), set_config('app.actor_kind','user',true);
UPDATE currency.currencies SET name_en = name_en || ' x' WHERE id = (SELECT id FROM currency.currencies ORDER BY code LIMIT 1) RETURNING code;
SELECT tenant_id, table_name, action, actor_kind, changes FROM audit.entries WHERE table_name='currencies' ORDER BY occurred_at DESC LIMIT 1;
-- try to make the trigger write into tenant B via a row carrying B's tenant id
UPDATE currency.currencies SET tenant_id='0190a000-0000-7000-8000-000000000002' WHERE id = (SELECT id FROM currency.currencies ORDER BY code LIMIT 1);
ROLLBACK;
