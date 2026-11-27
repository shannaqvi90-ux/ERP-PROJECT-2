BEGIN;
SELECT set_config('app.tenant_id','0190a000-0000-7000-8000-000000000001',true), set_config('app.tenant_tx', extract(epoch from now())::text, true);
UPDATE customfields.field_values SET text_value='pwn' WHERE tenant_id='0190a000-0000-7000-8000-000000000002';
DELETE FROM customfields.definitions WHERE tenant_id='0190a000-0000-7000-8000-000000000002';
ROLLBACK;
