\pset pager off
SELECT rolname, rolbypassrls, rolsuper, rolcanlogin FROM pg_roles WHERE rolname LIKE 'erp%' ORDER BY 1;
SELECT tenant_id, count(*) FROM audit.entries GROUP BY 1;
SELECT grantee, string_agg(privilege_type, ',' ORDER BY privilege_type) FROM information_schema.role_table_grants WHERE table_schema='audit' AND table_name='entries' GROUP BY 1;
SELECT polname, polpermissive, polcmd, polroles::regrole[], pg_get_expr(polqual, polrelid) q, pg_get_expr(polwithcheck, polrelid) wc FROM pg_policy WHERE polrelid='audit.entries'::regclass;
SELECT p.proname, p.prosecdef, pg_get_userbyid(p.proowner) owner, p.proconfig FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='audit';
SELECT relrowsecurity, relforcerowsecurity FROM pg_class WHERE oid='audit.entries'::regclass;
-- every table carrying the capture trigger, its RLS state, and whether it has tenant_id
SELECT c.oid::regclass AS tbl, c.relrowsecurity rls, c.relforcerowsecurity force_rls,
  EXISTS(SELECT 1 FROM pg_attribute a WHERE a.attrelid=c.oid AND a.attname='tenant_id') has_tenant,
  has_table_privilege('erp_app', c.oid, 'INSERT') app_ins, has_table_privilege('erp_worker', c.oid, 'INSERT') worker_ins
FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_proc p ON p.oid=t.tgfoid
WHERE p.proname='capture' AND NOT t.tgisinternal ORDER BY 1;
-- tenant tables WITHOUT the trigger
SELECT c.oid::regclass FROM pg_class c JOIN pg_attribute a ON a.attrelid=c.oid AND a.attname='tenant_id' JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE c.relkind IN ('r','p') AND n.nspname NOT IN ('audit','pg_catalog') AND NOT EXISTS (SELECT 1 FROM pg_trigger t JOIN pg_proc p ON p.oid=t.tgfoid WHERE t.tgrelid=c.oid AND p.proname='capture') ORDER BY 1;
-- schemas where erp_app or erp_worker can CREATE
SELECT nspname, has_schema_privilege('erp_app', oid, 'CREATE') app_create, has_schema_privilege('erp_worker', oid, 'CREATE') worker_create FROM pg_namespace WHERE nspname NOT LIKE 'pg_%' AND nspname<>'information_schema';
SELECT has_function_privilege('erp_app','audit.capture()','EXECUTE') app_exec, has_function_privilege('erp_worker','audit.capture()','EXECUTE') worker_exec, has_database_privilege('erp_app', current_database(), 'CREATE') app_db_create, has_database_privilege('erp_app', current_database(), 'TEMP') app_temp;
