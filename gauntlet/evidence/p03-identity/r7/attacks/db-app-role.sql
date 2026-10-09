\echo -- no tenant bound
select count(*) as users from identity.users;
select count(*) as roles from identity.roles;
select count(*) as ucr from identity.user_company_roles;
select count(*) as audit from audit.audit_log;
\echo -- tenant set for the whole session (not transaction-local)
select set_config('app.tenant_id','0190a000-0000-7000-8000-000000000001', false);
select count(*) as users_session_bound from identity.users;
\echo -- bound to A transaction-local
begin;
select set_config('app.tenant_id','0190a000-0000-7000-8000-000000000001', true), set_config('app.tenant_tx', extract(epoch from now())::text, true), set_config('app.company_scope','all',true);
select count(*) filter (where tenant_id='0190a000-0000-7000-8000-000000000001') a_users, count(*) filter (where tenant_id<>'0190a000-0000-7000-8000-000000000001') other from identity.users;
select count(*) other_roles from identity.roles where tenant_id<>'0190a000-0000-7000-8000-000000000001';
select count(*) other_ucr from identity.user_company_roles where tenant_id<>'0190a000-0000-7000-8000-000000000001';
select count(*) other_sessions from identity.sessions where tenant_id<>'0190a000-0000-7000-8000-000000000001';
update identity.users set display_name='pwned' where tenant_id='0190a000-0000-7000-8000-000000000002';
insert into identity.roles (id, tenant_id, name_en, name_ar, permissions) values (gen_random_uuid(), '0190a000-0000-7000-8000-000000000002', 'x','x','{}');
rollback;
\echo -- escape attempts
alter table identity.users disable row level security;
set role erp_owner;
set row_security = off;
select count(*) from identity.users;
select rolname, rolbypassrls, rolsuper from pg_roles where rolname like 'erp%' or rolbypassrls;
