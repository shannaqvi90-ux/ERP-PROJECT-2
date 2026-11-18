-- as erp_app with tenant A set: read tenant B's canary through tables and every value_* function
BEGIN;
SELECT set_config('app.tenant_id','0190a000-0000-7000-8000-000000000001',true), set_config('app.tenant_tx', extract(epoch from now())::text, true);
SELECT 'value_text(B field,B record)=' || coalesce(customfields.value_text('01a1ec21-edc3-7770-85c3-2b285af49f16','01a1ec21-eedc-714a-9fd8-4e0930e68813'),'<null>');
SELECT 'value_date_trunc(B date field,B record)=' || coalesce(customfields.value_date_trunc('01a1ec21-eebb-78c2-8752-74ae7eca6fb4','01a1ec21-eedc-714a-9fd8-4e0930e68813','year')::text,'<null>');
SELECT 'field_values rows with CANARY=' || count(*) FROM customfields.field_values WHERE text_value LIKE '%CANARYB1794963008929%';
SELECT 'B definitions visible=' || count(*) FROM customfields.definitions WHERE tenant_id='0190a000-0000-7000-8000-000000000002';
SELECT 'all definitions visible=' || count(*) FROM customfields.definitions;
SELECT 'B record values visible=' || count(*) FROM customfields.field_values v WHERE v.record_id='01a1ec21-eedc-714a-9fd8-4e0930e68813';
SAVEPOINT s; INSERT INTO customfields.field_values (id, record_type, record_id, field_id, code, tenant_id, text_value, created_at) VALUES (gen_random_uuid(),'contacts.contacts','01a1ec21-eedc-714a-9fd8-4e0930e68813','01a1ec21-edc3-7770-85c3-2b285af49f16','x','0190a000-0000-7000-8000-000000000002','x', now()); ROLLBACK TO s;
SELECT 'update of B values rows=' || (WITH u AS (UPDATE customfields.field_values SET text_value='pwn' WHERE tenant_id='0190a000-0000-7000-8000-000000000002' RETURNING 1) SELECT count(*) FROM u);
ROLLBACK;
BEGIN;
SELECT set_config('app.tenant_id','0190a000-0000-7000-8000-000000000002',true), set_config('app.tenant_tx', extract(epoch from now())::text, true);
SELECT 'control as B: value_text=' || coalesce(customfields.value_text('01a1ec21-edc3-7770-85c3-2b285af49f16','01a1ec21-eedc-714a-9fd8-4e0930e68813'),'<null>') || ' date_trunc=' || coalesce(customfields.value_date_trunc('01a1ec21-eebb-78c2-8752-74ae7eca6fb4','01a1ec21-eedc-714a-9fd8-4e0930e68813','year')::text,'<null>');
ROLLBACK;
