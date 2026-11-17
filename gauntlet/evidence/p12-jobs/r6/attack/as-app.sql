\echo == as erp_app (its own login): role switches and DDL
SELECT current_user, session_user;
SET ROLE erp_owner;
SET ROLE erp_worker;
SET ROLE postgres;
ALTER TABLE jobs.jobs DISABLE ROW LEVEL SECURITY;
ALTER TABLE jobs.jobs NO FORCE ROW LEVEL SECURITY;
SET row_security = off;
SELECT count(*) FROM jobs.jobs;
RESET row_security;
SELECT count(*) AS unbound_jobs FROM jobs.jobs;
