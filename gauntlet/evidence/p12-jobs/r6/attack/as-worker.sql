\echo == as erp_worker (its own login)
SELECT current_user, session_user;
SET ROLE erp_app;
SET ROLE erp_owner;
SELECT count(*), count(DISTINCT tenant_id) FROM jobs.jobs;
SELECT title_en FROM jobs.jobs LIMIT 1;
SELECT title_ar FROM jobs.jobs LIMIT 1;
SELECT payload FROM jobs.schedules LIMIT 1;
SELECT * FROM jobs.job_attempts LIMIT 1;
SELECT * FROM identity.users LIMIT 1;
UPDATE jobs.jobs SET status = 'queued' WHERE false;
CREATE TABLE jobs.x (a int);
CREATE TABLE public.x (a int);
