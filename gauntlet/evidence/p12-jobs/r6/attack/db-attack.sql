\echo == erp_app, no tenant bound
SET ROLE erp_app;
SELECT 'jobs' t, count(*) FROM jobs.jobs UNION ALL SELECT 'attempts', count(*) FROM jobs.job_attempts UNION ALL SELECT 'schedules', count(*) FROM jobs.schedules;
RESET ROLE;
\echo == erp_app bound to A (alnoor)
BEGIN;
SET LOCAL ROLE erp_app;
SELECT set_config('app.tenant_id','0190a000-0000-7000-8000-000000000001',true), set_config('app.tenant_tx', extract(epoch from now())::text, true);
SELECT tenant_id, count(*) FROM jobs.jobs GROUP BY 1;
SELECT tenant_id, count(*) FROM jobs.job_attempts GROUP BY 1;
SELECT tenant_id, count(*) FROM jobs.schedules GROUP BY 1;
\echo == UPDATE / DELETE B rows as A
UPDATE jobs.jobs SET cancel_requested = true WHERE tenant_id = '0190a000-0000-7000-8000-000000000002';
DELETE FROM jobs.schedules WHERE tenant_id = '0190a000-0000-7000-8000-000000000002';
DELETE FROM jobs.job_attempts WHERE tenant_id = '0190a000-0000-7000-8000-000000000002';
\echo == INSERT a schedule for B as A
INSERT INTO jobs.schedules (id, tenant_id, name, type, cron, payload, is_enabled) VALUES (gen_random_uuid(), '0190a000-0000-7000-8000-000000000002', 'critic-r6', 'jobs.cleanup', '* * * * *', '{}', true);
ROLLBACK;
\echo == switch role from erp_app to erp_worker
BEGIN; SET LOCAL ROLE erp_app; SET LOCAL ROLE erp_worker; SELECT current_user; ROLLBACK;
\echo == erp_app to erp_owner
BEGIN; SET LOCAL ROLE erp_app; SET LOCAL ROLE erp_owner; SELECT current_user; ROLLBACK;
\echo == erp_app alter table
BEGIN; SET LOCAL ROLE erp_app; ALTER TABLE jobs.jobs DISABLE ROW LEVEL SECURITY; ROLLBACK;
\echo == erp_worker: column grants and attempts at other columns
SELECT rolname, rolsuper, rolbypassrls, rolcanlogin FROM pg_roles WHERE rolname IN ('erp_app','erp_worker','erp_owner');
SELECT relname, relrowsecurity, relforcerowsecurity FROM pg_class WHERE relnamespace='jobs'::regnamespace AND relkind='r';
BEGIN; SET LOCAL ROLE erp_worker; SELECT count(*), count(DISTINCT tenant_id) FROM jobs.jobs; SAVEPOINT a; SELECT title_en FROM jobs.jobs LIMIT 1; ROLLBACK TO a; SELECT payload FROM jobs.schedules LIMIT 1; ROLLBACK;
BEGIN; SET LOCAL ROLE erp_worker; SELECT count(*) FROM jobs.job_attempts; ROLLBACK;
BEGIN; SET LOCAL ROLE erp_worker; UPDATE jobs.jobs SET status='queued' WHERE false; ROLLBACK;
SELECT n.nspname||'.'||c.relname||'.'||a.attname FROM pg_attribute a JOIN pg_class c ON c.oid=a.attrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE a.attnum>0 AND NOT a.attisdropped AND c.relkind IN ('r','v','m','p','f') AND n.nspname NOT IN ('pg_catalog','information_schema','pg_toast') AND has_column_privilege('erp_worker',c.oid,a.attnum,'SELECT') ORDER BY 1;
